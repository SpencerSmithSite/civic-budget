using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CivicBudget.Infrastructure.Seed;

/// <summary>
/// Loads the fictional tenants on first run. Idempotent: if any government exists, it does nothing.
/// Written as ordinary C# against the domain model (not <c>HasData</c>) so the seed goes through the
/// same invariants as user input, so a seed that violates a domain rule fails loudly at startup.
/// </summary>
public sealed class DevelopmentSeeder(
    IDbContextFactory<CivicBudgetDbContext> dbFactory,
    AmbientTenantContext tenant,
    ILogger<DevelopmentSeeder> logger)
{
    private const string SeedUserId = "seed";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);

        if (await db.Governments.AnyAsync(ct))
        {
            logger.LogInformation("Seed skipped: governments already exist.");
            return;
        }

        await SeedMapleRidgeAsync(db, ct);
        await SeedPineHollowAsync(db, ct);
        tenant.Clear();

        logger.LogInformation("Seeded Maple Ridge and Pine Hollow demo data.");
    }

    private async Task SeedMapleRidgeAsync(CivicBudgetDbContext db, CancellationToken ct)
    {
        Government government = MapleRidgeSeed.Government();
        db.Governments.Add(government);
        await db.SaveChangesAsync(ct);
        tenant.Set(government.Id);

        var chart = await ChartOfAccounts.CreateAsync(
            db, government, MapleRidgeSeed.Funds(government.Id), MapleRidgeSeed.Departments(government.Id), MapleRidgeSeed.Accounts(government.Id), ct);

        // FY2025: adopted December 2024.
        BudgetVersion fy2025 = chart.BuildVersion(2025, MapleRidgeSeed.Lines, MapleRidgeSeed.BeginningBalances,
            amount: l => l.Budget2025, prior: l => l.Actual2023, current: l => l.Budget2024);
        fy2025.Propose();
        fy2025.Adopt("2024-38", SeedUserId, new DateTimeOffset(2024, 12, 16, 19, 30, 0, TimeSpan.Zero));

        // FY2026: adopted December 2025, then amended in June 2026.
        BudgetVersion fy2026 = chart.BuildVersion(2026, MapleRidgeSeed.Lines, MapleRidgeSeed.BeginningBalances,
            amount: l => l.Budget2026, prior: l => l.Actual2024, current: l => l.Budget2025);
        fy2026.Propose();
        fy2026.Adopt("2025-41", SeedUserId, new DateTimeOffset(2025, 12, 15, 19, 30, 0, TimeSpan.Zero));

        BudgetVersion fy2026Amendment = fy2026.CreateAmendment(MapleRidgeSeed.Amendment1Reason);
        foreach ((string fundCode, string? deptCode, string accountCode, decimal newAmount) in MapleRidgeSeed.Amendment1Changes)
        {
            BudgetLine line = fy2026Amendment.Lines.Single(l =>
                l.FundId == chart.Fund(fundCode).Id
                && l.DepartmentId == (deptCode is null ? null : chart.Department(deptCode).Id)
                && l.AccountId == chart.Account(accountCode).Id);
            fy2026Amendment.UpdateLineAmount(line.Id, newAmount);
        }

        fy2026Amendment.Propose();
        fy2026Amendment.Adopt("2026-11", SeedUserId, new DateTimeOffset(2026, 6, 15, 19, 30, 0, TimeSpan.Zero));
        fy2026.MarkSupersededBy(fy2026Amendment);

        // FY2027: draft in progress. Street fund is intentionally over its appropriation limit.
        BudgetVersion fy2027 = chart.BuildVersion(2027, MapleRidgeSeed.Lines, MapleRidgeSeed.BeginningBalances,
            amount: l => l.Budget2027, prior: l => l.Actual2025, current: l => l.Budget2026);

        // Fiscal years are not reachable from a version by navigation, so they are added explicitly.
        db.FiscalYears.AddRange(chart.FiscalYears);
        db.BudgetVersions.AddRange(fy2025, fy2026, fy2026Amendment, fy2027);
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedPineHollowAsync(CivicBudgetDbContext db, CancellationToken ct)
    {
        Government government = PineHollowSeed.Government();
        db.Governments.Add(government);
        await db.SaveChangesAsync(ct);
        tenant.Set(government.Id);

        var chart = await ChartOfAccounts.CreateAsync(
            db, government, PineHollowSeed.Funds(government.Id), PineHollowSeed.Departments(government.Id), PineHollowSeed.Accounts(government.Id), ct);

        // FY2026 runs July 2025 through June 2026; adopted in March 2025 (townships adopt before the year starts).
        BudgetVersion fy2026 = chart.BuildVersion(2026, PineHollowSeed.Lines, PineHollowSeed.BeginningBalances,
            amount: l => l.Budget2026, prior: l => l.Actual2024, current: l => l.Budget2025);
        fy2026.Propose();
        fy2026.Adopt("2025-07", SeedUserId, new DateTimeOffset(2025, 3, 18, 23, 0, 0, TimeSpan.Zero));

        BudgetVersion fy2027 = chart.BuildVersion(2027, PineHollowSeed.Lines, PineHollowSeed.BeginningBalances,
            amount: l => l.Budget2027, prior: l => l.Actual2025, current: l => l.Budget2026);

        db.FiscalYears.AddRange(chart.FiscalYears);
        db.BudgetVersions.AddRange(fy2026, fy2027);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The saved funds, departments, and accounts for one government, looked up by code.</summary>
    private sealed class ChartOfAccounts(
        Government government,
        Dictionary<string, Fund> funds,
        Dictionary<string, Department> departments,
        Dictionary<string, Account> accounts,
        Dictionary<int, FiscalYear> fiscalYears)
    {
        public static async Task<ChartOfAccounts> CreateAsync(
            CivicBudgetDbContext db,
            Government government,
            IReadOnlyList<Fund> funds,
            IReadOnlyList<Department> departments,
            IReadOnlyList<Account> accounts,
            CancellationToken ct)
        {
            db.Funds.AddRange(funds);
            db.Departments.AddRange(departments);
            db.Accounts.AddRange(accounts);
            await db.SaveChangesAsync(ct);

            return new ChartOfAccounts(
                government,
                funds.ToDictionary(f => f.Code),
                departments.ToDictionary(d => d.Code),
                accounts.ToDictionary(a => a.Code),
                []);
        }

        public Fund Fund(string code) => funds[code];
        public Department Department(string code) => departments[code];
        public Account Account(string code) => accounts[code];

        public BudgetVersion BuildVersion(
            int year,
            IReadOnlyList<SeedLine> lines,
            IReadOnlyDictionary<(string FundCode, int Year), decimal> beginningBalances,
            Func<SeedLine, decimal> amount,
            Func<SeedLine, decimal> prior,
            Func<SeedLine, decimal> current)
        {
            if (!fiscalYears.TryGetValue(year, out FiscalYear? fiscalYear))
            {
                fiscalYear = new FiscalYear(government.Id, year, government.FiscalYearStartMonth);
                fiscalYears[year] = fiscalYear;
            }

            BudgetVersion version = BudgetVersion.CreateOriginal(government.Id, fiscalYear.Id);
            foreach (SeedLine line in lines)
            {
                version.AddLine(
                    Fund(line.FundCode),
                    line.DepartmentCode is null ? null : Department(line.DepartmentCode),
                    Account(line.AccountCode),
                    amount(line),
                    prior(line),
                    current(line));
            }

            foreach (((string fundCode, int balanceYear), decimal balance) in beginningBalances)
            {
                if (balanceYear == year)
                {
                    version.SetBeginningBalance(Fund(fundCode), balance);
                }
            }

            return version;
        }

        public IEnumerable<FiscalYear> FiscalYears => fiscalYears.Values;
    }
}
