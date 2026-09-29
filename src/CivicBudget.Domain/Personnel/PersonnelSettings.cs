using System.Collections.ObjectModel;
using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Personnel;

/// <summary>
/// One fiscal year's personnel settings: the rates and plans every position in that year's budget is
/// priced with. They belong to a year because they change by year (next year's health premiums arrive
/// while this year's amendment is still being drafted, and must not reach it), and an adopted budget
/// keeps the settings it was built with. A new year starts as a copy of the last one.
/// One row per government and fiscal year; every plan and schedule is a child changed only through it.
/// </summary>
[Audited]
public sealed class PersonnelSettings : Entity, ITenantOwned
{
    public const int NameMaxLength = 60;
    public const decimal DefaultStandardHours = 2080m;
    public const decimal DefaultMedicareRate = 1.45m;

    private readonly List<RetirementPlan> _retirementPlans = [];
    private readonly List<InsurancePlan> _insurancePlans = [];
    private readonly List<ExtraPay> _extraPay = [];
    private readonly List<LongevitySchedule> _longevitySchedules = [];
    private readonly List<PayScale> _payScales = [];

    public Guid GovernmentId { get; private set; }
    public int FiscalYear { get; private set; }

    /// <summary>A full-time year's hours (2,080 is 40 hours for 52 weeks); new positions start with it.</summary>
    public decimal StandardHours { get; private set; }

    /// <summary>Where base pay lands unless a position says otherwise.</summary>
    public Guid PayAccountId { get; private set; }

    public decimal MedicareRate { get; private set; }
    public Guid MedicareAccountId { get; private set; }

    /// <summary>The Ohio BWC rate as a percentage of payroll; public employers each have their own.</summary>
    public decimal WorkersCompRate { get; private set; }

    public Guid WorkersCompAccountId { get; private set; }

    public IReadOnlyCollection<RetirementPlan> RetirementPlans => _retirementPlans.AsReadOnly();
    public IReadOnlyCollection<InsurancePlan> InsurancePlans => _insurancePlans.AsReadOnly();
    public IReadOnlyCollection<ExtraPay> ExtraPay => _extraPay.AsReadOnly();
    public IReadOnlyCollection<LongevitySchedule> LongevitySchedules => _longevitySchedules.AsReadOnly();
    public IReadOnlyCollection<PayScale> PayScales => _payScales.AsReadOnly();

    public PersonnelSettings(Guid governmentId, int fiscalYear, Guid payAccountId, Guid medicareAccountId, Guid workersCompAccountId)
    {
        Guard.Against(governmentId == Guid.Empty, "GovernmentId is required.");
        GovernmentId = governmentId;
        FiscalYear = fiscalYear;
        SetBasics(DefaultStandardHours, payAccountId, DefaultMedicareRate, medicareAccountId, 0m, workersCompAccountId);
    }

    private PersonnelSettings()
    {
    }

