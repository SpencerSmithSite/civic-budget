using CivicBudget.Application.Erp;
using CivicBudget.Domain.Personnel;

namespace CivicBudget.Application.Personnel;

/// <summary>What the sync does to one position.</summary>
public enum SyncAction
{
    /// <summary>A new employee with no vacancy to fill: a new position.</summary>
    Add = 1,

    /// <summary>A new employee who fills a vacant position with the same title in their department.</summary>
    Fill = 2,

    /// <summary>An employee already budgeted whose pay, plans, or funds changed in the ERP.</summary>
    Update = 3,

    /// <summary>A budgeted employee the ERP no longer lists: the position stays, vacant.</summary>
    Vacate = 4,

    Unchanged = 5,
}

/// <summary>A position as it is in the budget, for matching.</summary>
public sealed record ExistingPosition(Guid Id, Guid DepartmentId, PositionDetails Details);

/// <summary>The department and fund codes an employee can be matched to (active ones only).</summary>
public sealed record EmployeeChart(IReadOnlyDictionary<string, Guid> Departments, IReadOnlyDictionary<string, Guid> Funds);

/// <summary>One step of a sync: the position it touches (null for a new one), what it will say, and what changed, in words.</summary>
public sealed record SyncStep(SyncAction Action, Guid DepartmentId, Guid? PositionId, PositionDetails Details, string Who, IReadOnlyList<string> Changes);

/// <summary>Everything a sync would do, or why it cannot.</summary>
public sealed record SyncPlan(IReadOnlyList<SyncStep> Steps, IReadOnlyList<string> Errors)
{
    public int Count(SyncAction action) => Steps.Count(s => s.Action == action);
}

