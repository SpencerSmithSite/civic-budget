using CivicBudget.Domain.Personnel;

namespace CivicBudget.Infrastructure.Seed;

/// <summary>
/// Maple Ridge's FY2027 personnel: the settings a fiscal officer would enter once, and the positions
/// of three departments in the draft budget. Police shows a union pay scale, OP&amp;F, and a chief whose
/// employee share the village picks up; Finance shows a part-time hourly clerk; Streets &amp; Service
/// shows positions split between the General Fund and the Street fund, and a summer seasonal vacancy.
/// Each longevity method appears once: a per-year amount (non-union policy), a step table (FOP), and a
/// percentage of pay (AFSCME). Every name is fictional.
/// </summary>
internal static class MapleRidgePersonnel
{
    public const int Year = 2027;

    public static PersonnelSettings Settings(Guid governmentId, Func<string, Guid> account)
    {
        var settings = PersonnelSettings.CreateDefault(governmentId, Year,
            payAccountId: account("5110"), overtimeAccountId: account("5120"), retirementAccountId: account("5210"),
            medicareAccountId: account("5220"), workersCompAccountId: account("5240"));
        settings.SetBasics(PersonnelSettings.DefaultStandardHours, account("5110"), PersonnelSettings.DefaultMedicareRate, account("5220"), 2.1m, account("5240"));

        settings.SaveInsurancePlan(null, "Medical (PPO)", 650m, 1_300m, 1_760m, 15m, account("5230"));
        settings.SaveInsurancePlan(null, "Dental", 42m, 84m, 128m, 10m, account("5230"));
        settings.SaveInsurancePlan(null, "Life ($25,000)", 9.50m, null, null, 0m, account("5230"));

        settings.SaveLongevitySchedule(null, "Non-union personnel policy", LongevityMethod.AmountPerYear, ServiceCountedOn.FirstDayOfYear, 25, null,
            [new(5, 75m)]);
        settings.SaveLongevitySchedule(null, "FOP Lodge 112 contract", LongevityMethod.FlatAmount, ServiceCountedOn.LastDayOfYear, null, null,
            [new(5, 600m), new(10, 900m), new(15, 1_200m), new(20, 1_500m)]);
        settings.SaveLongevitySchedule(null, "AFSCME Local 3301 contract", LongevityMethod.PercentOfPay, ServiceCountedOn.FirstDayOfYear, null, null,
            [new(5, 1.5m), new(10, 3m), new(15, 4.5m)]);

        settings.SavePayScale(null, "FOP patrol and sergeants", PayBasis.Hourly,
        [
            new("PO", 1, 25.10m), new("PO", 2, 26.60m), new("PO", 3, 28.10m), new("PO", 4, 29.60m), new("PO", 5, 31.10m),
            new("SGT", 1, 33.60m), new("SGT", 2, 35.10m), new("SGT", 3, 36.60m),
        ]);
        return settings;
    }

