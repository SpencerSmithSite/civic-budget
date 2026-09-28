using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Personnel;

/// <summary>
/// One budgeted position in a department, filled or vacant, in one budget version. Positions belong
/// to the version the way its lines do: an adopted budget's positions are fixed, an amendment copies
/// them, and next year's budget carries them forward. Created and changed only through
/// <see cref="Budgets.BudgetVersion"/>, which owns the editability rule and keeps the department's
/// lines equal to what its positions cost.
/// </summary>
[Audited]
public sealed class Position : Entity, ITenantOwned
{
    private readonly List<PositionFundShare> _funds = [];
    private readonly List<PositionCoverage> _coverages = [];
    private readonly List<PositionExtraPay> _extraPay = [];

    public Guid GovernmentId { get; private set; }
    public Guid BudgetVersionId { get; private set; }
    public Guid DepartmentId { get; private set; }

    public string Title { get; private set; } = null!;
    public string? EmployeeName { get; private set; }
    public string? EmployeeId { get; private set; }
    public DateOnly? HireDate { get; private set; }
    public PayBasis Basis { get; private set; }
    public decimal Rate { get; private set; }
    public decimal AnnualHours { get; private set; }
    public Guid? PayScaleId { get; private set; }
    public string? Grade { get; private set; }
    public int? Step { get; private set; }
    public int? StepIncreaseMonth { get; private set; }
    public decimal RaisePercent { get; private set; }
    public int RaiseMonth { get; private set; }
    public int FirstMonth { get; private set; }
    public int LastMonth { get; private set; }
    public Guid? LongevityScheduleId { get; private set; }
    public Guid? RetirementPlanId { get; private set; }
    public bool PicksUpEmployeeShare { get; private set; }
    public Guid? PayAccountId { get; private set; }

    public IReadOnlyCollection<PositionFundShare> Funds => _funds.AsReadOnly();
    public IReadOnlyCollection<PositionCoverage> Coverages => _coverages.AsReadOnly();
    public IReadOnlyCollection<PositionExtraPay> ExtraPay => _extraPay.AsReadOnly();

    internal Position(Guid governmentId, Guid budgetVersionId, Guid departmentId, PositionDetails details, PayrollRules rules)
    {
        GovernmentId = governmentId;
        BudgetVersionId = budgetVersionId;
        DepartmentId = departmentId;
        Set(details, rules);
    }

    private Position()
    {
    }

    /// <summary>Replaces everything about the position; refused when the details do not price under the year's rules.</summary>
    internal void Set(PositionDetails details, PayrollRules rules)
    {
        IReadOnlyList<(string Field, string Message)> problems = details.Problems(rules);
        Guard.Against(problems.Count > 0, problems.Count > 0 ? problems[0].Message : "");

        bool onScale = details.PayScaleId is not null;
        Title = details.Title.Trim();
        EmployeeName = details.EmployeeName?.Trim();
        EmployeeId = details.EmployeeId?.Trim();
        HireDate = details.HireDate;
        Basis = onScale ? rules.PayScale(details.PayScaleId!.Value)!.Basis : details.Basis;
        Rate = onScale ? 0m : Money.Round(details.Rate);
        AnnualHours = details.AnnualHours;
        PayScaleId = details.PayScaleId;
        Grade = onScale ? details.Grade!.Trim() : null;
        Step = onScale ? details.Step : null;
        StepIncreaseMonth = onScale ? details.StepIncreaseMonth : null;
        RaisePercent = onScale ? 0m : details.RaisePercent;
        RaiseMonth = onScale ? 1 : details.RaiseMonth;
        FirstMonth = details.FirstMonth;
        LastMonth = details.LastMonth;
        LongevityScheduleId = details.LongevityScheduleId;
        RetirementPlanId = details.RetirementPlanId;
        PicksUpEmployeeShare = details.RetirementPlanId is not null && details.PicksUpEmployeeShare;
        PayAccountId = details.PayAccountId;

        _funds.Clear();
        _funds.AddRange(details.Funds.Select(f => new PositionFundShare(GovernmentId, Id, f.FundId, f.Percent)));
        _coverages.Clear();
        _coverages.AddRange(details.Coverages.Select(c => new PositionCoverage(GovernmentId, Id, c.PlanId, c.Tier)));
        _extraPay.Clear();
        _extraPay.AddRange(details.ExtraPay.Where(e => e.Value != 0m).Select(e => new PositionExtraPay(GovernmentId, Id, e.ExtraPayId, e.Value)));
    }