    /// <summary>
    /// A year's settings with the Ohio defaults: the retirement systems at their statutory employer and
    /// employee rates, Medicare at 1.45%, and the common kinds of extra pay. Workers' compensation starts
    /// at zero because every public employer has its own BWC rate; insurance plans are the government's
    /// own, so there are none until they are entered.
    /// </summary>
    public static PersonnelSettings CreateDefault(Guid governmentId, int fiscalYear, Guid payAccountId, Guid? overtimeAccountId,
        Guid retirementAccountId, Guid medicareAccountId, Guid workersCompAccountId)
    {
        var settings = new PersonnelSettings(governmentId, fiscalYear, payAccountId, medicareAccountId, workersCompAccountId);
        settings.SaveRetirementPlan(null, "OPERS", 14m, 10m, retirementAccountId);
        settings.SaveRetirementPlan(null, "OPERS law enforcement", 18.1m, 13m, retirementAccountId);
        settings.SaveRetirementPlan(null, "OP&F police", 19.5m, 12.25m, retirementAccountId);
        settings.SaveRetirementPlan(null, "OP&F fire", 24m, 12.25m, retirementAccountId);
        settings.SaveRetirementPlan(null, "Social Security (not in a state system)", 6.2m, 6.2m, retirementAccountId);

        settings.SaveExtraPay(null, "Overtime", ExtraPayKind.Hours, 1.5m, isPensionable: true, isTaxable: true, overtimeAccountId);
        settings.SaveExtraPay(null, "Holiday pay", ExtraPayKind.Hours, 1m, isPensionable: true, isTaxable: true, null);
        settings.SaveExtraPay(null, "Shift differential", ExtraPayKind.AnnualAmount, 1m, isPensionable: true, isTaxable: true, null);
        settings.SaveExtraPay(null, "Certification or education pay", ExtraPayKind.PercentOfBase, 1m, isPensionable: true, isTaxable: true, null);
        settings.SaveExtraPay(null, "Uniform allowance", ExtraPayKind.AnnualAmount, 1m, isPensionable: false, isTaxable: true, null);
        settings.SaveExtraPay(null, "Sick or vacation leave conversion", ExtraPayKind.AnnualAmount, 1m, isPensionable: false, isTaxable: true, null);
        settings.SaveExtraPay(null, "Phone or vehicle stipend", ExtraPayKind.AnnualAmount, 1m, isPensionable: false, isTaxable: true, null);
        settings.SaveExtraPay(null, "Cash in lieu of health insurance", ExtraPayKind.AnnualAmount, 1m, isPensionable: false, isTaxable: true, null);
        settings.SaveExtraPay(null, "Deferred compensation match (457)", ExtraPayKind.PercentOfBase, 1m, isPensionable: false, isTaxable: false, retirementAccountId);
        return settings;
    }

    public void SetBasics(decimal standardHours, Guid payAccountId, decimal medicareRate, Guid medicareAccountId, decimal workersCompRate, Guid workersCompAccountId)
    {
        Guard.Against(standardHours <= 0m || standardHours > PositionDetails.MaxHours, "A full-time year must be more than 0 hours and no more than 8,784.");
        Guard.Against(payAccountId == Guid.Empty || medicareAccountId == Guid.Empty || workersCompAccountId == Guid.Empty, "Choose an account for base pay, Medicare, and workers' compensation.");
        (StandardHours, PayAccountId) = (standardHours, payAccountId);
        (MedicareRate, MedicareAccountId) = (Rate(medicareRate, "The Medicare rate"), medicareAccountId);
        (WorkersCompRate, WorkersCompAccountId) = (Rate(workersCompRate, "The workers' compensation rate"), workersCompAccountId);
    }

    // ---- Retirement plans -------------------------------------------------------------------

    public RetirementPlan SaveRetirementPlan(Guid? id, string name, decimal employerRate, decimal employeeRate, Guid accountId)
    {
        string validName = Name(name, "retirement plan", _retirementPlans.Where(p => p.Id != id).Select(p => p.Name));
        (decimal employer, decimal employee) = (Rate(employerRate, "The employer rate"), Rate(employeeRate, "The employee rate"));
        RequireAccount(accountId);
        RetirementPlan plan = Find(_retirementPlans, id) ?? Add(_retirementPlans, new RetirementPlan(GovernmentId, Id));
        plan.Set(validName, employer, employee, accountId, _retirementPlans.IndexOf(plan));
        return plan;
    }

    public void RemoveRetirementPlan(Guid id) => _retirementPlans.Remove(Existing(_retirementPlans, id));

    // ---- Insurance plans --------------------------------------------------------------------

