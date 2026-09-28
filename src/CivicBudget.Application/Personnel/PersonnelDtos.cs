using CivicBudget.Application.Budgets;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Personnel;

namespace CivicBudget.Application.Personnel;

/// <summary>A position and what it costs; <see cref="Problem"/> instead of a cost when the year's settings no longer price it.</summary>
public sealed record PositionDto(Guid Id, PositionDetails Details, PositionCost? Cost, string? Problem);

/// <summary>One of the department's budget lines calculated from its positions.</summary>
public sealed record PersonnelLineDto(Guid LineId, string FundCode, string FundName, string AccountNumber, string AccountCode, string AccountName, decimal Amount, int PositionCount);

/// <summary>Everything the personnel page needs for one department in one budget version.</summary>
public sealed record PersonnelPageDto(
    Guid VersionId,
    int FiscalYear,
    string VersionLabel,
    BudgetStatus Status,
    /// <summary>The calendar month the fiscal year starts in, so month 1 can be shown as "July".</summary>
    int FiscalYearStartMonth,
    LookupDto Department,
    /// <summary>The departments this user may open, for the picker.</summary>
    IReadOnlyList<LookupDto> Departments,
    bool CanEdit,
    /// <summary>The year's personnel settings as the calculator's values; null until someone sets the year up.</summary>
    PayrollRules? Rules,
    bool CanSetUpSettings,
    IReadOnlyList<PositionDto> Positions,
    IReadOnlyList<PersonnelLineDto> Lines,
    IReadOnlyList<LookupDto> Funds,
    /// <summary>Every account, active or not, so a cost's account can always be named; pick lists filter to active expenditure.</summary>
    IReadOnlyList<AccountLookupDto> Accounts,
    /// <summary>False when the lines no longer equal what the positions cost (an adopted budget whose year's settings changed since).</summary>
    bool LinesMatchPositions)
{
    public decimal Total => Positions.Sum(p => p.Cost?.Total ?? 0m);
    public int Vacancies => Positions.Count(p => p.Details.IsVacant);
}

/// <summary>What a position save did to the budget.</summary>
public sealed record PersonnelSavedDto(Guid PositionId, int LinesChanged);

/// <summary>Accounts the settings page proposes for a new year's defaults, guessed from the chart's names and categories.</summary>
public sealed record SuggestedAccounts(Guid? Pay, Guid? Overtime, Guid? Retirement, Guid? Medicare, Guid? WorkersComp, Guid? Insurance);

/// <summary>The personnel settings page for one fiscal year.</summary>
public sealed record PersonnelSettingsPageDto(
    IReadOnlyList<int> Years,
    int FiscalYear,
    DateOnly YearStart,
    DateOnly YearEnd,
    bool CanEdit,
    /// <summary>Null until the year is set up.</summary>
    PersonnelSettingsForm? Form,
    bool PriorYearIsSetUp,
    IReadOnlyList<AccountLookupDto> Accounts,
    SuggestedAccounts Suggested,
    /// <summary>How many positions in this year's budgets use each plan, schedule, item, or scale, by its id.</summary>
    IReadOnlyDictionary<Guid, int> PositionsUsing);

/// <summary>How to start a year's settings: from last year's, or from the Ohio defaults with these accounts.</summary>
public sealed record CreatePersonnelSettingsRequest(
    bool CopyPriorYear,
    Guid PayAccountId,
    Guid? OvertimeAccountId,
    Guid RetirementAccountId,
    Guid MedicareAccountId,
    Guid WorkersCompAccountId);

/// <summary>What a settings save changed in the year's open budgets.</summary>
public sealed record PersonnelSettingsSavedDto(int LinesChanged, IReadOnlyList<string> BudgetsRecalculated);

/// <summary>
/// Rates and hours come back from the database with the column's scale ("14.0000", "2080.00"); a
/// form shows them the way people type them ("14", "2080"). Dividing by 1.000... drops trailing zeros.
/// </summary>
public static class Decimals
{
    public static decimal Trim(decimal value) => value / 1.0000000000000000000000000000m;

    public static decimal? Trim(decimal? value) => value is { } v ? Trim(v) : null;
}

// ---- The settings form: mutable, because the page binds to it; the service turns it into domain calls. ----

public sealed class PersonnelSettingsForm
{
    public decimal StandardHours { get; set; }
    public Guid PayAccountId { get; set; }
    public decimal MedicareRate { get; set; }
    public Guid MedicareAccountId { get; set; }
    public decimal WorkersCompRate { get; set; }
    public Guid WorkersCompAccountId { get; set; }
    public List<RetirementPlanForm> RetirementPlans { get; set; } = [];
    public List<InsurancePlanForm> InsurancePlans { get; set; } = [];
    public List<ExtraPayForm> ExtraPay { get; set; } = [];
    public List<LongevityForm> Longevity { get; set; } = [];
    public List<PayScaleForm> PayScales { get; set; } = [];