    public PositionDetails ToDetails() => new()
    {
        Title = Title,
        EmployeeName = EmployeeName,
        EmployeeId = EmployeeId,
        HireDate = HireDate,
        Basis = Basis,
        Rate = Rate,
        AnnualHours = AnnualHours,
        PayScaleId = PayScaleId,
        Grade = Grade,
        Step = Step,
        StepIncreaseMonth = StepIncreaseMonth,
        RaisePercent = RaisePercent,
        RaiseMonth = RaiseMonth,
        FirstMonth = FirstMonth,
        LastMonth = LastMonth,
        LongevityScheduleId = LongevityScheduleId,
        RetirementPlanId = RetirementPlanId,
        PicksUpEmployeeShare = PicksUpEmployeeShare,
        PayAccountId = PayAccountId,
        Funds = _funds.Select(f => new FundShare(f.FundId, f.Percent)).ToList(),
        Coverages = _coverages.Select(c => new Coverage(c.InsurancePlanId, c.Tier)).ToList(),
        ExtraPay = _extraPay.Select(e => new ExtraPayAmount(e.ExtraPayId, e.Value)).ToList(),
    };

    /// <summary>An exact copy for an amendment: same year, same settings, same everything.</summary>
    internal Position CopyTo(Guid targetVersionId)
    {
        var copy = new Position { GovernmentId = GovernmentId, BudgetVersionId = targetVersionId, DepartmentId = DepartmentId };
        copy.CopyFields(ToDetails());
        return copy;
    }

    /// <summary>
    /// The position as it starts next year: at the rate it ends this year (after its raise, or one step
    /// up after its step increase), with no raise planned yet, paid all year, and pointing at next
    /// year's copies of its plans. Longevity moves on by itself, because years of service are counted
    /// from the hire date. Plans that have no copy in next year's settings are dropped.
    /// </summary>
    internal Position CarryForward(Guid targetVersionId, PayrollRules thisYear, IReadOnlyDictionary<Guid, Guid> newIds)
    {
        PositionDetails details = ToDetails();
        Guid? Map(Guid? id) => id is { } old && newIds.TryGetValue(old, out Guid mapped) ? mapped : null;

        // A position that no longer prices (its scale's grade was since removed) keeps the rate it has.
        (decimal startRate, decimal endRate) = (details.Rate, details.Rate);
        if (details.Problems(thisYear).Count == 0)
        {
            PositionCost cost = PositionCostCalculator.Calculate(details, thisYear);
            (startRate, endRate) = (cost.StartingRate, cost.EndingRate);
        }

        bool stepped = details.PayScaleId is not null && details.StepIncreaseMonth is not null && endRate != startRate;
        PositionDetails next = details with
        {
            Rate = details.PayScaleId is null ? endRate : 0m,
            Step = stepped ? details.Step + 1 : details.Step,
            StepIncreaseMonth = details.StepIncreaseMonth,
            RaisePercent = 0m,
            RaiseMonth = 1,
            FirstMonth = 1,
            LastMonth = 12,
            PayScaleId = Map(details.PayScaleId),
            LongevityScheduleId = Map(details.LongevityScheduleId),
            RetirementPlanId = Map(details.RetirementPlanId),
            PicksUpEmployeeShare = Map(details.RetirementPlanId) is not null && details.PicksUpEmployeeShare,
            Coverages = details.Coverages.Where(c => newIds.ContainsKey(c.PlanId)).Select(c => c with { PlanId = newIds[c.PlanId] }).ToList(),
            ExtraPay = details.ExtraPay.Where(e => newIds.ContainsKey(e.ExtraPayId)).Select(e => e with { ExtraPayId = newIds[e.ExtraPayId] }).ToList(),
        };

        // A position whose scale did not come across keeps paying what it ended the year at.
        if (details.PayScaleId is not null && next.PayScaleId is null)
        {
            next = next with { Rate = endRate, Grade = null, Step = null, StepIncreaseMonth = null };
        }

        var copy = new Position { GovernmentId = GovernmentId, BudgetVersionId = targetVersionId, DepartmentId = DepartmentId };
        copy.CopyFields(next);
        return copy;
    }