    public InsurancePlan SaveInsurancePlan(Guid? id, string name, decimal singlePremium, decimal? employeeSpousePremium, decimal? familyPremium,
        decimal employeeSharePercent, Guid accountId)
    {
        string validName = Name(name, "insurance plan", _insurancePlans.Where(p => p.Id != id).Select(p => p.Name));
        Guard.Against(new[] { singlePremium, employeeSpousePremium ?? 0m, familyPremium ?? 0m }.Any(p => p < 0m || !Money.IsStorable(p)),
            "Premiums cannot be negative.");
        decimal share = Rate(employeeSharePercent, "The employee's share");
        RequireAccount(accountId);
        InsurancePlan plan = Find(_insurancePlans, id) ?? Add(_insurancePlans, new InsurancePlan(GovernmentId, Id));
        plan.Set(validName, Money.Round(singlePremium), employeeSpousePremium is { } s ? Money.Round(s) : null,
            familyPremium is { } f ? Money.Round(f) : null, share, accountId, _insurancePlans.IndexOf(plan));
        return plan;
    }

    public void RemoveInsurancePlan(Guid id) => _insurancePlans.Remove(Existing(_insurancePlans, id));

    // ---- Extra pay --------------------------------------------------------------------------

    public ExtraPay SaveExtraPay(Guid? id, string name, ExtraPayKind kind, decimal multiplier, bool isPensionable, bool isTaxable, Guid? accountId)
    {
        string validName = Name(name, "extra pay item", _extraPay.Where(p => p.Id != id).Select(p => p.Name));
        Guard.Against(!Enum.IsDefined(kind), "Choose how the item is figured.");
        Guard.Against(kind == ExtraPayKind.Hours && (multiplier <= 0m || multiplier > 5m), "Hours are paid at more than 0 and no more than 5 times the hourly rate.");
        Guard.Against(accountId == Guid.Empty, "Choose an account, or leave it with base pay.");
        ExtraPay item = Find(_extraPay, id) ?? Add(_extraPay, new ExtraPay(GovernmentId, Id));
        item.Set(validName, kind, kind == ExtraPayKind.Hours ? multiplier : 1m, isPensionable, isTaxable, accountId, _extraPay.IndexOf(item));
        return item;
    }

    public void RemoveExtraPay(Guid id) => _extraPay.Remove(Existing(_extraPay, id));

    // ---- Longevity --------------------------------------------------------------------------

    public LongevitySchedule SaveLongevitySchedule(Guid? id, string name, LongevityMethod method, ServiceCountedOn countedOn, int? maxYears,
        Guid? accountId, IReadOnlyList<LongevityStepRule> steps)
    {
        string validName = Name(name, "longevity schedule", _longevitySchedules.Where(p => p.Id != id).Select(p => p.Name));
        Guard.Against(!Enum.IsDefined(method) || !Enum.IsDefined(countedOn), "Choose how longevity is paid.");
        Guard.Against(steps.Count == 0, $"{validName}: add at least one step.");
        Guard.Against(steps.Any(s => s.MinYears is < 0 or > 60), $"{validName}: years of service must be 0 to 60.");
        Guard.Against(steps.Select(s => s.MinYears).Distinct().Count() != steps.Count, $"{validName}: each number of years can appear once.");
        Guard.Against(steps.Any(s => s.Value <= 0m || !Money.IsStorable(s.Value)), $"{validName}: each step pays more than zero.");
        Guard.Against(method == LongevityMethod.PercentOfPay && steps.Any(s => s.Value > 100m), $"{validName}: a percentage of pay is 100 or less.");
        Guard.Against(maxYears is { } max && (max < steps.Min(s => s.MinYears) || max > 60), $"{validName}: the most years counted must be at least the first step's years and no more than 60.");
        Guard.Against(accountId == Guid.Empty, "Choose an account, or leave it with base pay.");
        LongevitySchedule schedule = Find(_longevitySchedules, id) ?? Add(_longevitySchedules, new LongevitySchedule(GovernmentId, Id));
        schedule.Set(validName, method, countedOn, method == LongevityMethod.AmountPerYear ? maxYears : null, accountId, steps, _longevitySchedules.IndexOf(schedule));
        return schedule;
    }

