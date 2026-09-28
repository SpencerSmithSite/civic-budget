using CivicBudget.Domain.Common;
using CivicBudget.Domain.Personnel;

namespace CivicBudget.Application.Reports;

// ---- Position roster ------------------------------------------------------------------------

/// <summary>One position on the roster.</summary>
public sealed record RosterRowDto(
    string Title, string? EmployeeName, string? EmployeeId, DateOnly? HireDate, int? YearsOfService,
    string PayText, string Months, string Funds, decimal Pay, decimal Benefits)
{
    public bool IsVacant => EmployeeName is null;
    public decimal Total => Pay + Benefits;
}

public sealed record RosterDepartmentDto(string DepartmentCode, string DepartmentName, IReadOnlyList<RosterRowDto> Positions)
{
    public int Vacant => Positions.Count(p => p.IsVacant);
    public decimal Pay => Positions.Sum(p => p.Pay);
    public decimal Benefits => Positions.Sum(p => p.Benefits);
    public decimal Total => Pay + Benefits;
}

/// <param name="HasSettings">False when the budget's year has no personnel settings, so nothing can be priced.</param>
public sealed record PositionRosterDto(ReportHeaderDto Header, IReadOnlyList<RosterDepartmentDto> Departments, bool HasSettings)
{
    public int Positions => Departments.Sum(d => d.Positions.Count);
    public int Vacant => Departments.Sum(d => d.Vacant);
    public decimal Pay => Departments.Sum(d => d.Pay);
    public decimal Benefits => Departments.Sum(d => d.Benefits);
    public decimal Total => Pay + Benefits;
}

// ---- Personnel cost by fund -----------------------------------------------------------------

/// <summary>Personnel cost by kind: what the appropriation's salary and benefit lines are made of.</summary>
public sealed record PersonnelCostRowDto(string Label, decimal Pay, decimal Retirement, decimal Medicare, decimal WorkersComp, decimal Insurance)
{
    public decimal Total => Pay + Retirement + Medicare + WorkersComp + Insurance;

    public static PersonnelCostRowDto Sum(string label, IEnumerable<PersonnelCostRowDto> rows)
    {
        List<PersonnelCostRowDto> all = rows.ToList();
        return new(label, all.Sum(r => r.Pay), all.Sum(r => r.Retirement), all.Sum(r => r.Medicare), all.Sum(r => r.WorkersComp), all.Sum(r => r.Insurance));
    }
}

public sealed record PersonnelCostFundDto(string FundCode, string FundName, IReadOnlyList<PersonnelCostRowDto> Departments, PersonnelCostRowDto Subtotal);

public sealed record PersonnelCostDto(ReportHeaderDto Header, IReadOnlyList<PersonnelCostFundDto> Funds, PersonnelCostRowDto Total, bool HasSettings);

// ---- Benefits summary -----------------------------------------------------------------------

public sealed record BenefitTierDto(CoverageTier Tier, int Positions, decimal MonthlyPremium, decimal EmployerCost, decimal EmployeeShare);

public sealed record InsuranceSummaryDto(string Plan, decimal EmployeeSharePercent, IReadOnlyList<BenefitTierDto> Tiers)
{
    public int Positions => Tiers.Sum(t => t.Positions);
    public decimal EmployerCost => Tiers.Sum(t => t.EmployerCost);
    public decimal EmployeeShare => Tiers.Sum(t => t.EmployeeShare);
}

public sealed record RetirementSummaryDto(string System, decimal EmployerRate, int Members, decimal PensionablePay, decimal EmployerShare, decimal PickedUp)
{
    public decimal Total => EmployerShare + PickedUp;
}

public sealed record BenefitsSummaryDto(
    ReportHeaderDto Header,
    int Positions,
    IReadOnlyList<RetirementSummaryDto> Retirement,
    IReadOnlyList<InsuranceSummaryDto> Insurance,
    decimal MedicareRate,
    decimal TaxablePay,
    decimal Medicare,
    decimal WorkersCompRate,
    decimal WorkersComp,
    bool HasSettings)
{
    public decimal Total => Retirement.Sum(r => r.Total) + Insurance.Sum(i => i.EmployerCost) + Medicare + WorkersComp;
}

/// <summary>A priced position with the names the reports print.</summary>
public sealed record ReportPosition(string DepartmentCode, string DepartmentName, PositionDetails Details, PositionCost Cost);

/// <summary>
/// The personnel reports, from positions the service has already priced. Pure, like the other
/// report builders: every figure is a sum of <see cref="PositionCost"/> pieces, the same pieces the
/// budget lines are made of, so the reports agree with the lines to the cent.
/// </summary>
public static class PersonnelReportBuilder
{
    public static IReadOnlyList<RosterDepartmentDto> Roster(IEnumerable<ReportPosition> positions, PayrollRules rules, int fiscalYearStartMonth, IReadOnlyDictionary<Guid, string> fundCodes) =>
        positions
            .GroupBy(p => (p.DepartmentCode, p.DepartmentName))
            .OrderBy(g => g.Key.DepartmentCode)
            .Select(g => new RosterDepartmentDto(g.Key.DepartmentCode, g.Key.DepartmentName, g
                .OrderBy(p => p.Details.IsVacant).ThenByDescending(p => p.Cost.Total)
                .Select(p => new RosterRowDto(
                    p.Details.Title, p.Details.EmployeeName, p.Details.EmployeeId, p.Details.HireDate,
                    p.Details.HireDate is { } hired ? PositionCostCalculator.CompletedYears(hired, rules.YearStart) : null,
                    Personnel.PositionText.Pay(p.Details, rules),
                    Months(fiscalYearStartMonth, p.Details),
                    string.Join(", ", p.Details.Funds.OrderByDescending(f => f.Percent).ThenBy(f => fundCodes.GetValueOrDefault(f.FundId, ""), StringComparer.Ordinal).Select(f => p.Details.Funds.Count == 1 ? fundCodes.GetValueOrDefault(f.FundId, "") : $"{fundCodes.GetValueOrDefault(f.FundId, "")} {PositionCostCalculator.Number(f.Percent)}%")),
                    p.Cost.Pay, p.Cost.Benefits))
                .ToList()))
            .ToList();

