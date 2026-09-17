using CivicBudget.Application.Common;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Tenancy;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Governments;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Setup;

public sealed record FiscalYearDto(Guid Id, int Year, string Label, DateOnly StartDate, DateOnly EndDate, bool IsClosed, int VersionCount);

public sealed record CreateFiscalYearRequest(int Year);

public sealed class CreateFiscalYearRequestValidator : AbstractValidator<CreateFiscalYearRequest>
{
    public CreateFiscalYearRequestValidator()
    {
        RuleFor(r => r.Year).InclusiveBetween(2000, 2100);
    }
}

public interface IFiscalYearService
{
    Task<IReadOnlyList<FiscalYearDto>> ListAsync(CancellationToken ct = default);
    Task<Result<Guid>> CreateAsync(CreateFiscalYearRequest request, CancellationToken ct = default);
    Task<Result> SetClosedAsync(Guid id, bool isClosed, CancellationToken ct = default);
}

/// <summary>
/// Fiscal year maintenance. Years are created, never edited: their dates derive from the government's
/// fiscal year start month at creation time, and a year with budget versions must keep its dates.
/// </summary>
public sealed class FiscalYearService(
    ICivicBudgetDbContextFactory dbFactory,
    ITenantContext tenant,
    IValidator<CreateFiscalYearRequest> validator) : IFiscalYearService
{
    public async Task<IReadOnlyList<FiscalYearDto>> ListAsync(CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        return await db.FiscalYears
            .OrderByDescending(fy => fy.Year)
            .Select(fy => new FiscalYearDto(
                fy.Id, fy.Year, "FY" + fy.Year, fy.StartDate, fy.EndDate, fy.IsClosed,
                db.BudgetVersions.Count(v => v.FiscalYearId == fy.Id)))
            .ToListAsync(ct);
    }

    public async Task<Result<Guid>> CreateAsync(CreateFiscalYearRequest request, CancellationToken ct = default)
    {
        if (await validator.ValidateToResultAsync(request, ct) is { } invalid)
        {
            return Result.Failure<Guid>(invalid.Errors);
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);

        if (await db.FiscalYears.AnyAsync(fy => fy.Year == request.Year, ct))
        {
            return Result.Failure<Guid>(nameof(request.Year), $"FY{request.Year} already exists.");
        }

        Guid governmentId = tenant.GovernmentId ?? throw new InvalidOperationException("No tenant is set; cannot create a fiscal year.");
        Government government = await db.Governments.SingleAsync(g => g.Id == governmentId, ct);

        var fiscalYear = new FiscalYear(governmentId, request.Year, government.FiscalYearStartMonth);
        db.FiscalYears.Add(fiscalYear);
        await db.SaveChangesAsync(ct);
        return Result.Success(fiscalYear.Id);
    }

    public async Task<Result> SetClosedAsync(Guid id, bool isClosed, CancellationToken ct = default)
    {
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        FiscalYear? fiscalYear = await db.FiscalYears.FirstOrDefaultAsync(fy => fy.Id == id, ct);
        if (fiscalYear is null)
        {
            return Result.Failure("Fiscal year was not found.");
        }

        if (isClosed)
        {
            fiscalYear.Close();
        }
        else
        {
            fiscalYear.Reopen();
        }

        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