    /// <summary>Stores details without checking them against rules: used for copies of positions that were already checked.</summary>
    private void CopyFields(PositionDetails d)
    {
        (Title, EmployeeName, EmployeeId, HireDate, Basis, Rate, AnnualHours) = (d.Title, d.EmployeeName, d.EmployeeId, d.HireDate, d.Basis, d.Rate, d.AnnualHours);
        (PayScaleId, Grade, Step, StepIncreaseMonth, RaisePercent, RaiseMonth) = (d.PayScaleId, d.Grade, d.Step, d.StepIncreaseMonth, d.RaisePercent, d.RaiseMonth);
        (FirstMonth, LastMonth, LongevityScheduleId, RetirementPlanId, PicksUpEmployeeShare, PayAccountId) = (d.FirstMonth, d.LastMonth, d.LongevityScheduleId, d.RetirementPlanId, d.PicksUpEmployeeShare, d.PayAccountId);
        _funds.AddRange(d.Funds.Select(f => new PositionFundShare(GovernmentId, Id, f.FundId, f.Percent)));
        _coverages.AddRange(d.Coverages.Select(c => new PositionCoverage(GovernmentId, Id, c.PlanId, c.Tier)));
        _extraPay.AddRange(d.ExtraPay.Select(e => new PositionExtraPay(GovernmentId, Id, e.ExtraPayId, e.Value)));
    }
}

/// <summary>The share of a position one fund pays.</summary>
[Audited]
public sealed class PositionFundShare : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public Guid PositionId { get; private set; }
    public Guid FundId { get; private set; }
    public decimal Percent { get; private set; }

    internal PositionFundShare(Guid governmentId, Guid positionId, Guid fundId, decimal percent) =>
        (GovernmentId, PositionId, FundId, Percent) = (governmentId, positionId, fundId, percent);

    private PositionFundShare()
    {
    }
}

/// <summary>An insurance plan the position is covered by, at a tier.</summary>
[Audited]
public sealed class PositionCoverage : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public Guid PositionId { get; private set; }
    public Guid InsurancePlanId { get; private set; }
    public CoverageTier Tier { get; private set; }

    internal PositionCoverage(Guid governmentId, Guid positionId, Guid planId, CoverageTier tier) =>
        (GovernmentId, PositionId, InsurancePlanId, Tier) = (governmentId, positionId, planId, tier);

    private PositionCoverage()
    {
    }
}

/// <summary>An extra pay item for the position, in the item's own unit (dollars, hours, or a percentage).</summary>
[Audited]
public sealed class PositionExtraPay : Entity, ITenantOwned
{
    public Guid GovernmentId { get; private set; }
    public Guid PositionId { get; private set; }
    public Guid ExtraPayId { get; private set; }
    public decimal Value { get; private set; }

    internal PositionExtraPay(Guid governmentId, Guid positionId, Guid extraPayId, decimal value) =>
        (GovernmentId, PositionId, ExtraPayId, Value) = (governmentId, positionId, extraPayId, value);

    private PositionExtraPay()
    {
    }
}