    public void RemoveLongevitySchedule(Guid id) => _longevitySchedules.Remove(Existing(_longevitySchedules, id));

    // ---- Pay scales -------------------------------------------------------------------------

    public PayScale SavePayScale(Guid? id, string name, PayBasis basis, IReadOnlyList<PayScaleRateRule> rates)
    {
        string validName = Name(name, "pay scale", _payScales.Where(p => p.Id != id).Select(p => p.Name));
        Guard.Against(!Enum.IsDefined(basis), "Choose whether the scale is yearly salaries or hourly rates.");
        Guard.Against(rates.Count == 0, $"{validName}: add at least one grade and step.");
        Guard.Against(rates.Any(r => string.IsNullOrWhiteSpace(r.Grade) || r.Grade.Trim().Length > PositionDetails.GradeMaxLength),
            $"{validName}: every grade needs a name of {PositionDetails.GradeMaxLength} characters or fewer.");
        Guard.Against(rates.Any(r => r.Step is < 1 or > 40), $"{validName}: steps are numbered 1 to 40.");
        Guard.Against(rates.Any(r => r.Rate <= 0m || !Money.IsStorable(r.Rate)), $"{validName}: every rate is more than zero.");
        Guard.Against(rates.GroupBy(r => (r.Grade.Trim().ToUpperInvariant(), r.Step)).Any(g => g.Count() > 1), $"{validName}: each grade and step can appear once.");
        PayScale scale = Find(_payScales, id) ?? Add(_payScales, new PayScale(GovernmentId, Id));
        scale.Set(validName, basis, rates, _payScales.IndexOf(scale));
        return scale;
    }

    public void RemovePayScale(Guid id) => _payScales.Remove(Existing(_payScales, id));

    // ---- Use --------------------------------------------------------------------------------

    /// <summary>
    /// The settings as the calculator's plain values, for the fiscal year that runs from <paramref name="yearStart"/>
    /// to <paramref name="yearEnd"/>. <paramref name="fundCodes"/> is every fund's number, by id.
    /// </summary>
    public PayrollRules ToRules(DateOnly yearStart, DateOnly yearEnd, IReadOnlyDictionary<Guid, string> fundCodes) => new(
        FiscalYear, yearStart, yearEnd, StandardHours,
        PayAccountId, MedicareRate, MedicareAccountId, WorkersCompRate, WorkersCompAccountId,
        _retirementPlans.OrderBy(p => p.SortOrder).Select(p => new RetirementPlanRule(p.Id, p.Name, p.EmployerRate, p.EmployeeRate, p.AccountId)).ToList(),
        _insurancePlans.OrderBy(p => p.SortOrder).Select(p => new InsurancePlanRule(p.Id, p.Name, p.SinglePremium, p.EmployeeSpousePremium, p.FamilyPremium, p.EmployeeSharePercent, p.AccountId)).ToList(),
        _extraPay.OrderBy(p => p.SortOrder).Select(p => new ExtraPayRule(p.Id, p.Name, p.Kind, p.Multiplier, p.IsPensionable, p.IsTaxable, p.AccountId)).ToList(),
        _longevitySchedules.OrderBy(p => p.SortOrder).Select(p => new LongevityRule(p.Id, p.Name, p.Method, p.CountedOn, p.MaxYears, p.AccountId,
            p.Steps.OrderBy(s => s.MinYears).Select(s => new LongevityStepRule(s.MinYears, s.Value)).ToList())).ToList(),
        _payScales.OrderBy(p => p.SortOrder).Select(p => new PayScaleRule(p.Id, p.Name, p.Basis,
            p.Rates.OrderBy(r => r.Grade).ThenBy(r => r.Step).Select(r => new PayScaleRateRule(r.Grade, r.Step, r.Rate)).ToList())).ToList(),
        fundCodes);