    public static (IReadOnlyList<PersonnelCostFundDto> Funds, PersonnelCostRowDto Total) CostByFund(
        IEnumerable<ReportPosition> positions, IReadOnlyDictionary<Guid, (string Code, string Name)> funds)
    {
        var rows = new List<(Guid Fund, string Department, PersonnelCostRowDto Row)>();
        foreach (ReportPosition p in positions)
        {
            foreach (IGrouping<Guid, FundKindCost> fund in p.Cost.ByFundAndKind.GroupBy(c => c.FundId))
            {
                decimal Kind(params CostKind[] kinds) => fund.Where(c => kinds.Contains(c.Kind)).Sum(c => c.Amount);
                rows.Add((fund.Key, $"{p.DepartmentCode} {p.DepartmentName}", new PersonnelCostRowDto("",
                    Kind(CostKind.BasePay, CostKind.Longevity, CostKind.ExtraPay), Kind(CostKind.Retirement), Kind(CostKind.Medicare),
                    Kind(CostKind.WorkersComp), Kind(CostKind.Insurance))));
            }
        }

        List<PersonnelCostFundDto> byFund = rows
            .GroupBy(r => r.Fund)
            .Select(g =>
            {
                (string code, string name) = funds.GetValueOrDefault(g.Key, ("", ""));
                List<PersonnelCostRowDto> departments = g.GroupBy(r => r.Department).OrderBy(d => d.Key)
                    .Select(d => PersonnelCostRowDto.Sum(d.Key, d.Select(r => r.Row))).ToList();
                return new PersonnelCostFundDto(code, name, departments, PersonnelCostRowDto.Sum($"Total, {name}", departments));
            })
            .OrderBy(f => f.FundCode)
            .ToList();
        return (byFund, PersonnelCostRowDto.Sum("All funds", byFund.Select(f => f.Subtotal)));
    }

    public static BenefitsSummaryDto Benefits(ReportHeaderDto header, IReadOnlyList<ReportPosition> positions, PayrollRules rules)
    {
        List<RetirementSummaryDto> retirement = rules.RetirementPlans
            .Select(plan =>
            {
                List<ReportPosition> members = positions.Where(p => p.Details.RetirementPlanId == plan.Id).ToList();
                // The same arithmetic as the calculator: each share is the rate times pensionable pay, rounded per position.
                decimal employer = members.Sum(m => Money.Round(m.Cost.PensionablePay * plan.EmployerRate / 100m));
                decimal pickedUp = members.Where(m => m.Details.PicksUpEmployeeShare).Sum(m => Money.Round(m.Cost.PensionablePay * plan.EmployeeRate / 100m));
                return new RetirementSummaryDto(plan.Name, plan.EmployerRate, members.Count, members.Sum(m => m.Cost.PensionablePay), employer, pickedUp);
            })
            .Where(r => r.Members > 0)
            .ToList();

        List<InsuranceSummaryDto> insurance = rules.InsurancePlans
            .Select(plan => new InsuranceSummaryDto(plan.Name, plan.EmployeeSharePercent, plan.OfferedTiers
                .Select(tier =>
                {
                    List<ReportPosition> covered = positions.Where(p => p.Details.Coverages.Any(c => c.PlanId == plan.Id && c.Tier == tier)).ToList();
                    decimal premium = plan.Premium(tier)!.Value;
                    decimal gross = covered.Sum(p => Money.Round(premium * p.Details.MonthsPaid));
                    decimal employer = covered.Sum(p => Money.Round(premium * (100m - plan.EmployeeSharePercent) / 100m * p.Details.MonthsPaid));
                    return new BenefitTierDto(tier, covered.Count, premium, employer, gross - employer);
                })
                .ToList()))
            .Where(i => i.Positions > 0)
            .ToList();

        return new BenefitsSummaryDto(header, positions.Count, retirement, insurance,
            rules.MedicareRate, positions.Sum(p => p.Cost.TaxablePay), positions.Sum(p => p.Cost.Parts.Where(c => c.Kind == CostKind.Medicare).Sum(c => c.Amount)),
            rules.WorkersCompRate, positions.Sum(p => p.Cost.Parts.Where(c => c.Kind == CostKind.WorkersComp).Sum(c => c.Amount)),
            HasSettings: true);
    }

    private static string Months(int startMonth, PositionDetails d)
    {
        if (d.FirstMonth == 1 && d.LastMonth == 12)
        {
            return "All year";
        }

        var us = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        string Name(int m) => us.DateTimeFormat.GetAbbreviatedMonthName(((startMonth - 1 + m - 1) % 12) + 1);
        return $"{Name(d.FirstMonth)}–{Name(d.LastMonth)}";
    }
}
