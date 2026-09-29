namespace CivicBudget.Domain.Personnel;

/// <summary>How a position is paid: a yearly salary, or an hourly rate times the hours it works.</summary>
public enum PayBasis
{
    Salary = 1,
    Hourly = 2,
}

/// <summary>Who an insurance plan covers besides the employee; each tier has its own monthly premium.</summary>
public enum CoverageTier
{
    EmployeeOnly = 1,
    EmployeeSpouse = 2,
    Family = 3,
}

/// <summary>How an extra pay item is figured for one position.</summary>
public enum ExtraPayKind
{
    /// <summary>A dollar amount for the year (uniform allowance, a stipend).</summary>
    AnnualAmount = 1,

    /// <summary>Hours at a multiple of the position's hourly rate (overtime at 1.5, holiday pay at 1.0).</summary>
    Hours = 2,

    /// <summary>A percentage of the position's base pay (certification pay, a deferred compensation match).</summary>
    PercentOfBase = 3,
}

/// <summary>The three ways Ohio contracts usually pay longevity.</summary>
public enum LongevityMethod
{
    /// <summary>A step table: a flat yearly amount once the employee reaches each number of years.</summary>
    FlatAmount = 1,

    /// <summary>A percentage of base pay once the employee reaches each number of years.</summary>
    PercentOfPay = 2,

    /// <summary>An amount for every year of service, once the employee reaches a number of years.</summary>
    AmountPerYear = 3,
}

/// <summary>The day on which completed years of service are counted for longevity.</summary>
public enum ServiceCountedOn
{
    FirstDayOfYear = 1,
    LastDayOfYear = 2,
}

/// <summary>
/// One year's personnel settings as plain values: everything <see cref="PositionCostCalculator"/>
/// needs, and nothing tied to the database, so a screen can price a position while it is being typed.
/// Rates are percentages (14 means 14%). <see cref="FundCodes"/> numbers the government's funds: when
/// two funds pay equal shares of a position, the lower fund number takes the odd cent.
/// </summary>
public sealed record PayrollRules(
    int FiscalYear,
    DateOnly YearStart,
    DateOnly YearEnd,
    decimal StandardHours,
    Guid PayAccountId,
    decimal MedicareRate,
    Guid MedicareAccountId,
    decimal WorkersCompRate,
    Guid WorkersCompAccountId,
    IReadOnlyList<RetirementPlanRule> RetirementPlans,
    IReadOnlyList<InsurancePlanRule> InsurancePlans,
    IReadOnlyList<ExtraPayRule> ExtraPay,
    IReadOnlyList<LongevityRule> Longevity,
    IReadOnlyList<PayScaleRule> PayScales,
    IReadOnlyDictionary<Guid, string> FundCodes)
{
    public RetirementPlanRule? RetirementPlan(Guid id) => RetirementPlans.FirstOrDefault(p => p.Id == id);
    public InsurancePlanRule? InsurancePlan(Guid id) => InsurancePlans.FirstOrDefault(p => p.Id == id);
    public ExtraPayRule? ExtraPayItem(Guid id) => ExtraPay.FirstOrDefault(p => p.Id == id);
    public LongevityRule? LongevitySchedule(Guid id) => Longevity.FirstOrDefault(p => p.Id == id);
    public PayScaleRule? PayScale(Guid id) => PayScales.FirstOrDefault(p => p.Id == id);
}

/// <summary>A retirement system: OPERS, OP&amp;F police or fire, or Social Security for employees outside a state system.</summary>
public sealed record RetirementPlanRule(Guid Id, string Name, decimal EmployerRate, decimal EmployeeRate, Guid AccountId);

/// <summary>An insurance plan's monthly premiums by tier (null where the tier is not offered) and the employee's share of them.</summary>
public sealed record InsurancePlanRule(
    Guid Id, string Name, decimal SinglePremium, decimal? EmployeeSpousePremium, decimal? FamilyPremium, decimal EmployeeSharePercent, Guid AccountId)
{
    public decimal? Premium(CoverageTier tier) => tier switch
    {
        CoverageTier.EmployeeOnly => SinglePremium,
        CoverageTier.EmployeeSpouse => EmployeeSpousePremium,
        CoverageTier.Family => FamilyPremium,
        _ => null,
    };

    public IEnumerable<CoverageTier> OfferedTiers =>
        Enum.GetValues<CoverageTier>().Where(t => Premium(t) is not null);
}

/// <summary>
/// A kind of pay beyond base pay. <see cref="IsPensionable"/> is whether it is earnable salary for the
/// retirement systems; <see cref="IsTaxable"/> is whether Medicare and workers' compensation are
/// charged on it. A null account means it lands with the position's base pay.
/// </summary>
public sealed record ExtraPayRule(Guid Id, string Name, ExtraPayKind Kind, decimal Multiplier, bool IsPensionable, bool IsTaxable, Guid? AccountId);

/// <summary>A longevity schedule; the highest step the employee has reached applies.</summary>
public sealed record LongevityRule(
    Guid Id, string Name, LongevityMethod Method, ServiceCountedOn CountedOn, int? MaxYears, Guid? AccountId, IReadOnlyList<LongevityStepRule> Steps);

public sealed record LongevityStepRule(int MinYears, decimal Value);

/// <summary>A salary schedule of grades and steps.</summary>
public sealed record PayScaleRule(Guid Id, string Name, PayBasis Basis, IReadOnlyList<PayScaleRateRule> Rates)
{
    public decimal? Rate(string grade, int step) =>
        Rates.FirstOrDefault(r => string.Equals(r.Grade, grade, StringComparison.OrdinalIgnoreCase) && r.Step == step)?.Rate;
}

public sealed record PayScaleRateRule(string Grade, int Step, decimal Rate);

/// <summary>
/// The department whose positions are being applied, and the funds and accounts its lines may need,
/// so <see cref="Budgets.BudgetVersion.ApplyPersonnel"/> can create a line a position newly costs into.
/// </summary>
public sealed record PersonnelChart(
    Departments.Department Department,
    IReadOnlyDictionary<Guid, Funds.Fund> Funds,
    IReadOnlyDictionary<Guid, Accounts.Account> Accounts);

/// <summary>A line the personnel calculation changed: its amount before (null when it was just created) and after.</summary>
public sealed record PersonnelLineChange(Guid LineId, Guid FundId, Guid AccountId, decimal? Before, decimal After, int? PositionCount);