    /// <summary>
    /// The village's payroll as the ERP holds it when the FY2027 budget was started, plus what the
    /// budget adds to each position (the planned raise or step increase, months, longevity, other pay).
    /// A null employee number is a vacancy the budget carries and the payroll does not.
    /// </summary>
    public static IReadOnlyList<SeedEmployee> Roster { get; } =
    [
        // ---- 110 Police: eight sworn officers (one position vacant until April) and a part-time clerk.
        new("E1001", "110", "Chief of Police", "Morgan Hale", new(2006, 4, 17), PayBasis.Salary, 88_500m, Retirement: "OP&F police", PickUp: true,
            Benefits: Family, Raise: 3m, Longevity: NonUnion, Extra: [("Uniform allowance", 1_100m)]),
        Officer("E1014", "Sergeant", "Alex Rivera", new(2011, 8, 8), "SGT", 3, null, CoverageTier.Family, 60m),
        Officer("E1022", "Sergeant", "Taylor Brooks", new(2015, 3, 2), "SGT", 2, 9, CoverageTier.EmployeeSpouse, 60m),
        Officer("E1017", "Patrol officer", "Riley Chen", new(2013, 6, 3), "PO", 5, null, CoverageTier.Family, 70m),
        Officer("E1031", "Patrol officer", "Quinn Murphy", new(2018, 10, 15), "PO", 4, 10, CoverageTier.EmployeeOnly, 70m),
        Officer("E1036", "Patrol officer", "Jordan Price", new(2021, 1, 11), "PO", 3, 7, CoverageTier.EmployeeSpouse, 70m),
        Officer("E1042", "Patrol officer", "Avery Santos", new(2024, 5, 20), "PO", 2, 5, CoverageTier.EmployeeOnly, 70m),
        Officer(null, "Patrol officer", null, null, "PO", 1, null, CoverageTier.Family, 40m) with { FirstMonth = 4 },
        new("E1029", "110", "Records clerk (part-time)", "Pat Donnelly", new(2020, 2, 3), PayBasis.Hourly, 19.25m, Hours: 1_040m,
            Retirement: "OPERS", Benefits: [], Raise: 3m),

        // ---- 725 Finance: the fiscal officer and a part-time payroll clerk.
        new("E1008", "725", "Fiscal Officer", "Dana Whitfield", new(2011, 3, 1), PayBasis.Salary, 64_800m, Retirement: "OPERS",
            Benefits: Family, Raise: 3m, Longevity: NonUnion),
        new("E1033", "725", "Payroll and accounts payable clerk", "Casey Lin", new(2019, 6, 10), PayBasis.Hourly, 23.10m, Hours: 1_560m,
            Retirement: "OPERS", Benefits: [], Raise: 3m),

        // ---- 620 Streets & Service: the director and crew, shared between the General Fund and the Street fund.
        new("E1011", "620", "Service Director", "Sam Okafor", new(2010, 5, 3), PayBasis.Salary, 71_500m, Retirement: "OPERS",
            Funds: [("1000", 50m), ("2011", 50m)], Benefits: Family, Raise: 3m, Longevity: NonUnion, Extra: [("Phone or vehicle stipend", 1_200m)]),
        StreetWorker("E1005", "Chris Hollis", new(2008, 7, 14), 24.80m, CoverageTier.Family, 25m),
        StreetWorker("E1026", "Rowan Ruiz", new(2016, 4, 4), 23.10m, CoverageTier.EmployeeSpouse, 40m),
        StreetWorker("E1039", "Devon Walsh", new(2022, 9, 12), 21.40m, CoverageTier.EmployeeOnly, 40m),
        new SeedEmployee(null, "620", "Seasonal laborer (summer paving)", null, null, PayBasis.Hourly, 16m, Retirement: "OPERS",
            Funds: [("2011", 100m)], Benefits: []) with { FirstMonth = 6, LastMonth = 8 },
    ];

    /// <summary>
    /// What the simulated ERP's payroll says today: the roster, moved on a little since the budget was
    /// started, so the sync has something to show. A patrol officer was hired into the vacancy, Casey
    /// Lin had a merit increase, and Rowan Ruiz retired.
    /// </summary>
    public static IReadOnlyList<SeedEmployee> Payroll { get; } =
    [
        .. Roster.Where(e => e.Id is not null && e.Id != "E1026").Select(e => e.Id == "E1033" ? e with { Rate = 23.60m } : e),
        Officer("E1047", "Patrol officer", "Jamie Ortiz", new(2026, 9, 8), "PO", 1, null, CoverageTier.Family, 40m),
    ];

    /// <summary>Each budgeted position, by department code, in the terms of the year's settings.</summary>
    public static IReadOnlyList<(string Department, PositionDetails Details)> Positions(PayrollRules rules, Func<string, Guid> fund) =>
        Roster.Select(e => (e.Department, e.ToDetails(rules, fund))).ToList();

    private const string NonUnion = "Non-union personnel policy";

    private static (string Plan, CoverageTier Tier)[] Family =>
        [("Medical (PPO)", CoverageTier.Family), ("Dental", CoverageTier.Family), ("Life ($25,000)", CoverageTier.EmployeeOnly)];

    private static (string, CoverageTier)[] Covered(CoverageTier tier) =>
        [("Medical (PPO)", tier), ("Dental", tier), ("Life ($25,000)", CoverageTier.EmployeeOnly)];

