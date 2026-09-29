using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Budgets.Planning;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.Erp;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Domain.Personnel;
using CivicBudget.Domain.Publishing;
using CivicBudget.Domain.Reports;
using CivicBudget.Infrastructure.Erp;
using CivicBudget.Infrastructure.Identity;
using CivicBudget.Infrastructure.Persistence;
using CivicBudget.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CivicBudget.Infrastructure.Seed;

/// <summary>
/// Loads roles and the fictional tenants on first run. Roles are ensured every start (cheap, and a
/// new role must exist before anyone can be assigned to it); tenant data and demo users are loaded
/// only when no government exists yet. Written as ordinary C# against the domain model (not
/// <c>HasData</c>) so the seed goes through the same invariants as user input; a seed that violates
/// a domain rule fails loudly at startup.
/// </summary>
public sealed class DevelopmentSeeder(
    IDbContextFactory<CivicBudgetDbContext> dbFactory,
    CurrentUserContext tenant,
    RoleManager<IdentityRole> roleManager,
    UserManager<ApplicationUser> userManager,
    IOptions<SeedOptions> seedOptions,
    TimeProvider clock,
    ILogger<DevelopmentSeeder> logger)
{
    private const string SeedUserId = "seed";

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await EnsureRolesAsync();

        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);

        // Governments are seeded only into an empty database: an administrator may rename a government
        // or change its address, so nothing about an existing one can say "this is the seeded one", and
        // guessing would seed a duplicate on the next start. Users are different: each is found by email,
        // so a first start without a demo password (no users) is completed by the next start that has one.
        // A seed interrupted part way is not repaired; `--reseed` rebuilds everything.
        bool seededGovernments = !await db.Governments.AnyAsync(ct);
        Government? mapleRidge = seededGovernments
            ? await SeedMapleRidgeAsync(db, ct)
            : await db.Governments.SingleOrDefaultAsync(g => g.PublicSlug == MapleRidgeSeed.Slug, ct);
        Government? pineHollow = seededGovernments
            ? await SeedPineHollowAsync(db, ct)
            : await db.Governments.SingleOrDefaultAsync(g => g.PublicSlug == PineHollowSeed.Slug, ct);

        int users = (mapleRidge is null ? 0 : await SeedUsersAsync(mapleRidge, DemoUsers.MapleRidge, ct))
            + (pineHollow is null ? 0 : await SeedUsersAsync(pineHollow, DemoUsers.PineHollow, ct));
        tenant.Clear();

        if (seededGovernments || users > 0)
        {
            logger.LogInformation("Seeded demo data: governments {Governments}, {Users} user(s).", seededGovernments ? "created" : "already present", users);
        }
        else
        {
            logger.LogInformation("Seed skipped: the demo governments and users already exist.");
        }
    }

    private async Task EnsureRolesAsync()
    {
        foreach (string role in Roles.All)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }
    }

    /// <summary>
    /// Creates the demo logins for one government that do not exist yet, and returns how many it made.
    /// Skipped, with a warning, when no demo password is configured.
    /// </summary>
    private async Task<int> SeedUsersAsync(Government government, IReadOnlyList<DemoUser> users, CancellationToken ct)
    {
        string? password = seedOptions.Value.DemoPassword;
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Seed:DemoPassword is not configured; demo users for {Government} were not created. Run scripts/dev-setup.sh.", government.Name);
            return 0;
        }

        tenant.SetTenant(government.Id);
        await using CivicBudgetDbContext db = await dbFactory.CreateDbContextAsync(ct);
        Dictionary<string, Guid> departmentsByCode = await db.Departments.ToDictionaryAsync(d => d.Code, d => d.Id, ct);

        int created = 0;
        foreach (DemoUser demo in users)
        {
            if (await userManager.FindByEmailAsync(demo.Email) is not null)
            {
                continue;
            }

            var user = new ApplicationUser
            {
                UserName = demo.Email,
                Email = demo.Email,
                EmailConfirmed = true,
                DisplayName = demo.DisplayName,
                GovernmentId = government.Id,
            };
            foreach (string code in demo.DepartmentCodes)
            {
                user.Departments.Add(new UserDepartment { DepartmentId = departmentsByCode[code] });
            }

            IdentityResult made = await userManager.CreateAsync(user, password);
            IdentityResult roled = made.Succeeded ? await userManager.AddToRoleAsync(user, demo.Role) : made;
            if (!roled.Succeeded)
            {
                throw new InvalidOperationException($"Could not create demo user {demo.Email}: {string.Join("; ", roled.Errors.Select(e => e.Description))}");
            }

            created++;
        }

        return created;
    }

    private async Task<Government> SeedMapleRidgeAsync(CivicBudgetDbContext db, CancellationToken ct)
    {
        Government government = MapleRidgeSeed.Government();
        // On for Maple Ridge and off for Pine Hollow, so both halves of each switch are in the demo.
        // They do nothing until the operator connects a model (scripts/azure-assistant.sh on the hosted demo).
        government.SetAssistantEnabled(true);
        government.SetPortalQuestionsEnabled(true);
        tenant.SetTenant(government.Id); // before the first save: the government's own audit row is tenant-checked
        db.Governments.Add(government);
        await db.SaveChangesAsync(ct);

        var chart = await ChartOfAccounts.CreateAsync(
            db, government, MapleRidgeSeed.Funds(government.Id), MapleRidgeSeed.Departments(government.Id), MapleRidgeSeed.Accounts(government.Id), ct);

        // FY2025: adopted December 2024.
        BudgetVersion fy2025 = chart.BuildVersion(2025, MapleRidgeSeed.Lines, MapleRidgeSeed.BeginningBalances,
            amount: l => l.Budget2025, prior: l => l.Actual2023, current: l => l.Budget2024);
        fy2025.SetPlan(1, wholeDollars: true, []); // budgeted before the plan existed: the budget year only
        fy2025.Propose();
        fy2025.Adopt("2024-38", SeedUserId, new DateTimeOffset(2024, 12, 16, 19, 30, 0, TimeSpan.Zero));

        // FY2026: adopted December 2025, then amended in June 2026. Department narratives are written
        // before adoption and travel into the amendment, so the published portal shows them.
        BudgetVersion fy2026 = chart.BuildVersion(2026, MapleRidgeSeed.Lines, MapleRidgeSeed.BeginningBalances,
            amount: l => l.Budget2026, prior: l => l.Actual2024, current: l => l.Budget2025);
        foreach ((string deptCode, string narrative) in MapleRidgeSeed.Narratives)
        {
            fy2026.SetDepartmentNarrative(chart.Department(deptCode), narrative);
        }

        fy2026.SetPlan(BudgetVersion.DefaultPlanYears, wholeDollars: true, MapleRidgeSeed.Plan2026);
        (string heading, string body, string signedBy, string signerTitle) = MapleRidgeSeed.Message2026;
        fy2026.SetMessage(heading, body, signedBy, signerTitle);
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

        // FY2027: draft in progress. Street fund is intentionally over its appropriation limit. The
        // department round is mid-way: Police has submitted, Parks was returned, Streets is still writing.
        BudgetVersion fy2027 = chart.BuildVersion(2027, MapleRidgeSeed.Lines, MapleRidgeSeed.BeginningBalances,
            amount: l => l.Budget2027, prior: l => l.Actual2025, current: l => l.Budget2026);
        foreach ((string deptCode, string narrative) in MapleRidgeSeed.Narratives)
        {
            fy2027.SetDepartmentNarrative(chart.Department(deptCode), narrative);
        }

        fy2027.SetPlan(BudgetVersion.DefaultPlanYears, wholeDollars: true, MapleRidgeSeed.Plan2027);
        (heading, body, signedBy, signerTitle) = MapleRidgeSeed.Message2027;
        fy2027.SetMessage(heading, body, signedBy, signerTitle);
        (string planFund, string planDept, string planAccount, int planYear, decimal planAmount) = MapleRidgeSeed.PlannedProject;
        BudgetLine projectLine = fy2027.Lines.Single(l => l.FundId == chart.Fund(planFund).Id
            && l.DepartmentId == chart.Department(planDept).Id && l.AccountId == chart.Account(planAccount).Id);
        fy2027.SetPlannedAmount(projectLine.Id, planYear, planAmount);

        fy2027.SubmitDepartment(chart.Department("110"), SeedUserId, "Chief Morgan Hale", new DateTimeOffset(2026, 9, 14, 20, 15, 0, TimeSpan.Zero));
        fy2027.SubmitDepartment(chart.Department("310"), SeedUserId, "Sam Okafor (Service Director)", new DateTimeOffset(2026, 9, 10, 18, 40, 0, TimeSpan.Zero));
        fy2027.ReturnDepartment(chart.Department("310"), MapleRidgeSeed.ParksReturnNote, new DateTimeOffset(2026, 9, 11, 13, 5, 0, TimeSpan.Zero));

        // FY2027 personnel: the year's settings and the positions of Police, Finance, and Streets & Service.
        // Adding each position prices its department, so those departments' salary and benefit lines
        // become calculated ("from 9 positions") the moment the seed runs, as they would for a real user.
        PersonnelSettings personnel = MapleRidgePersonnel.Settings(government.Id, code => chart.Account(code).Id);
        FiscalYear personnelYear = chart.FiscalYear(MapleRidgePersonnel.Year);
        PayrollRules rules = personnel.ToRules(personnelYear.StartDate, personnelYear.EndDate, chart.AllFunds.ToDictionary(f => f.Id, f => f.Code));
        foreach ((string deptCode, PositionDetails details) in MapleRidgePersonnel.Positions(rules, code => chart.Fund(code).Id))
        {
            fy2027.AddPosition(chart.PersonnelChart(deptCode), details, rules);
        }

        db.PersonnelSettings.Add(personnel);

        // Fiscal years are not reachable from a version by navigation, so they are added explicitly.
        db.FiscalYears.AddRange(chart.FiscalYears);
        db.BudgetVersions.AddRange(fy2025, fy2026, fy2026Amendment, fy2027);

        // SPEC section 9: FY2025 and the FY2026 amendment are published to the portal.
        List<Fund> funds = chart.AllFunds.ToList();
        db.PublishedBudgetSnapshots.Add(PublishedBudgetSnapshot.Capture(
            government, chart.FiscalYear(2025), fy2025, funds, SeedUserId, "system", new DateTimeOffset(2025, 1, 6, 15, 0, 0, TimeSpan.Zero)));
        db.PublishedBudgetSnapshots.Add(PublishedBudgetSnapshot.Capture(
            government, chart.FiscalYear(2026), fy2026Amendment, funds, SeedUserId, "system", new DateTimeOffset(2026, 6, 16, 14, 0, 0, TimeSpan.Zero)));

        // Both originals went to the ERP when they were adopted; the FY2026 amendment has not been sent
        // yet, so the send page opens on exactly the supplemental appropriation's two changes.
        chart.AddSentJournal(db, fy2025, 2025, "FY2025 Original, resolution 2024-38", new DateOnly(2025, 1, 1), "BJ2025-00112", new DateTimeOffset(2025, 1, 2, 15, 10, 0, TimeSpan.Zero));
        chart.AddSentJournal(db, fy2026, 2026, "FY2026 Original, resolution 2025-41", new DateOnly(2026, 1, 1), "BJ2026-00007", new DateTimeOffset(2026, 1, 5, 14, 30, 0, TimeSpan.Zero));

        // The certificate's settings, as the fiscal officer would set them once: the county whose
        // budget commission certifies it (fictional, like the village), who prepares it, and a Taxes
        // column of the village's real estate and municipal income taxes. Pine Hollow keeps the defaults.
        var certificate = new CertificateSettings(government.Id);
        certificate.Update("Harmon", "Dana Whitfield", CertificateSettings.DefaultFiscalOfficerTitle, CertificateSettings.DefaultBalanceLabel, CertificateSettings.DefaultOtherSourcesLabel);
        db.CertificateSettings.Add(certificate);
        db.ReportAccountGroups.Add(new ReportAccountGroup(government.Id, ReportKind.Certificate, "Taxes", 0, [chart.Account("4110").Id, chart.Account("4130").Id]));

        // The ERP has already sent last year's closed books and this year's so far, as it would have by
        // the time a draft is in progress. The actuals page can fetch them again at any time.
        await chart.AddActualsAsync(db, new SimulatedErpActualsApi(clock), [2025, 2026], clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        return government;
    }

    private async Task<Government> SeedPineHollowAsync(CivicBudgetDbContext db, CancellationToken ct)
    {
        Government government = PineHollowSeed.Government();
        tenant.SetTenant(government.Id);
        db.Governments.Add(government);
        await db.SaveChangesAsync(ct);

        var chart = await ChartOfAccounts.CreateAsync(
            db, government, PineHollowSeed.Funds(government.Id), PineHollowSeed.Departments(government.Id), PineHollowSeed.Accounts(government.Id), ct);

        // FY2026 runs July 2025 through June 2026; adopted in March 2025 (townships adopt before the year starts).
        BudgetVersion fy2026 = chart.BuildVersion(2026, PineHollowSeed.Lines, PineHollowSeed.BeginningBalances,
            amount: l => l.Budget2026, prior: l => l.Actual2024, current: l => l.Budget2025);
        fy2026.SetPlan(BudgetVersion.DefaultPlanYears, wholeDollars: true, [new(1, 2m, 2.5m), new(2, 2m, 2.5m), new(3, 2m, 2.5m), new(4, 2m, 2.5m)]);
        fy2026.Propose();
        fy2026.Adopt("2025-07", SeedUserId, new DateTimeOffset(2025, 3, 18, 23, 0, 0, TimeSpan.Zero));

        BudgetVersion fy2027 = chart.BuildVersion(2027, PineHollowSeed.Lines, PineHollowSeed.BeginningBalances,
            amount: l => l.Budget2027, prior: l => l.Actual2025, current: l => l.Budget2026);

        db.FiscalYears.AddRange(chart.FiscalYears);
        db.BudgetVersions.AddRange(fy2026, fy2027);
        db.PublishedBudgetSnapshots.Add(PublishedBudgetSnapshot.Capture(
            government, chart.FiscalYear(2026), fy2026, chart.AllFunds.ToList(), SeedUserId, "system", new DateTimeOffset(2025, 7, 1, 13, 0, 0, TimeSpan.Zero)));
        chart.AddSentJournal(db, fy2026, 2026, "FY2026 Original, resolution 2025-07", new DateOnly(2025, 7, 1), "BJ2026-00031", new DateTimeOffset(2025, 7, 2, 13, 45, 0, TimeSpan.Zero));
        await chart.AddActualsAsync(db, new SimulatedErpActualsApi(clock), [2025, 2026], clock.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        return government;
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

        /// <summary>A department and the whole chart, for adding its positions.</summary>
        public PersonnelChart PersonnelChart(string departmentCode) =>
            new(departments[departmentCode], funds.Values.ToDictionary(f => f.Id), accounts.Values.ToDictionary(a => a.Id));

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

        /// <summary>
        /// Stages the simulated ERP's actuals for these years, matched against this chart. A year the
        /// ERP cannot give yet (no month closed, depending on today's date) is simply skipped.
        /// </summary>
        public async Task AddActualsAsync(CivicBudgetDbContext db, SimulatedErpActualsApi erp, int[] years, DateTimeOffset nowUtc, CancellationToken ct)
        {
            var entity = new ErpEntity(government.Id, government.PublicSlug, government.Name, government.FiscalYearStartMonth, government.AccountNumberFormat);
            foreach (int year in years)
            {
                Result<ErpActuals> fetched = await erp.FetchAsync(entity, year, ct);
                if (fetched.IsFailure)
                {
                    continue;
                }

                MatchedActuals matched = ActualsMatcher.Match(
                    fetched.Value,
                    funds.Values.Select(f => new ChartCode(f.Id, f.Code, f.Name)).ToList(),
                    departments.Values.Select(d => new ChartCode(d.Id, d.Code, d.Name)).ToList(),
                    accounts.Values.Select(a => new ChartCode(a.Id, a.Code, a.Name, a.Type)).ToList()).Value;
                DateOnly asOf = FiscalPeriod.For(year, government.FiscalYearStartMonth).Start.AddMonths(matched.ThroughPeriod).AddDays(-1);
                ActualsWriter.Add(db, government.Id, matched, asOf, erp.Name, null, SeedUserId, "system", nowUtc, priorActualsUpdated: 0);
            }
        }

        /// <summary>A budget journal the ERP accepted in the past: the whole version, as the first send of a year is.</summary>
        public void AddSentJournal(CivicBudgetDbContext db, BudgetVersion version, int year, string description, DateOnly postingDate, string journalNumber, DateTimeOffset sentAtUtc)
        {
            var sent = new BudgetTransmission(government.Id, version.Id, year, TransmissionMethod.Api, "ERP (simulated)", description, postingDate, SeedUserId, "system", sentAtUtc);
            foreach (BudgetLine line in version.Lines.Where(l => l.Amount != 0m))
            {
                sent.AddLine(line.FundId, line.DepartmentId, line.AccountId,
                    AccountNumber.Compose(government.AccountNumberFormat, line.Fund.Code, line.Department?.Code, line.Account.Code), line.Amount);
            }

            sent.MarkAccepted(journalNumber, sentAtUtc.AddSeconds(2));
            db.BudgetTransmissions.Add(sent);
        }

        public IEnumerable<FiscalYear> FiscalYears => fiscalYears.Values;
        public FiscalYear FiscalYear(int year) => fiscalYears[year];
        public IEnumerable<Fund> AllFunds => funds.Values;
    }
}
