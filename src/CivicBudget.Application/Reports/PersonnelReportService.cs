using CivicBudget.Application.Persistence;
using CivicBudget.Application.Personnel;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Personnel;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Reports;

/// <summary>
/// The personnel reports for a budget version. The roster is a list of positions, so a department
/// user gets their own departments' (the same rule as their lines). Cost by fund and the benefits
/// summary are about the whole government's payroll and return null for department users, like the
/// other whole-fund reports.
/// </summary>
public interface IPersonnelReportService
{
    Task<PositionRosterDto?> RosterAsync(Guid budgetVersionId, CancellationToken ct = default);
    Task<PersonnelCostDto?> CostByFundAsync(Guid budgetVersionId, CancellationToken ct = default);
    Task<BenefitsSummaryDto?> BenefitsAsync(Guid budgetVersionId, CancellationToken ct = default);
}

public sealed class PersonnelReportService(
    ICivicBudgetDbContextFactory dbFactory,
    ICurrentUser currentUser,
    TimeProvider clock) : IPersonnelReportService
{
    public async Task<PositionRosterDto?> RosterAsync(Guid budgetVersionId, CancellationToken ct = default)
    {
        Loaded? loaded = await LoadAsync(budgetVersionId, wholeGovernmentOnly: false, ct);
        if (loaded is null)
        {
            return null;
        }

        return loaded.Rules is null
            ? new PositionRosterDto(loaded.Header, [], HasSettings: false)
            : new PositionRosterDto(loaded.Header,
                PersonnelReportBuilder.Roster(loaded.Positions, loaded.Rules, loaded.FiscalYearStartMonth, loaded.Funds.ToDictionary(f => f.Key, f => f.Value.Code)),
                HasSettings: true);
    }

    public async Task<PersonnelCostDto?> CostByFundAsync(Guid budgetVersionId, CancellationToken ct = default)
    {
        Loaded? loaded = await LoadAsync(budgetVersionId, wholeGovernmentOnly: true, ct);
        if (loaded is null)
        {
            return null;
        }

        (IReadOnlyList<PersonnelCostFundDto> funds, PersonnelCostRowDto total) = PersonnelReportBuilder.CostByFund(loaded.Positions, loaded.Funds);
        return new PersonnelCostDto(loaded.Header, funds, total, loaded.Rules is not null);
    }

    public async Task<BenefitsSummaryDto?> BenefitsAsync(Guid budgetVersionId, CancellationToken ct = default)
    {
        Loaded? loaded = await LoadAsync(budgetVersionId, wholeGovernmentOnly: true, ct);
        if (loaded is null)
        {
            return null;
        }

        return loaded.Rules is null
            ? new BenefitsSummaryDto(loaded.Header, 0, [], [], 0m, 0m, 0m, 0m, 0m, HasSettings: false)
            : PersonnelReportBuilder.Benefits(loaded.Header, loaded.Positions, loaded.Rules);
    }

    private sealed record Loaded(
        ReportHeaderDto Header, PayrollRules? Rules, int FiscalYearStartMonth,
        IReadOnlyList<ReportPosition> Positions, IReadOnlyDictionary<Guid, (string Code, string Name)> Funds);

    private async Task<Loaded?> LoadAsync(Guid budgetVersionId, bool wholeGovernmentOnly, CancellationToken ct)
    {
        if (wholeGovernmentOnly && currentUser.IsDepartmentUser())
        {
            return null;
        }

        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        BudgetVersion? version = await PersonnelData.VersionsWithPositions(db).FirstOrDefaultAsync(v => v.Id == budgetVersionId, ct);
        if (version is null)
        {
            return null;
        }

        FiscalYear year = await db.FiscalYears.SingleAsync(f => f.Id == version.FiscalYearId, ct);
        Government government = await db.Governments.SingleAsync(g => g.Id == version.GovernmentId, ct);
        PayrollRules? rules = await PersonnelData.RulesAsync(db, year, ct);
        Dictionary<Guid, Department> departments = await db.Departments.ToDictionaryAsync(d => d.Id, ct);
        Dictionary<Guid, (string Code, string Name)> funds = await db.Funds.ToDictionaryAsync(f => f.Id, f => (f.Code, f.Name), ct);
        var header = new ReportHeaderDto(version.Id, government.Name, year.Year, version.Label, version.Status, version.ResolutionNumber,
            currentUser.DisplayName ?? "", clock.GetUtcNow());

        IEnumerable<Position> visible = version.Positions;
        if (currentUser.IsDepartmentUser())
        {
            visible = visible.Where(p => currentUser.DepartmentIds.Contains(p.DepartmentId));
        }

        // A position the year's settings no longer price (a plan was changed after adoption) is left
        // out rather than guessed at; its personnel page says why.
        List<ReportPosition> positions = rules is null ? [] : visible
            .Select(p => (Position: p, Details: p.ToDetails()))
            .Where(x => x.Details.Problems(rules).Count == 0)
            .Select(x => new ReportPosition(departments[x.Position.DepartmentId].Code, departments[x.Position.DepartmentId].Name,
                x.Details, PositionCostCalculator.Calculate(x.Details, rules)))
            .ToList();
        return new Loaded(header, rules, government.FiscalYearStartMonth, positions, funds);
    }
}