    private static SeedEmployee Officer(string? id, string title, string? name, DateOnly? hired, string grade, int step, int? stepMonth, CoverageTier tier, decimal overtimeHours) =>
        new(id, "110", title, name, hired, PayBasis.Hourly, 0m, Grade: grade, Step: step, Retirement: "OP&F police", Benefits: Covered(tier),
            StepMonth: stepMonth, Longevity: hired is null ? null : "FOP Lodge 112 contract",
            Extra: [("Overtime", overtimeHours), ("Holiday pay", 88m), ("Uniform allowance", 1_100m)]);

    private static SeedEmployee StreetWorker(string id, string name, DateOnly hired, decimal rate, CoverageTier tier, decimal generalShare) =>
        new(id, "620", "Maintenance worker", name, hired, PayBasis.Hourly, rate, Retirement: "OPERS",
            Funds: [("1000", generalShare), ("2011", 100m - generalShare)], Benefits: Covered(tier), Raise: 2.5m,
            Longevity: "AFSCME Local 3301 contract", Extra: [("Overtime", 80m), ("Uniform allowance", 400m)]);
}

/// <summary>
/// One employee (or vacancy) in the demo: the ERP's side (number, name, pay, plans, funds, by name and
/// code, the way a payroll export has them) and the budget's side (raise, months, longevity, other pay).
/// </summary>
internal sealed record SeedEmployee(
    string? Id,
    string Department,
    string Title,
    string? Name,
    DateOnly? Hired,
    PayBasis Basis,
    decimal Rate,
    decimal? Hours = null,
    string? Grade = null,
    int? Step = null,
    string? Retirement = null,
    bool PickUp = false,
    (string Fund, decimal Percent)[]? Funds = null,
    (string Plan, CoverageTier Tier)[]? Benefits = null,
    decimal Raise = 0m,
    int? StepMonth = null,
    string? Longevity = null,
    (string Item, decimal Value)[]? Extra = null)
{
    public int FirstMonth { get; init; } = 1;
    public int LastMonth { get; init; } = 12;

    private (string Fund, decimal Percent)[] FundShares => Funds ?? [("1000", 100m)];

    /// <summary>The employee as the ERP's payroll lists them.</summary>
    public Application.Erp.ErpEmployee ToErp() => new(
        Id!, Name!, Title, Department, Basis, Rate, Hours, Hired, Grade, Step, Retirement, PickUp,
        FundShares.Select(f => new Application.Erp.ErpFundShare(f.Fund, f.Percent)).ToList(),
        (Benefits ?? []).Select(b => new Application.Erp.ErpBenefit(b.Plan, b.Tier)).ToList());

    /// <summary>The budgeted position, with plan names resolved against the year's settings.</summary>
    public PositionDetails ToDetails(PayrollRules rules, Func<string, Guid> fund)
    {
        PayScaleRule? scale = Grade is null ? null : rules.PayScales.First(s => s.Rate(Grade, Step!.Value) is not null);
        return new PositionDetails
        {
            Title = Title,
            EmployeeName = Name,
            EmployeeId = Id,
            HireDate = Hired,
            Basis = scale?.Basis ?? Basis,
            Rate = Rate,
            AnnualHours = Hours ?? rules.StandardHours,
            PayScaleId = scale?.Id,
            Grade = Grade,
            Step = Step,
            StepIncreaseMonth = StepMonth,
            RaisePercent = Raise,
            FirstMonth = FirstMonth,
            LastMonth = LastMonth,
            LongevityScheduleId = Longevity is null ? null : rules.Longevity.Single(l => l.Name == Longevity).Id,
            RetirementPlanId = Retirement is null ? null : rules.RetirementPlans.Single(p => p.Name == Retirement).Id,
            PicksUpEmployeeShare = PickUp,
            Funds = FundShares.Select(f => new FundShare(fund(f.Fund), f.Percent)).ToList(),
            Coverages = (Benefits ?? []).Select(b => new Coverage(rules.InsurancePlans.Single(p => p.Name == b.Plan).Id, b.Tier)).ToList(),
            ExtraPay = (Extra ?? []).Select(x => new ExtraPayAmount(rules.ExtraPay.Single(p => p.Name == x.Item).Id, x.Value)).ToList(),
        };
    }
}
