using CivicBudget.Application.Notifications;
using CivicBudget.Application.Persistence;
using CivicBudget.Application.Security;
using CivicBudget.Application.Tenancy;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using Microsoft.EntityFrameworkCore;

namespace CivicBudget.Application.Setup;

/// <summary>One step of getting a government ready.</summary>
/// <param name="AdminOnly">The page it points to is for Administrators; a Fiscal Officer sees the step but asks an Administrator.</param>
public sealed record SetupStepDto(string Title, string Description, bool Done, bool Optional, string Href, string ActionLabel, string? Detail, bool AdminOnly = false);

public sealed record SetupChecklistDto(string GovernmentName, IReadOnlyList<SetupStepDto> Steps)
{
    public int RequiredDone => Steps.Count(s => !s.Optional && s.Done);
    public int RequiredTotal => Steps.Count(s => !s.Optional);
    public bool IsReady => RequiredDone == RequiredTotal;
}

/// <summary>
/// What a new government still has to do before its first budget, worked out from what is already
/// there rather than from ticks someone could forget to tick: a chart is set up when it has funds,
/// departments, and both kinds of account. The same list tells a government that has everything which
/// optional pieces (personnel, actuals, two-step sign-in, the portal) it has not used yet.
/// </summary>
public interface ISetupChecklistService
{
    Task<SetupChecklistDto> GetAsync(CancellationToken ct = default);
}

public sealed class SetupChecklistService(ICivicBudgetDbContextFactory dbFactory, ITenantContext tenant, IUserDirectory users) : ISetupChecklistService
{
    public async Task<SetupChecklistDto> GetAsync(CancellationToken ct = default)
    {
        Guid governmentId = tenant.GovernmentId ?? throw new InvalidOperationException("No tenant is set.");
        await using ICivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        var government = await db.Governments.Where(g => g.Id == governmentId).Select(g => new { g.Name, g.FiscalYearStartMonth, g.RequireMfa }).SingleAsync(ct);

        int funds = await db.Funds.CountAsync(f => f.IsActive, ct);
        int departments = await db.Departments.CountAsync(d => d.IsActive, ct);
        int revenue = await db.Accounts.CountAsync(a => a.IsActive && a.Type == AccountType.Revenue, ct);
        int expenditure = await db.Accounts.CountAsync(a => a.IsActive && a.Type == AccountType.Expenditure, ct);
        List<int> years = await db.FiscalYears.OrderByDescending(f => f.Year).Select(f => f.Year).ToListAsync(ct);
        int versions = await db.BudgetVersions.CountAsync(ct);
        int? preparing = await db.BudgetVersions.Where(v => v.Status != BudgetStatus.Adopted)
            .Join(db.FiscalYears, v => v.FiscalYearId, f => f.Id, (v, f) => (int?)f.Year).OrderByDescending(y => y).FirstOrDefaultAsync(ct);
        bool personnel = await db.PersonnelSettings.AnyAsync(ct);
        bool actuals = await db.ActualsSyncs.AnyAsync(ct);
        bool published = await db.PublishedBudgetSnapshots.AnyAsync(ct);
        int people = await users.CountAsync(governmentId, ct);
        string month = System.Globalization.CultureInfo.GetCultureInfo("en-US").DateTimeFormat.GetMonthName(government.FiscalYearStartMonth);

        List<SetupStepDto> steps =
        [
            new("Your government", "Its name, public portal address, how account numbers are written, and the portal logo.",
                Done: true, Optional: false, "admin/settings", "Review", $"The fiscal year starts in {month}.", AdminOnly: true),
            new("Chart of accounts", "Funds, departments, and accounts, brought in from the ERP (by connection or its export file) or added here.",
                Done: funds > 0 && departments > 0 && revenue > 0 && expenditure > 0, Optional: false, "admin/chart-sync", "Bring it in from the ERP",
                $"{funds} funds, {departments} departments, {revenue} revenue and {expenditure} expenditure accounts."),
            new("A fiscal year", "The year the first budget is for.",
                Done: years.Count > 0, Optional: false, "admin/fiscal-years", "Add a fiscal year", years.Count == 0 ? null : $"FY{string.Join(", FY", years.Take(4))}."),
            new("The first budget", "Started empty, or imported from a spreadsheet of last year's lines.",
                Done: versions > 0, Optional: false, "admin/fiscal-years", "Start a budget", versions == 0 ? null : $"{versions} budget version{(versions == 1 ? "" : "s")}."),
            new("Your team", "The Fiscal Officer, department heads, and council members who view. Each gets an email to choose their password.",
                Done: people > 1, Optional: false, "admin/users", "Add people", $"{people} user{(people == 1 ? "" : "s")}.", AdminOnly: true),
            new("Personnel", "Retirement rates, insurance premiums, and pay rules, so salary and benefit lines are calculated from positions.",
                Done: personnel, Optional: true, preparing is { } y ? $"admin/personnel-settings?year={y}" : "admin/personnel-settings", "Set up personnel", null),
            new("Actuals from the ERP", "The ERP's spending and receipts, for budget against actual and the prior-year column.",
                Done: actuals, Optional: true, "admin/actuals-sync", "Sync actuals", null),
            new("Two-step sign-in", "Require a code from an authenticator app at every sign-in.",
                Done: government.RequireMfa, Optional: true, "admin/settings", "Require it", null, AdminOnly: true),
            new("The public portal", "Publish an adopted budget so residents can see it.",
                Done: published, Optional: true, "admin/budgets", "Open a budget to publish", null),
        ];

        return new SetupChecklistDto(government.Name, steps);
    }
}