    /// <summary>
    /// The next year's settings, starting as a copy of these. Every plan gets a new id (it belongs to
    /// the new year), and the map from old ids to new is returned so positions carried into the new
    /// year's budget point at the new year's plans.
    /// </summary>
    public (PersonnelSettings Copy, IReadOnlyDictionary<Guid, Guid> NewIds) CopyTo(int fiscalYear)
    {
        Guard.Against(fiscalYear == FiscalYear, "The settings already belong to that year.");
        var copy = new PersonnelSettings(GovernmentId, fiscalYear, PayAccountId, MedicareAccountId, WorkersCompAccountId);
        copy.SetBasics(StandardHours, PayAccountId, MedicareRate, MedicareAccountId, WorkersCompRate, WorkersCompAccountId);
        var ids = new Dictionary<Guid, Guid>();
        // Only the plans are read here, so the rules need no dates or funds.
        PayrollRules rules = ToRules(DateOnly.MinValue, DateOnly.MaxValue, ReadOnlyDictionary<Guid, string>.Empty);
        foreach (RetirementPlanRule p in rules.RetirementPlans)
        {
            ids[p.Id] = copy.SaveRetirementPlan(null, p.Name, p.EmployerRate, p.EmployeeRate, p.AccountId).Id;
        }

        foreach (InsurancePlanRule p in rules.InsurancePlans)
        {
            ids[p.Id] = copy.SaveInsurancePlan(null, p.Name, p.SinglePremium, p.EmployeeSpousePremium, p.FamilyPremium, p.EmployeeSharePercent, p.AccountId).Id;
        }

        foreach (ExtraPayRule p in rules.ExtraPay)
        {
            ids[p.Id] = copy.SaveExtraPay(null, p.Name, p.Kind, p.Multiplier, p.IsPensionable, p.IsTaxable, p.AccountId).Id;
        }

        foreach (LongevityRule p in rules.Longevity)
        {
            ids[p.Id] = copy.SaveLongevitySchedule(null, p.Name, p.Method, p.CountedOn, p.MaxYears, p.AccountId, p.Steps).Id;
        }

        foreach (PayScaleRule p in rules.PayScales)
        {
            ids[p.Id] = copy.SavePayScale(null, p.Name, p.Basis, p.Rates).Id;
        }

        return (copy, ids);
    }

    /// <summary>
    /// For each plan, schedule, item, and scale in <paramref name="earlier"/>, the id of the one of the
    /// same kind and name here: how positions carried from last year find this year's plans when this
    /// year was set up on its own rather than copied. One with no match here is left out.
    /// </summary>
    public IReadOnlyDictionary<Guid, Guid> IdsMatching(PersonnelSettings earlier)
    {
        var ids = new Dictionary<Guid, Guid>();
        void Match<T>(IEnumerable<T> old, IEnumerable<T> current, Func<T, string> name) where T : Entity
        {
            foreach (T item in old)
            {
                if (current.FirstOrDefault(c => string.Equals(name(c), name(item), StringComparison.OrdinalIgnoreCase)) is { } match)
                {
                    ids[item.Id] = match.Id;
                }
            }
        }

        Match(earlier._retirementPlans, _retirementPlans, p => p.Name);
        Match(earlier._insurancePlans, _insurancePlans, p => p.Name);
        Match(earlier._extraPay, _extraPay, p => p.Name);
        Match(earlier._longevitySchedules, _longevitySchedules, p => p.Name);
        Match(earlier._payScales, _payScales, p => p.Name);
        return ids;
    }

    private static string Name(string name, string what, IEnumerable<string> others)
    {
        string trimmed = Guard.MaxLength(Guard.NotNullOrWhiteSpace(name, $"The {what}'s name"), NameMaxLength, $"The {what}'s name");
        Guard.Against(others.Any(o => string.Equals(o, trimmed, StringComparison.OrdinalIgnoreCase)), $"There is already a {what} named {trimmed}.");
        return trimmed;
    }