/// <summary>
/// Matches the ERP's employees to a budget's positions. Pure, like <see cref="ActualsMatcher"/>: the
/// preview and the commit run it on the same inputs and get the same plan.
/// <list type="bullet">
/// <item>An employee already budgeted (same ERP employee number, same department) is updated with
/// what the ERP owns: name, title, hire date, pay, retirement, insurance, and funds. What the budget
/// owns stays: the planned raise or step increase, the months paid, longevity, and other pay.</item>
/// <item>A new employee fills a vacant position with the same title in their department, or becomes
/// a new position that copies the budget's choices from a colleague with the same title.</item>
/// <item>A budgeted employee the ERP no longer lists leaves the position vacant, not removed: the
/// department usually means to fill it, and removing is one click on the personnel page.</item>
/// <item>Anything that cannot be matched (a department or fund code, a retirement system or plan the
/// year's settings do not have) is an error, and the sync is refused until it is fixed, the same
/// rule as the actuals sync: a half-applied roster would misstate every personnel line.</item>
/// </list>
/// </summary>
public static class EmployeeMatcher
{
    public static SyncPlan Plan(ErpEmployees erp, EmployeeChart chart, PayrollRules rules, IReadOnlyList<ExistingPosition> positions)
    {
        var steps = new List<SyncStep>();
        var errors = new List<string>();
        var claimedVacancies = new HashSet<Guid>();
        var kept = new HashSet<Guid>();
        Dictionary<string, ExistingPosition> byEmployee = positions
            .Where(p => p.Details.EmployeeId is not null)
            .GroupBy(p => p.Details.EmployeeId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (ErpEmployee e in erp.Employees.OrderBy(e => e.DepartmentCode).ThenBy(e => e.Title).ThenBy(e => e.Name))
        {
            string who = $"{e.Name}, {e.Title} ({e.EmployeeId})";
            var problems = new List<string>();
            Guid? departmentId = chart.Departments.TryGetValue(e.DepartmentCode, out Guid d) ? d : null;
            if (departmentId is null)
            {
                problems.Add($"no active department has the code {e.DepartmentCode}");
            }

            Terms? terms = ResolveTerms(e, chart, rules, problems);
            if (problems.Count > 0 || terms is null || departmentId is null)
            {
                errors.Add($"{who}: {string.Join("; ", problems)}.");
                continue;
            }

            SyncStep step;
            if (byEmployee.TryGetValue(e.EmployeeId, out ExistingPosition? existing) && existing.DepartmentId == departmentId)
            {
                kept.Add(existing.Id);
                PositionDetails updated = Apply(existing.Details, e, terms, rules, keepMonths: true);
                List<string> changes = Differences(existing.Details, updated, rules, chart);
                step = new SyncStep(changes.Count == 0 ? SyncAction.Unchanged : SyncAction.Update, departmentId.Value, existing.Id, updated, who, changes);
            }
            else
            {
                // A transfer between departments: the old position is left vacant below, and the
                // employee is placed in the new department like any new hire.
                ExistingPosition? vacancy = positions.FirstOrDefault(p => p.DepartmentId == departmentId && p.Details.IsVacant
                    && !claimedVacancies.Contains(p.Id) && string.Equals(p.Details.Title, e.Title, StringComparison.OrdinalIgnoreCase));
                if (vacancy is not null)
                {
                    claimedVacancies.Add(vacancy.Id);
                    step = new SyncStep(SyncAction.Fill, departmentId.Value, vacancy.Id, Apply(vacancy.Details, e, terms, rules, keepMonths: false), who,
                        [$"Fills the vacant {vacancy.Details.Title} position"]);
                }
                else
                {
                    PositionDetails template = positions.FirstOrDefault(p => p.DepartmentId == departmentId
                        && string.Equals(p.Details.Title, e.Title, StringComparison.OrdinalIgnoreCase))?.Details ?? new PositionDetails();
                    step = new SyncStep(SyncAction.Add, departmentId.Value, null, Apply(template, e, terms, rules, keepMonths: false), who,
                        [template.Title.Length > 0 ? $"New position, with the budget's choices copied from another {template.Title}" : "New position"]);
                }
            }

            if (step.Details.Problems(rules) is { Count: > 0 } invalid)
            {
                errors.Add($"{who}: {invalid[0].Message}");
                continue;
            }

            steps.Add(step);
        }

        HashSet<string> listed = erp.Employees.Select(e => e.EmployeeId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (ExistingPosition gone in positions.Where(p => p.Details.EmployeeId is not null && !kept.Contains(p.Id)).OrderBy(p => p.Details.Title))
        {
            PositionDetails vacant = gone.Details with { EmployeeName = null, EmployeeId = null, HireDate = null };
            string reason = listed.Contains(gone.Details.EmployeeId!) ? "moved to another department in the ERP" : "no longer on the ERP's payroll";
            steps.Add(new SyncStep(SyncAction.Vacate, gone.DepartmentId, gone.Id, vacant,
                $"{gone.Details.EmployeeName}, {gone.Details.Title} ({gone.Details.EmployeeId})", [$"Position left vacant: {reason}"]));
        }

        return new SyncPlan(steps, errors);
    }

    /// <summary>What the ERP says about an employee, in the budget's terms (ids from the chart and the year's settings).</summary>
    private sealed record Terms(PayScaleRule? Scale, Guid? RetirementPlanId, IReadOnlyList<FundShare> Funds, IReadOnlyList<Coverage> Coverages);

    private static Terms? ResolveTerms(ErpEmployee e, EmployeeChart chart, PayrollRules rules, List<string> problems)
    {
        PayScaleRule? scale = null;
        if (e.Grade is not null && e.Step is not null)
        {
            scale = rules.PayScales.FirstOrDefault(s => s.Rate(e.Grade, e.Step.Value) is not null);
            if (scale is null)
            {
                problems.Add($"grade {e.Grade} step {e.Step} is not on any pay scale in FY{rules.FiscalYear}'s personnel settings");
            }
        }

        Guid? retirement = null;
        if (e.Retirement is { } system)
        {
            retirement = rules.RetirementPlans.FirstOrDefault(p => string.Equals(p.Name, system, StringComparison.OrdinalIgnoreCase))?.Id;
            if (retirement is null)
            {
                problems.Add($"FY{rules.FiscalYear}'s settings have no retirement system named \"{system}\"");
            }
        }

        var funds = new List<FundShare>();
        foreach (ErpFundShare share in e.Funds)
        {
            if (chart.Funds.TryGetValue(share.FundCode, out Guid fundId))
            {
                funds.Add(new FundShare(fundId, share.Percent));
            }
            else
            {
                problems.Add($"no active fund has the code {share.FundCode}");
            }
        }

        var coverages = new List<Coverage>();
        foreach (ErpBenefit benefit in e.Benefits)
        {
            InsurancePlanRule? plan = rules.InsurancePlans.FirstOrDefault(p => string.Equals(p.Name, benefit.Plan, StringComparison.OrdinalIgnoreCase));
            if (plan is null)
            {
                problems.Add($"FY{rules.FiscalYear}'s settings have no insurance plan named \"{benefit.Plan}\"");
            }
            else
            {
                coverages.Add(new Coverage(plan.Id, benefit.Tier));
            }
        }

        return problems.Count > 0 ? null : new Terms(scale, retirement, funds, coverages);
    }

    /// <summary>The position's details with the ERP's side replaced and the budget's side kept.</summary>
    private static PositionDetails Apply(PositionDetails budget, ErpEmployee e, Terms terms, PayrollRules rules, bool keepMonths)
    {
        bool wasOnScale = budget.PayScaleId is not null;
        PositionDetails details = budget with
        {
            Title = e.Title,
            EmployeeName = e.Name,
            EmployeeId = e.EmployeeId,
            HireDate = e.HireDate,
            Basis = terms.Scale?.Basis ?? e.Basis,
            Rate = terms.Scale is null ? e.Rate : 0m,
            AnnualHours = e.AnnualHours ?? (budget.Title.Length > 0 ? budget.AnnualHours : rules.StandardHours),
            PayScaleId = terms.Scale?.Id,
            Grade = terms.Scale is null ? null : e.Grade,
            Step = terms.Scale is null ? null : e.Step,
            // A planned step increase belongs with a scale, a planned raise with a rate of its own.
            StepIncreaseMonth = terms.Scale is not null && wasOnScale ? budget.StepIncreaseMonth : null,
            RaisePercent = terms.Scale is null && !wasOnScale ? budget.RaisePercent : 0m,
            RaiseMonth = terms.Scale is null && !wasOnScale ? budget.RaiseMonth : 1,
            RetirementPlanId = terms.RetirementPlanId,
            PicksUpEmployeeShare = terms.RetirementPlanId is not null && e.PicksUpEmployeeShare,
            Funds = terms.Funds,
            Coverages = terms.Coverages,
        };

        if (!keepMonths)
        {
            // Someone already on the payroll is paid from the start of the year (or from the hire date,
            // if that falls within it), whatever month the vacancy was planned to be filled in.
            int first = e.HireDate is { } hired && hired > rules.YearStart ? Math.Min(FiscalMonth(rules, hired), 12) : 1;
            details = details with { FirstMonth = first, LastMonth = Math.Max(first, details.LastMonth) };
        }

        return details;
    }

    private static int FiscalMonth(PayrollRules rules, DateOnly date) =>
        ((date.Year - rules.YearStart.Year) * 12) + date.Month - rules.YearStart.Month + 1;

    /// <summary>What the ERP changed, in the words the preview shows.</summary>
    private static List<string> Differences(PositionDetails before, PositionDetails after, PayrollRules rules, EmployeeChart chart)
    {
        var changes = new List<string>();
        void Compare(string what, string was, string now)
        {
            if (was != now)
            {
                changes.Add($"{what}: {was} → {now}");
            }
        }

        Compare("Name", before.EmployeeName ?? "", after.EmployeeName ?? "");
        Compare("Title", before.Title, after.Title);
        Compare("Hire date", Date(before.HireDate), Date(after.HireDate));
        Compare("Pay", PositionText.Pay(before, rules), PositionText.Pay(after, rules));
        Compare("Retirement", Retirement(before, rules), Retirement(after, rules));
        Dictionary<Guid, string> fundCodes = chart.Funds.ToDictionary(f => f.Value, f => f.Key);
        string Funds(PositionDetails d) => string.Join(", ", d.Funds
            .Select(f => $"{fundCodes.GetValueOrDefault(f.FundId, "?")} {PositionCostCalculator.Number(f.Percent)}%")
            .Order(StringComparer.Ordinal));
        Compare("Funds", Funds(before), Funds(after));
        Compare("Insurance", Insurance(before, rules), Insurance(after, rules));
        return changes;
    }

    private static string Date(DateOnly? date) => date?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "none";

    private static string Retirement(PositionDetails d, PayrollRules rules) =>
        d.RetirementPlanId is { } id ? (rules.RetirementPlan(id)?.Name ?? "?") + (d.PicksUpEmployeeShare ? " with pick-up" : "") : "none";

    private static string Insurance(PositionDetails d, PayrollRules rules) =>
        d.Coverages.Count == 0 ? "none" : string.Join(", ", d.Coverages
            .Select(c => $"{rules.InsurancePlan(c.PlanId)?.Name} {PositionCostCalculator.TierName(c.Tier)}")
            .Order(StringComparer.Ordinal));
}
