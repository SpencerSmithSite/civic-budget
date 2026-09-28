namespace CivicBudget.Domain.Personnel;

/// <summary>
/// Everything about one budgeted position, as plain values: who holds it, how it is paid, and where
/// its cost goes. The same record is what a screen edits, what <see cref="Position"/> stores, and what
/// <see cref="PositionCostCalculator"/> prices. Months are months of the fiscal year (1 is its first).
/// </summary>
public sealed record PositionDetails
{
    public const int TitleMaxLength = 100;
    public const int NameMaxLength = 100;
    public const int GradeMaxLength = 10;
    public const int EmployeeIdMaxLength = 20;
    public const decimal MaxHours = 8784m;
    public const decimal MaxRaisePercent = 100m;

    public string Title { get; init; } = "";

    /// <summary>Null for a vacancy: the position is budgeted, nobody holds it yet.</summary>
    public string? EmployeeName { get; init; }

    /// <summary>
    /// The employee's number in the ERP's payroll, when the position came from (or was matched to) the
    /// ERP's employee list. It is how next year's sync finds this position again; null for a vacancy
    /// or a position entered here.
    /// </summary>
    public string? EmployeeId { get; init; }

    /// <summary>For longevity; null when the position earns none (a vacancy, a part-time seasonal).</summary>
    public DateOnly? HireDate { get; init; }

    public PayBasis Basis { get; init; } = PayBasis.Salary;

    /// <summary>The yearly salary or the hourly rate, when the position is not on a pay scale.</summary>
    public decimal Rate { get; init; }

    /// <summary>Hours paid in a year. For a salaried position it only sets the hourly rate that overtime uses.</summary>
    public decimal AnnualHours { get; init; } = 2080m;

    /// <summary>When set, pay comes from this scale's <see cref="Grade"/> and <see cref="Step"/> and the scale's basis applies.</summary>
    public Guid? PayScaleId { get; init; }

    public string? Grade { get; init; }
    public int? Step { get; init; }

    /// <summary>On a pay scale: the month the position moves up one step (an anniversary step increase), or null.</summary>
    public int? StepIncreaseMonth { get; init; }

    /// <summary>Off a pay scale: a raise, as a percentage, starting in <see cref="RaiseMonth"/>.</summary>
    public decimal RaisePercent { get; init; }

    public int RaiseMonth { get; init; } = 1;

    /// <summary>The months the position is paid: a vacancy filled in month 4 is budgeted from 4 to 12.</summary>
    public int FirstMonth { get; init; } = 1;

    public int LastMonth { get; init; } = 12;

    public Guid? LongevityScheduleId { get; init; }

    /// <summary>Null when the position is in no retirement system (rare; a seasonal worker who is exempt).</summary>
    public Guid? RetirementPlanId { get; init; }

    /// <summary>The employer pays the employee's share of retirement too (a "pick-up", common in Ohio contracts).</summary>
    public bool PicksUpEmployeeShare { get; init; }

    /// <summary>Where base pay lands when not the settings' salaries account (elected officials' salaries, say).</summary>
    public Guid? PayAccountId { get; init; }

    public IReadOnlyList<FundShare> Funds { get; init; } = [];
    public IReadOnlyList<Coverage> Coverages { get; init; } = [];
    public IReadOnlyList<ExtraPayAmount> ExtraPay { get; init; } = [];

    public bool IsVacant => EmployeeName is null;
    public int MonthsPaid => LastMonth - FirstMonth + 1;

