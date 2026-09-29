using CivicBudget.Domain.Personnel;

namespace CivicBudget.Domain.Tests.Personnel;

/// <summary>FY2027 personnel settings (a calendar year) with one of everything, and a builder for positions.</summary>
internal sealed class PersonnelTestData
{
    public static readonly Guid SalariesAccount = Guid.CreateVersion7();
    public static readonly Guid OvertimeAccount = Guid.CreateVersion7();
    public static readonly Guid RetirementAccount = Guid.CreateVersion7();
    public static readonly Guid MedicareAccount = Guid.CreateVersion7();
    public static readonly Guid WorkersCompAccount = Guid.CreateVersion7();
    public static readonly Guid InsuranceAccount = Guid.CreateVersion7();
    public static readonly Guid GeneralFund = Guid.CreateVersion7();
    public static readonly Guid StreetFund = Guid.CreateVersion7();
    public static readonly IReadOnlyDictionary<Guid, string> FundCodes = new Dictionary<Guid, string> { [GeneralFund] = "1000", [StreetFund] = "2011" };

    public PersonnelSettings Settings { get; }

    public PersonnelTestData(decimal workersCompRate = 2m)
    {
        Settings = PersonnelSettings.CreateDefault(TestData.GovernmentId, 2027, SalariesAccount, OvertimeAccount, RetirementAccount, MedicareAccount, WorkersCompAccount);
        Settings.SetBasics(2080m, SalariesAccount, 1.45m, MedicareAccount, workersCompRate, WorkersCompAccount);
        Settings.SaveInsurancePlan(null, "Medical", 800m, 1_600m, 2_000m, 15m, InsuranceAccount);
        Settings.SaveInsurancePlan(null, "Life", 10m, null, null, 0m, InsuranceAccount);
        Settings.SaveLongevitySchedule(null, "Step table", LongevityMethod.FlatAmount, ServiceCountedOn.FirstDayOfYear, null, null,
            [new(5, 500m), new(10, 750m), new(15, 1_000m)]);
        Settings.SaveLongevitySchedule(null, "Percent", LongevityMethod.PercentOfPay, ServiceCountedOn.FirstDayOfYear, null, null,
            [new(5, 2m), new(10, 4m)]);
        Settings.SaveLongevitySchedule(null, "Per year", LongevityMethod.AmountPerYear, ServiceCountedOn.LastDayOfYear, 20, null,
            [new(5, 100m)]);
        Settings.SavePayScale(null, "Non-union", PayBasis.Salary, [new("B", 1, 50_000m), new("B", 2, 52_000m)]);
    }

    public PayrollRules Rules => Settings.ToRules(new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), FundCodes);

    public Guid Plan(string name) => Rules.RetirementPlans.Single(p => p.Name == name).Id;
    public Guid Insurance(string name) => Rules.InsurancePlans.Single(p => p.Name == name).Id;
    public Guid Extra(string name) => Rules.ExtraPay.Single(p => p.Name == name).Id;
    public Guid Longevity(string name) => Rules.Longevity.Single(p => p.Name == name).Id;
    public Guid Scale(string name) => Rules.PayScales.Single(p => p.Name == name).Id;

    /// <summary>A salaried clerk at $60,000 in OPERS, all in the General Fund, with no benefits beyond the required ones.</summary>
    public PositionDetails Clerk() => new()
    {
        Title = "Clerk",
        EmployeeName = "Jordan Reyes",
        Basis = PayBasis.Salary,
        Rate = 60_000m,
        RetirementPlanId = Plan("OPERS"),
        Funds = [new FundShare(GeneralFund, 100m)],
    };
}