    private static decimal Rate(decimal rate, string what)
    {
        Guard.Against(rate is < 0m or > 100m, $"{what} must be between 0% and 100%.");
        return Math.Round(rate, 4, MidpointRounding.AwayFromZero);
    }

    private static void RequireAccount(Guid accountId) => Guard.Against(accountId == Guid.Empty, "Choose the account the cost lands on.");

    private static T? Find<T>(List<T> items, Guid? id) where T : Entity =>
        id is { } wanted ? Existing(items, wanted) : null;

    private static T Existing<T>(List<T> items, Guid id) where T : Entity =>
        items.FirstOrDefault(i => i.Id == id) ?? throw new DomainException("That item is not in these settings.");

    private static T Add<T>(List<T> items, T item)
    {
        items.Add(item);
        return item;
    }
}

/// <summary>A retirement system and its employer and employee rates.</summary>
[Audited]
public sealed class RetirementPlan : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public Guid PersonnelSettingsId { get; private set; }
    public string Name { get; private set; } = null!;
    public decimal EmployerRate { get; private set; }
    public decimal EmployeeRate { get; private set; }
    public Guid AccountId { get; private set; }
    public int SortOrder { get; private set; }

    internal RetirementPlan(Guid governmentId, Guid settingsId) => (GovernmentId, PersonnelSettingsId) = (governmentId, settingsId);

    private RetirementPlan()
    {
    }

    internal void Set(string name, decimal employerRate, decimal employeeRate, Guid accountId, int sortOrder) =>
        (Name, EmployerRate, EmployeeRate, AccountId, SortOrder) = (name, employerRate, employeeRate, accountId, sortOrder);
}

/// <summary>An insurance plan: monthly premium by coverage tier, and the share employees pay.</summary>
[Audited]
public sealed class InsurancePlan : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public Guid PersonnelSettingsId { get; private set; }
    public string Name { get; private set; } = null!;
    public decimal SinglePremium { get; private set; }

    /// <summary>Null when the plan does not offer the tier (a life policy covers the employee only).</summary>
    public decimal? EmployeeSpousePremium { get; private set; }

    public decimal? FamilyPremium { get; private set; }
    public decimal EmployeeSharePercent { get; private set; }
    public Guid AccountId { get; private set; }
    public int SortOrder { get; private set; }

    internal InsurancePlan(Guid governmentId, Guid settingsId) => (GovernmentId, PersonnelSettingsId) = (governmentId, settingsId);

    private InsurancePlan()
    {
    }

    internal void Set(string name, decimal single, decimal? spouse, decimal? family, decimal share, Guid accountId, int sortOrder) =>
        (Name, SinglePremium, EmployeeSpousePremium, FamilyPremium, EmployeeSharePercent, AccountId, SortOrder) = (name, single, spouse, family, share, accountId, sortOrder);
}

/// <summary>A kind of pay beyond base pay (overtime, holiday pay, a uniform allowance) and how it is figured.</summary>
[Audited]
public sealed class ExtraPay : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public Guid PersonnelSettingsId { get; private set; }
    public string Name { get; private set; } = null!;
    public ExtraPayKind Kind { get; private set; }

    /// <summary>For hours: the multiple of the hourly rate (1.5 for overtime). 1 otherwise.</summary>
    public decimal Multiplier { get; private set; }

    public bool IsPensionable { get; private set; }
    public bool IsTaxable { get; private set; }
    public Guid? AccountId { get; private set; }
    public int SortOrder { get; private set; }

    internal ExtraPay(Guid governmentId, Guid settingsId) => (GovernmentId, PersonnelSettingsId) = (governmentId, settingsId);

    private ExtraPay()
    {
    }

    internal void Set(string name, ExtraPayKind kind, decimal multiplier, bool isPensionable, bool isTaxable, Guid? accountId, int sortOrder) =>
        (Name, Kind, Multiplier, IsPensionable, IsTaxable, AccountId, SortOrder) = (name, kind, multiplier, isPensionable, isTaxable, accountId, sortOrder);
}