    /// <summary>
    /// What is wrong with these details under these rules, as (field, message) pairs; empty when the
    /// position can be priced. Screens show the messages; <see cref="Position"/> refuses to store a
    /// position that has any.
    /// </summary>
    public IReadOnlyList<(string Field, string Message)> Problems(PayrollRules rules)
    {
        var problems = new List<(string, string)>();
        void Add(string field, string message) => problems.Add((field, message));

        if (string.IsNullOrWhiteSpace(Title))
        {
            Add(nameof(Title), "Enter the position's title.");
        }
        else if (Title.Trim().Length > TitleMaxLength)
        {
            Add(nameof(Title), $"The title must be {TitleMaxLength} characters or fewer.");
        }

        if (EmployeeName is { } name && (string.IsNullOrWhiteSpace(name) || name.Trim().Length > NameMaxLength))
        {
            Add(nameof(EmployeeName), $"Enter the employee's name ({NameMaxLength} characters or fewer), or mark the position vacant.");
        }

        if (EmployeeId is { } employeeId && (EmployeeName is null || string.IsNullOrWhiteSpace(employeeId) || employeeId.Trim().Length > EmployeeIdMaxLength))
        {
            Add(nameof(EmployeeId), $"An ERP employee number is up to {EmployeeIdMaxLength} characters and belongs to a filled position.");
        }

        if (HireDate is { } hired && hired > rules.YearEnd)
        {
            Add(nameof(HireDate), "The hire date is after the end of the budget year.");
        }

        if (AnnualHours <= 0m || AnnualHours > MaxHours)
        {
            Add(nameof(AnnualHours), "Hours a year must be more than 0 and no more than 8,784.");
        }

        if (FirstMonth is < 1 or > 12 || LastMonth is < 1 or > 12 || FirstMonth > LastMonth)
        {
            Add(nameof(FirstMonth), "The months paid must run from month 1 to 12, first before last.");
        }

        if (PayScaleId is { } scaleId)
        {
            PayScaleRule? scale = rules.PayScale(scaleId);
            if (scale is null)
            {
                Add(nameof(PayScaleId), "That pay scale is not in this year's settings.");
            }
            else if (Grade is null || Step is null || scale.Rate(Grade, Step.Value) is null)
            {
                Add(nameof(Grade), $"Choose a grade and step that {scale.Name} has.");
            }

            if (StepIncreaseMonth is < 1 or > 12)
            {
                Add(nameof(StepIncreaseMonth), "The step increase month must be 1 to 12.");
            }
        }
        else
        {
            if (Rate <= 0m || !Common.Money.IsStorable(Rate))
            {
                Add(nameof(Rate), Basis == PayBasis.Salary ? "Enter the yearly salary." : "Enter the hourly rate.");
            }

            if (RaisePercent < 0m || RaisePercent > MaxRaisePercent)
            {
                Add(nameof(RaisePercent), "A raise must be between 0% and 100%.");
            }

            if (RaiseMonth is < 1 or > 12)
            {
                Add(nameof(RaiseMonth), "The raise month must be 1 to 12.");
            }
        }

        if (LongevityScheduleId is { } longevity && rules.LongevitySchedule(longevity) is null)
        {
            Add(nameof(LongevityScheduleId), "That longevity schedule is not in this year's settings.");
        }

        if (RetirementPlanId is { } plan && rules.RetirementPlan(plan) is null)
        {
            Add(nameof(RetirementPlanId), "That retirement plan is not in this year's settings.");
        }

        if (PicksUpEmployeeShare && RetirementPlanId is null)
        {
            Add(nameof(PicksUpEmployeeShare), "Choose a retirement plan before picking up the employee's share.");
        }

        if (Funds.Count == 0)
        {
            Add(nameof(Funds), "Choose the fund that pays for the position.");
        }
        else if (Funds.Select(f => f.FundId).Distinct().Count() != Funds.Count)
        {
            Add(nameof(Funds), "Each fund can appear once.");
        }
        else if (Funds.Any(f => f.Percent <= 0m) || Funds.Sum(f => f.Percent) != 100m)
        {
            Add(nameof(Funds), "The funds' shares must be more than 0% each and add up to 100%.");
        }

        foreach (Coverage coverage in Coverages)
        {
            InsurancePlanRule? insurance = rules.InsurancePlan(coverage.PlanId);
            if (insurance is null)
            {
                Add(nameof(Coverages), "An insurance plan is not in this year's settings.");
            }
            else if (insurance.Premium(coverage.Tier) is null)
            {
                Add(nameof(Coverages), $"{insurance.Name} does not offer that coverage.");
            }
        }

        if (Coverages.Select(c => c.PlanId).Distinct().Count() != Coverages.Count)
        {
            Add(nameof(Coverages), "Each insurance plan can appear once.");
        }

        foreach (ExtraPayAmount extra in ExtraPay)
        {
            ExtraPayRule? item = rules.ExtraPayItem(extra.ExtraPayId);
            if (item is null)
            {
                Add(nameof(ExtraPay), "An extra pay item is not in this year's settings.");
            }
            else if (extra.Value < 0m || !Common.Money.IsStorable(extra.Value)
                || (item.Kind == ExtraPayKind.PercentOfBase && extra.Value > 100m)
                || (item.Kind == ExtraPayKind.Hours && extra.Value > MaxHours))
            {
                Add(nameof(ExtraPay), $"{item.Name}: enter {ValueHint(item.Kind)}.");
            }
        }

        if (ExtraPay.Select(e => e.ExtraPayId).Distinct().Count() != ExtraPay.Count)
        {
            Add(nameof(ExtraPay), "Each extra pay item can appear once.");
        }

        return problems;
    }

    private static string ValueHint(ExtraPayKind kind) => kind switch
    {
        ExtraPayKind.Hours => "hours a year, no more than 8,784",
        ExtraPayKind.PercentOfBase => "a percentage from 0 to 100",
        _ => "an amount of zero or more",
    };
}

/// <summary>The share of a position's cost one fund pays, as a percentage.</summary>
public sealed record FundShare(Guid FundId, decimal Percent);

/// <summary>The position is covered by an insurance plan at a tier.</summary>
public sealed record Coverage(Guid PlanId, CoverageTier Tier);

/// <summary>An extra pay item for the position: dollars, hours, or a percentage, as the item's kind says.</summary>
public sealed record ExtraPayAmount(Guid ExtraPayId, decimal Value);
