using CivicBudget.Domain.Personnel;

namespace CivicBudget.Web.Components.Admin.Personnel;

/// <summary>
/// The position editor's fields, shaped for binding: a vacancy checkbox instead of a null name, a
/// pay scale switch, one coverage choice per insurance plan and one value per extra pay item. It
/// converts to the <see cref="PositionDetails"/> the service and the calculator take.
/// </summary>
public sealed class PositionForm
{
    public string Title { get; set; } = "";
    public string? EmployeeName { get; set; }

    /// <summary>The ERP's employee number, kept as it came so next year's sync finds the position; not edited here.</summary>
    public string? EmployeeId { get; set; }
    public bool IsVacant { get; set; }
    public DateOnly? HireDate { get; set; }
    public bool OnScale { get; set; }
    public PayBasis Basis { get; set; } = PayBasis.Salary;
    public decimal? Rate { get; set; }
    public decimal AnnualHours { get; set; } = PersonnelSettings.DefaultStandardHours;
    public Guid? PayScaleId { get; set; }
    public string? Grade { get; set; }
    public int? Step { get; set; }
    public int? StepIncreaseMonth { get; set; }
    public decimal RaisePercent { get; set; }
    public int RaiseMonth { get; set; } = 1;
    public int FirstMonth { get; set; } = 1;
    public int LastMonth { get; set; } = 12;
    public Guid? LongevityScheduleId { get; set; }
    public Guid? RetirementPlanId { get; set; }
    public bool PicksUpEmployeeShare { get; set; }
    public Guid? PayAccountId { get; set; }
    public Dictionary<Guid, CoverageTier?> Coverage { get; } = [];
    public Dictionary<Guid, decimal?> ExtraPay { get; } = [];
    public List<FundRow> Funds { get; } = [];

    public sealed class FundRow
    {
        public Guid FundId { get; set; }
        public decimal Percent { get; set; }
    }

    public decimal FundTotal => Funds.Sum(f => f.Percent);

    /// <summary>A new position: full time, in the first retirement plan, paid from one fund.</summary>
    public static PositionForm New(PayrollRules rules, Guid? fundId)
    {
        var form = new PositionForm { AnnualHours = rules.StandardHours, RetirementPlanId = rules.RetirementPlans.Count > 0 ? rules.RetirementPlans[0].Id : null };
        if (fundId is { } fund)
        {
            form.Funds.Add(new FundRow { FundId = fund, Percent = 100m });
        }

        return form;
    }

    public static PositionForm From(PositionDetails d)
    {
        var form = new PositionForm
        {
            Title = d.Title,
            EmployeeName = d.EmployeeName,
            EmployeeId = d.EmployeeId,
            IsVacant = d.IsVacant,
            HireDate = d.HireDate,
            OnScale = d.PayScaleId is not null,
            Basis = d.Basis,
            Rate = d.PayScaleId is null ? d.Rate : null,
            AnnualHours = Application.Personnel.Decimals.Trim(d.AnnualHours),
            PayScaleId = d.PayScaleId,
            Grade = d.Grade,
            Step = d.Step,
            StepIncreaseMonth = d.StepIncreaseMonth,
            RaisePercent = Application.Personnel.Decimals.Trim(d.RaisePercent),
            RaiseMonth = d.RaiseMonth,
            FirstMonth = d.FirstMonth,
            LastMonth = d.LastMonth,
            LongevityScheduleId = d.LongevityScheduleId,
            RetirementPlanId = d.RetirementPlanId,
            PicksUpEmployeeShare = d.PicksUpEmployeeShare,
            PayAccountId = d.PayAccountId,
        };
        foreach (Domain.Personnel.Coverage c in d.Coverages)
        {
            form.Coverage[c.PlanId] = c.Tier;
        }

        foreach (ExtraPayAmount e in d.ExtraPay)
        {
            form.ExtraPay[e.ExtraPayId] = Application.Personnel.Decimals.Trim(e.Value);
        }

        form.Funds.AddRange(d.Funds.Select(f => new FundRow { FundId = f.FundId, Percent = Application.Personnel.Decimals.Trim(f.Percent) }));
        return form;
    }

    public PositionDetails ToDetails() => new()
    {
        Title = Title.Trim(),
        EmployeeName = IsVacant ? null : EmployeeName?.Trim() ?? "",
        EmployeeId = IsVacant ? null : EmployeeId,
        HireDate = HireDate,
        Basis = Basis,
        Rate = OnScale ? 0m : Rate ?? 0m,
        AnnualHours = AnnualHours,
        PayScaleId = OnScale ? PayScaleId : null,
        Grade = OnScale ? Grade : null,
        Step = OnScale ? Step : null,
        StepIncreaseMonth = OnScale ? StepIncreaseMonth : null,
        RaisePercent = OnScale ? 0m : RaisePercent,
        RaiseMonth = RaiseMonth,
        FirstMonth = FirstMonth,
        LastMonth = LastMonth,
        LongevityScheduleId = LongevityScheduleId,
        RetirementPlanId = RetirementPlanId,
        PicksUpEmployeeShare = RetirementPlanId is not null && PicksUpEmployeeShare,
        PayAccountId = PayAccountId,
        Funds = Funds.Select(f => new FundShare(f.FundId, f.Percent)).ToList(),
        Coverages = Coverage.Where(c => c.Value is not null).Select(c => new Domain.Personnel.Coverage(c.Key, c.Value!.Value)).ToList(),
        ExtraPay = ExtraPay.Where(e => e.Value is > 0m).Select(e => new ExtraPayAmount(e.Key, e.Value!.Value)).ToList(),
    };
}