/// <summary>A longevity schedule from a contract or the personnel policy.</summary>
[Audited]
public sealed class LongevitySchedule : Entity, ITenantOwned
{
    private readonly List<LongevityStep> _steps = [];

    public Guid GovernmentId { get; private set; }
    public Guid PersonnelSettingsId { get; private set; }
    public string Name { get; private set; } = null!;
    public LongevityMethod Method { get; private set; }
    public ServiceCountedOn CountedOn { get; private set; }

    /// <summary>For an amount per year: years beyond this add nothing. Null for no limit.</summary>
    public int? MaxYears { get; private set; }

    public Guid? AccountId { get; private set; }
    public int SortOrder { get; private set; }
    public IReadOnlyCollection<LongevityStep> Steps => _steps.AsReadOnly();

    internal LongevitySchedule(Guid governmentId, Guid settingsId) => (GovernmentId, PersonnelSettingsId) = (governmentId, settingsId);

    private LongevitySchedule()
    {
    }

    internal void Set(string name, LongevityMethod method, ServiceCountedOn countedOn, int? maxYears, Guid? accountId, IReadOnlyList<LongevityStepRule> steps, int sortOrder)
    {
        (Name, Method, CountedOn, MaxYears, AccountId, SortOrder) = (name, method, countedOn, maxYears, accountId, sortOrder);
        _steps.Clear();
        _steps.AddRange(steps.OrderBy(s => s.MinYears).Select(s => new LongevityStep(GovernmentId, Id, s.MinYears, Money.Round(s.Value))));
    }
}

/// <summary>One step of a longevity schedule: from this many years, this value.</summary>
public sealed class LongevityStep : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public Guid LongevityScheduleId { get; private set; }
    public int MinYears { get; private set; }
    public decimal Value { get; private set; }

    internal LongevityStep(Guid governmentId, Guid scheduleId, int minYears, decimal value) =>
        (GovernmentId, LongevityScheduleId, MinYears, Value) = (governmentId, scheduleId, minYears, value);

    private LongevityStep()
    {
    }
}

/// <summary>A salary schedule: a rate for each grade and step.</summary>
[Audited]
public sealed class PayScale : Entity, ITenantOwned
{
    private readonly List<PayScaleRate> _rates = [];

    public Guid GovernmentId { get; private set; }
    public Guid PersonnelSettingsId { get; private set; }
    public string Name { get; private set; } = null!;
    public PayBasis Basis { get; private set; }
    public int SortOrder { get; private set; }
    public IReadOnlyCollection<PayScaleRate> Rates => _rates.AsReadOnly();

    internal PayScale(Guid governmentId, Guid settingsId) => (GovernmentId, PersonnelSettingsId) = (governmentId, settingsId);

    private PayScale()
    {
    }

    internal void Set(string name, PayBasis basis, IReadOnlyList<PayScaleRateRule> rates, int sortOrder)
    {
        (Name, Basis, SortOrder) = (name, basis, sortOrder);
        _rates.Clear();
        _rates.AddRange(rates.Select(r => new PayScaleRate(GovernmentId, Id, r.Grade.Trim(), r.Step, Money.Round(r.Rate))));
    }
}

/// <summary>The rate for one grade and step of a pay scale.</summary>
public sealed class PayScaleRate : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public Guid PayScaleId { get; private set; }
    public string Grade { get; private set; } = null!;
    public int Step { get; private set; }
    public decimal Rate { get; private set; }

    internal PayScaleRate(Guid governmentId, Guid scaleId, string grade, int step, decimal rate) =>
        (GovernmentId, PayScaleId, Grade, Step, Rate) = (governmentId, scaleId, grade, step, rate);

    private PayScaleRate()
    {
    }
}
