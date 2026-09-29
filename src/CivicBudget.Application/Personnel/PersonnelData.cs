using CivicBudget.Application.Persistence;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Personnel;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Personnel;

/// <summary>The loads every personnel operation shares, so the includes and the year's rules are written once.</summary>
internal static class PersonnelData
{
    /// <summary>
    /// A budget version with its lines (and their fund, department, and account), every position, and its
    /// multi-year plan: amendments and next year's budget copy all of it, so a load that left the plan out
    /// would silently drop it.
    /// </summary>
    public static IQueryable<BudgetVersion> VersionsWithPositions(ICivicBudgetDbContext db) =>
        db.BudgetVersions
            .Include(v => v.Lines).ThenInclude(l => l.Fund)
            .Include(v => v.Lines).ThenInclude(l => l.Department)
            .Include(v => v.Lines).ThenInclude(l => l.Account)
            .Include(v => v.BeginningBalances)
            .Include(v => v.DepartmentRequests)
            .Include(v => v.Positions).ThenInclude(p => p.Funds)
            .Include(v => v.Positions).ThenInclude(p => p.Coverages)
            .Include(v => v.Positions).ThenInclude(p => p.ExtraPay)
            .Include(v => v.PlanAssumptions)
            .Include(v => v.Lines).ThenInclude(l => l.PlannedAmounts);

    public static Task<PersonnelSettings?> SettingsAsync(ICivicBudgetDbContext db, int fiscalYear, CancellationToken ct) =>
        db.PersonnelSettings
            .Include(s => s.RetirementPlans)
            .Include(s => s.InsurancePlans)
            .Include(s => s.ExtraPay)
            .Include(s => s.LongevitySchedules).ThenInclude(l => l.Steps)
            .Include(s => s.PayScales).ThenInclude(p => p.Rates)
            .FirstOrDefaultAsync(s => s.FiscalYear == fiscalYear, ct);

    /// <summary>The year's settings as rules, or null when the year has not been set up.</summary>
    public static async Task<PayrollRules?> RulesAsync(ICivicBudgetDbContext db, FiscalYear year, CancellationToken ct) =>
        (await SettingsAsync(db, year.Year, ct))?.ToRules(year.StartDate, year.EndDate);

    /// <summary>Every fund and account of the government, so applying personnel can create any line a position costs into.</summary>
    public static async Task<PersonnelChart> ChartAsync(ICivicBudgetDbContext db, Department department, CancellationToken ct) =>
        new(department,
            await db.Funds.ToDictionaryAsync(f => f.Id, ct),
            await db.Accounts.ToDictionaryAsync(a => a.Id, ct));

    /// <summary>
    /// Re-applies every department's positions in a version under the given rules, and returns how many
    /// lines changed. Departments with calculated lines but no positions left are included, so their
    /// lines go back to typed zeros.
    /// </summary>
    public static async Task<int> ApplyAllAsync(ICivicBudgetDbContext db, BudgetVersion version, PayrollRules rules, CancellationToken ct)
    {
        List<Guid> departmentIds = version.Positions.Select(p => p.DepartmentId)
            .Concat(version.Lines.Where(l => l.IsFromPersonnel && l.DepartmentId is not null).Select(l => l.DepartmentId!.Value))
            .Distinct()
            .ToList();
        if (departmentIds.Count == 0)
        {
            return 0;
        }

        Dictionary<Guid, Department> departments = await db.Departments.Where(d => departmentIds.Contains(d.Id)).ToDictionaryAsync(d => d.Id, ct);
        PersonnelChart chart = await ChartAsync(db, departments[departmentIds[0]], ct);
        int changed = 0;
        foreach (Guid departmentId in departmentIds)
        {
            changed += version.ApplyPersonnel(chart with { Department = departments[departmentId] }, rules).Count;
        }

        return changed;
    }

    /// <summary>
    /// A readable name for a position in a message: "Casey Lin, Patrol officer" or "Patrol officer (vacant)".
    /// </summary>
    public static string Describe(PositionDetails position) =>
        position.EmployeeName is { } name ? $"{name}, {position.Title}" : $"{position.Title} (vacant)";
}
