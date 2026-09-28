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

    /// <summary>Each position by department code.</summary>
    public static IReadOnlyList<(string Department, PositionDetails Details)> Positions(PayrollRules rules, Func<string, Guid> fund)
    {
        Guid Plan(string name) => rules.RetirementPlans.Single(p => p.Name == name).Id;
        Guid Insurance(string name) => rules.InsurancePlans.Single(p => p.Name == name).Id;
        Guid Extra(string name) => rules.ExtraPay.Single(p => p.Name == name).Id;
        Guid Longevity(string name) => rules.Longevity.Single(p => p.Name == name).Id;
        Guid fop = rules.PayScales.Single().Id;

        Coverage[] Covered(CoverageTier tier) =>
            [new(Insurance("Medical (PPO)"), tier), new(Insurance("Dental"), tier), new(Insurance("Life ($25,000)"), CoverageTier.EmployeeOnly)];
        FundShare[] General() => [new(fund("1000"), 100m)];

        PositionDetails Officer(string title, string? name, DateOnly? hired, string grade, int step, int? stepMonth, CoverageTier tier, decimal overtimeHours) => new()
        {
            Title = title,
            EmployeeName = name,
            HireDate = hired,
            PayScaleId = fop,
            Grade = grade,
            Step = step,
            StepIncreaseMonth = stepMonth,
            LongevityScheduleId = hired is null ? null : Longevity("FOP Lodge 112 contract"),
            RetirementPlanId = Plan("OP&F police"),
            Funds = General(),
            Coverages = Covered(tier),
            ExtraPay =
            [
                new(Extra("Overtime"), overtimeHours),
                new(Extra("Holiday pay"), 88m),
                new(Extra("Uniform allowance"), 1_100m),
            ],
        };

        PositionDetails StreetWorker(string name, DateOnly hired, decimal rate, CoverageTier tier, decimal generalShare) => new()
        {
            Title = "Maintenance worker",
            EmployeeName = name,
            HireDate = hired,
            Basis = PayBasis.Hourly,
            Rate = rate,
            RaisePercent = 2.5m,
            RaiseMonth = 1,
            LongevityScheduleId = Longevity("AFSCME Local 3301 contract"),
            RetirementPlanId = Plan("OPERS"),
            Funds = [new(fund("1000"), generalShare), new(fund("2011"), 100m - generalShare)],
            Coverages = Covered(tier),
            ExtraPay = [new(Extra("Overtime"), 80m), new(Extra("Uniform allowance"), 400m)],
        };

        return
        [
            // ---- 110 Police: eight sworn officers (one position vacant until April) and a part-time clerk.
            ("110", new PositionDetails
            {
                Title = "Chief of Police",
                EmployeeName = "Morgan Hale",
                HireDate = new DateOnly(2006, 4, 17),
                Rate = 88_500m,
                RaisePercent = 3m,
                LongevityScheduleId = Longevity("Non-union personnel policy"),
                RetirementPlanId = Plan("OP&F police"),
                PicksUpEmployeeShare = true,
                Funds = General(),
                Coverages = Covered(CoverageTier.Family),
                ExtraPay = [new(Extra("Uniform allowance"), 1_100m)],
            }),
            ("110", Officer("Sergeant", "Alex Rivera", new DateOnly(2011, 8, 8), "SGT", 3, null, CoverageTier.Family, 60m)),
            ("110", Officer("Sergeant", "Taylor Brooks", new DateOnly(2015, 3, 2), "SGT", 2, 9, CoverageTier.EmployeeSpouse, 60m)),
            ("110", Officer("Patrol officer", "Riley Chen", new DateOnly(2013, 6, 3), "PO", 5, null, CoverageTier.Family, 70m)),
            ("110", Officer("Patrol officer", "Quinn Murphy", new DateOnly(2018, 10, 15), "PO", 4, 10, CoverageTier.EmployeeOnly, 70m)),
            ("110", Officer("Patrol officer", "Jordan Price", new DateOnly(2021, 1, 11), "PO", 3, 7, CoverageTier.EmployeeSpouse, 70m)),
            ("110", Officer("Patrol officer", "Avery Santos", new DateOnly(2024, 5, 20), "PO", 2, 5, CoverageTier.EmployeeOnly, 70m)),
            ("110", Officer("Patrol officer", null, null, "PO", 1, null, CoverageTier.Family, 40m) with { FirstMonth = 4 }),
            ("110", new PositionDetails
            {
                Title = "Records clerk (part-time)",
                EmployeeName = "Pat Donnelly",
                HireDate = new DateOnly(2020, 2, 3),
                Basis = PayBasis.Hourly,
                Rate = 19.25m,
                AnnualHours = 1_040m,
                RaisePercent = 3m,
                RetirementPlanId = Plan("OPERS"),
                Funds = General(),
            }),

            // ---- 725 Finance: the fiscal officer and a part-time payroll clerk.
            ("725", new PositionDetails
            {
                Title = "Fiscal Officer",
                EmployeeName = "Dana Whitfield",
                HireDate = new DateOnly(2011, 3, 1),
                Rate = 64_800m,
                RaisePercent = 3m,
                LongevityScheduleId = Longevity("Non-union personnel policy"),
                RetirementPlanId = Plan("OPERS"),
                Funds = General(),
                Coverages = Covered(CoverageTier.Family),
            }),
            ("725", new PositionDetails
            {
                Title = "Payroll and accounts payable clerk",
                EmployeeName = "Casey Lin",
                HireDate = new DateOnly(2019, 6, 10),
                Basis = PayBasis.Hourly,
                Rate = 23.10m,
                AnnualHours = 1_560m,
                RaisePercent = 3m,
                RetirementPlanId = Plan("OPERS"),
                Funds = General(),
            }),

            // ---- 620 Streets & Service: the director and crew, shared between the General Fund and the Street fund.
            ("620", new PositionDetails
            {
                Title = "Service Director",
                EmployeeName = "Sam Okafor",
                HireDate = new DateOnly(2010, 5, 3),
                Rate = 71_500m,
                RaisePercent = 3m,
                LongevityScheduleId = Longevity("Non-union personnel policy"),
                RetirementPlanId = Plan("OPERS"),
                Funds = [new(fund("1000"), 50m), new(fund("2011"), 50m)],
                Coverages = Covered(CoverageTier.Family),
                ExtraPay = [new(Extra("Phone or vehicle stipend"), 1_200m)],
            }),
            ("620", StreetWorker("Chris Hollis", new DateOnly(2008, 7, 14), 24.80m, CoverageTier.Family, 25m)),
            ("620", StreetWorker("Rowan Ruiz", new DateOnly(2016, 4, 4), 23.10m, CoverageTier.EmployeeSpouse, 40m)),
            ("620", StreetWorker("Devon Walsh", new DateOnly(2022, 9, 12), 21.40m, CoverageTier.EmployeeOnly, 40m)),
            ("620", new PositionDetails
            {
                Title = "Seasonal laborer (summer paving)",
                Basis = PayBasis.Hourly,
                Rate = 16m,
                FirstMonth = 6,
                LastMonth = 8,
                RetirementPlanId = Plan("OPERS"),
                Funds = [new(fund("2011"), 100m)],
            }),
        ];
    }
}
