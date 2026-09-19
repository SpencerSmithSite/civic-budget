using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Tenancy;
using CivicBudget.Domain.Departments;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Setup;

public sealed record DepartmentDto(Guid Id, string Code, string Name, string? Description, bool IsActive);

public sealed record SaveDepartmentRequest(Guid? Id, string Code, string Name, string? Description);

public sealed class SaveDepartmentRequestValidator : AbstractValidator<SaveDepartmentRequest>
{
    public SaveDepartmentRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(Department.CodeMaxLength);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Department.NameMaxLength);
        RuleFor(r => r.Description).MaximumLength(2000);
    }
}

public interface IDepartmentService
{
    Task<IReadOnlyList<DepartmentDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<DepartmentDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<Guid>> SaveAsync(SaveDepartmentRequest request, CancellationToken ct = default);
    Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
}

/// <summary>Department maintenance. Same shape as <see cref="FundService"/>; see its remarks.</summary>
public sealed class DepartmentService(
    ICivicBudgetDbContextFactory dbFactory,
    ITenantContext tenant,
    IValidator<SaveDepartmentRequest> validator) : IDepartmentService
{
    public async Task<IReadOnlyList<DepartmentDto>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Departments
            .Where(d => includeInactive || d.IsActive)
            .OrderBy(d => d.Code)
            .Select(d => ToDto(d))
            .ToListAsync(ct);
    }

    public async Task<DepartmentDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Department? department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        return department is null ? null : ToDto(department);
    }

    public async Task<Result<Guid>> SaveAsync(SaveDepartmentRequest request, CancellationToken ct = default)
    {
        if (await validator.ValidateToResultAsync(request, ct) is { } invalid)
        {
            return Result.Failure<Guid>(invalid.Errors);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        if (await ChartOwnership.RefuseIfErpManagedAsync(db, tenant.GovernmentId, ct) is { } managed)
        {
            return Result.Failure<Guid>(managed.Errors);
        }

        string code = request.Code.Trim();
        if (await db.Departments.AnyAsync(d => d.Code == code && d.Id != request.Id, ct))
        {
            return Result.Failure<Guid>(nameof(request.Code), $"Department code {code} is already in use.");
        }

        Department department;
        if (request.Id is { } id)
        {
            department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct)
                ?? throw new KeyNotFoundException($"Department {id} was not found.");
            department.Update(code, request.Name, request.Description);
        }
        else
        {
            Guid governmentId = tenant.GovernmentId ?? throw new InvalidOperationException("No tenant is set; cannot create a department.");
            department = new Department(governmentId, code, request.Name, request.Description);
            db.Departments.Add(department);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(department.Id);
    }

    public async Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        if (await ChartOwnership.RefuseIfErpManagedAsync(db, tenant.GovernmentId, ct) is { } managed)
        {
            return managed;
        }
        Department? department = await db.Departments.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (department is null)
        {
            return Result.Failure("Department was not found.");
        }

        if (isActive)
        {
            department.Reactivate();
        }
        else
        {
            department.Deactivate();
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static DepartmentDto ToDto(Department d) => new(d.Id, d.Code, d.Name, d.Description, d.IsActive);
}