    public static PersonnelSettingsForm From(PayrollRules rules) => new()
    {
        StandardHours = Decimals.Trim(rules.StandardHours),
        PayAccountId = rules.PayAccountId,
        MedicareRate = Decimals.Trim(rules.MedicareRate),
        MedicareAccountId = rules.MedicareAccountId,
        WorkersCompRate = Decimals.Trim(rules.WorkersCompRate),
        WorkersCompAccountId = rules.WorkersCompAccountId,
        RetirementPlans = rules.RetirementPlans.Select(p => new RetirementPlanForm { Id = p.Id, Name = p.Name, EmployerRate = Decimals.Trim(p.EmployerRate), EmployeeRate = Decimals.Trim(p.EmployeeRate), AccountId = p.AccountId }).ToList(),
        InsurancePlans = rules.InsurancePlans.Select(p => new InsurancePlanForm
        {
            Id = p.Id,
            Name = p.Name,
            SinglePremium = p.SinglePremium,
            EmployeeSpousePremium = p.EmployeeSpousePremium,
            FamilyPremium = p.FamilyPremium,
            EmployeeSharePercent = Decimals.Trim(p.EmployeeSharePercent),
            AccountId = p.AccountId,
        }).ToList(),
        ExtraPay = rules.ExtraPay.Select(p => new ExtraPayForm
        {
            Id = p.Id,
            Name = p.Name,
            Kind = p.Kind,
            Multiplier = Decimals.Trim(p.Multiplier),
            IsPensionable = p.IsPensionable,
            IsTaxable = p.IsTaxable,
            AccountId = p.AccountId,
        }).ToList(),
        Longevity = rules.Longevity.Select(p => new LongevityForm
        {
            Id = p.Id,
            Name = p.Name,
            Method = p.Method,
            CountedOn = p.CountedOn,
            MaxYears = p.MaxYears,
            AccountId = p.AccountId,
            Steps = p.Steps.Select(s => new LongevityStepForm { MinYears = s.MinYears, Value = Decimals.Trim(s.Value) }).ToList(),
        }).ToList(),
        PayScales = rules.PayScales.Select(PayScaleForm.From).ToList(),
    };
}

public sealed class RetirementPlanForm
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public decimal EmployerRate { get; set; }
    public decimal EmployeeRate { get; set; }
    public Guid AccountId { get; set; }
}

public sealed class InsurancePlanForm
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public decimal SinglePremium { get; set; }
    public decimal? EmployeeSpousePremium { get; set; }
    public decimal? FamilyPremium { get; set; }
    public decimal EmployeeSharePercent { get; set; }
    public Guid AccountId { get; set; }
}

public sealed class ExtraPayForm
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public ExtraPayKind Kind { get; set; } = ExtraPayKind.AnnualAmount;
    public decimal Multiplier { get; set; } = 1m;
    public bool IsPensionable { get; set; }
    public bool IsTaxable { get; set; } = true;

    /// <summary>Null: the cost lands with the position's base pay.</summary>
    public Guid? AccountId { get; set; }
}

public sealed class LongevityForm
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public LongevityMethod Method { get; set; } = LongevityMethod.FlatAmount;
    public ServiceCountedOn CountedOn { get; set; } = ServiceCountedOn.FirstDayOfYear;
    public int? MaxYears { get; set; }
    public Guid? AccountId { get; set; }
    public List<LongevityStepForm> Steps { get; set; } = [];

    /// <summary>The schedule as the calculator sees it, so the page can describe it and work an example while it is edited.</summary>
    public LongevityRule ToRule() => new(Id ?? Guid.Empty, Name, Method, CountedOn, Method == LongevityMethod.AmountPerYear ? MaxYears : null, AccountId,
        Steps.Select(s => new LongevityStepRule(s.MinYears, s.Value)).ToList());
}

public sealed class LongevityStepForm
{
    public int MinYears { get; set; }
    public decimal Value { get; set; }
}

/// <summary>A pay scale as the grid the page edits: a row per grade, a column per step; a blank cell is not on the scale.</summary>
public sealed class PayScaleForm
{
    public Guid? Id { get; set; }
    public string Name { get; set; } = "";
    public PayBasis Basis { get; set; } = PayBasis.Salary;
    public int StepCount { get; set; } = 1;
    public List<PayScaleRowForm> Rows { get; set; } = [];

    public static PayScaleForm From(PayScaleRule scale)
    {
        int steps = scale.Rates.Count == 0 ? 1 : scale.Rates.Max(r => r.Step);
        return new PayScaleForm
        {
            Id = scale.Id,
            Name = scale.Name,
            Basis = scale.Basis,
            StepCount = steps,
            Rows = scale.Rates.GroupBy(r => r.Grade).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase).Select(g => new PayScaleRowForm
            {
                Grade = g.Key,
                Rates = Enumerable.Range(1, steps).Select(step => g.FirstOrDefault(r => r.Step == step)?.Rate).ToList(),
            }).ToList(),
        };
    }

    public IReadOnlyList<PayScaleRateRule> ToRates() =>
        Rows.SelectMany(row => row.Rates.Take(StepCount).Select((rate, i) => (row.Grade, Step: i + 1, rate)))
            .Where(c => c.rate is not null)
            .Select(c => new PayScaleRateRule(c.Grade, c.Step, c.rate!.Value))
            .ToList();
}

public sealed class PayScaleRowForm
{
    public string Grade { get; set; } = "";
    public List<decimal?> Rates { get; set; } = [];
}
