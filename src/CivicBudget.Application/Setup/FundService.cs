using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Application.Tenancy;
using CivicBudget.Domain.Funds;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Setup;

public sealed record FundDto(Guid Id, string Code, string Name, FundCategory Category, FundGroup Group, string? Description, bool IsActive);

/// <summary>Create (Id null) or update (Id set) a fund.</summary>
public sealed record SaveFundRequest(Guid? Id, string Code, string Name, FundCategory Category, string? Description);

public sealed class SaveFundRequestValidator : AbstractValidator<SaveFundRequest>
{
    public SaveFundRequestValidator()
    {
        RuleFor(r => r.Code).NotEmpty().MaximumLength(Fund.CodeMaxLength);
        RuleFor(r => r.Name).NotEmpty().MaximumLength(Fund.NameMaxLength);
        RuleFor(r => r.Category).IsInEnum();
        RuleFor(r => r.Description).MaximumLength(2000);
    }
}

public interface IFundService
{
    Task<IReadOnlyList<FundDto>> ListAsync(bool includeInactive, CancellationToken ct = default);
    Task<FundDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Result<Guid>> SaveAsync(SaveFundRequest request, CancellationToken ct = default);
    Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default);
}

/// <summary>
/// Fund maintenance, and the pattern every setup service follows: check the caller's role, refuse if
/// the ERP owns the chart, validate the request, load through the tenant-filtered context, change
/// the entity through its own methods, save. Uniqueness is checked here so the user gets a friendly
/// message, and a unique index enforces it anyway in case two people save at the same moment.
/// </summary>
public sealed class FundService(
    ICivicBudgetDbContextFactory dbFactory,
    ITenantContext tenant,
    ICurrentUser currentUser,
    IValidator<SaveFundRequest> validator) : IFundService
{
    public async Task<IReadOnlyList<FundDto>> ListAsync(bool includeInactive, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Funds
            .Where(f => includeInactive || f.IsActive)
            .OrderBy(f => f.Code)
            .Select(f => ToDto(f))
            .ToListAsync(ct);
    }

    public async Task<FundDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Fund? fund = await db.Funds.FirstOrDefaultAsync(f => f.Id == id, ct);
        return fund is null ? null : ToDto(fund);
    }

    public async Task<Result<Guid>> SaveAsync(SaveFundRequest request, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure<Guid>(SetupNotAllowed.FiscalAuthority);
        }

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
        bool codeTaken = await db.Funds.AnyAsync(f => f.Code == code && f.Id != request.Id, ct);
        if (codeTaken)
        {
            return Result.Failure<Guid>(nameof(request.Code), $"Fund code {code} is already in use.");
        }

        Fund fund;
        if (request.Id is { } id)
        {
            fund = await db.Funds.FirstOrDefaultAsync(f => f.Id == id, ct)
                ?? throw new KeyNotFoundException($"Fund {id} was not found.");
            fund.Update(code, request.Name, request.Category, request.Description);
        }
        else
        {
            fund = new Fund(RequireTenant(), code, request.Name, request.Category, request.Description);
            db.Funds.Add(fund);
        }

        await db.SaveChangesAsync(ct);
        return Result.Success(fund.Id);
    }

    public async Task<Result> SetActiveAsync(Guid id, bool isActive, CancellationToken ct = default)
    {
        if (!currentUser.IsFiscalAuthority())
        {
            return Result.Failure(SetupNotAllowed.FiscalAuthority);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        if (await ChartOwnership.RefuseIfErpManagedAsync(db, tenant.GovernmentId, ct) is { } managed)
        {
            return managed;
        }

        Fund? fund = await db.Funds.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (fund is null)
        {
            return Result.Failure("Fund was not found.");
        }

        if (isActive)
        {
            fund.Reactivate();
        }
        else
        {
            fund.Deactivate();
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }

    private Guid RequireTenant() =>
        tenant.GovernmentId ?? throw new InvalidOperationException("No tenant is set; cannot create a fund.");

    private static FundDto ToDto(Fund f) => new(f.Id, f.Code, f.Name, f.Category, f.Category.ToGroup(), f.Description, f.IsActive);
}
