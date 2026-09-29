using CivicBudget.Application.Reports;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Personnel;

namespace CivicBudget.Application.Tests.Personnel;

/// <summary>The personnel reports add up the same cost pieces the budget lines are made of.</summary>
public class PersonnelReportBuilderTests
{
    private static readonly Guid General = Guid.CreateVersion7();
    private static readonly Guid Street = Guid.CreateVersion7();
    private static readonly Guid Account = Guid.CreateVersion7();
    private static readonly ReportHeaderDto Header = new(Guid.CreateVersion7(), "Village of Maple Ridge", 2027, "Original", BudgetStatus.Draft, null, "Dana", DateTimeOffset.UtcNow);

    private readonly PayrollRules _rules;

    public PersonnelReportBuilderTests()
    {
        var settings = PersonnelSettings.CreateDefault(Guid.CreateVersion7(), 2027, Account, null, Account, Account, Account);
        settings.SetBasics(2080m, Account, 1.45m, Account, 2m, Account);
        settings.SaveInsurancePlan(null, "Medical", 1_000m, 1_500m, 2_000m, 10m, Account);
        _rules = settings.ToRules(new DateOnly(2027, 1, 1), new DateOnly(2027, 12, 31), new Dictionary<Guid, string> { [General] = "1000", [Street] = "2011" });
    }

    private ReportPosition Position(string department, string? name, decimal salary, CoverageTier? tier = null, bool pickUp = false, params FundShare[] funds)
    {
        PositionDetails details = new()
        {
            Title = "Worker",
            EmployeeName = name,
            Rate = salary,
            RetirementPlanId = _rules.RetirementPlans.Single(p => p.Name == "OPERS").Id,
            PicksUpEmployeeShare = pickUp,
            Funds = funds.Length == 0 ? [new FundShare(General, 100m)] : funds,
            Coverages = tier is { } t ? [new Coverage(_rules.InsurancePlans[0].Id, t)] : [],
        };
        return new ReportPosition(department, department == "110" ? "Police" : "Streets", details, PositionCostCalculator.Calculate(details, _rules));
    }

    [Fact]
    public void The_roster_groups_by_department_with_filled_positions_first()
    {
        IReadOnlyList<RosterDepartmentDto> roster = PersonnelReportBuilder.Roster(
            [Position("620", "Chris", 50_000m), Position("110", null, 60_000m), Position("110", "Riley", 55_000m)],
            _rules, 1, new Dictionary<Guid, string> { [General] = "1000" });

        Assert.Equal(["110", "620"], roster.Select(d => d.DepartmentCode));
        Assert.Equal(["Riley", null], roster[0].Positions.Select(p => p.EmployeeName));
        Assert.Equal(1, roster[0].Vacant);
        Assert.Equal("$55,000.00 a year", roster[0].Positions[0].PayText);
        Assert.Equal("1000", roster[0].Positions[0].Funds);
    }

    [Fact]
    public void Cost_by_fund_splits_a_shared_position_and_totals_match_the_positions()
    {
        ReportPosition shared = Position("620", "Chris", 40_000m, null, false, new FundShare(General, 25m), new FundShare(Street, 75m));
        ReportPosition police = Position("110", "Riley", 60_000m);

        (IReadOnlyList<PersonnelCostFundDto> funds, PersonnelCostRowDto total) = PersonnelReportBuilder.CostByFund(
            [shared, police], new Dictionary<Guid, (string, string)> { [General] = ("1000", "General Fund"), [Street] = ("2011", "Street") });

        PersonnelCostFundDto street = funds.Single(f => f.FundCode == "2011");
        Assert.Equal(30_000m, street.Subtotal.Pay);                      // 75% of 40,000
        Assert.Equal(4_200m, street.Subtotal.Retirement);                // 14% of it
        Assert.Equal(["110 Police", "620 Streets"], funds.Single(f => f.FundCode == "1000").Departments.Select(d => d.Label));
        Assert.Equal(shared.Cost.Total + police.Cost.Total, total.Total);
    }

    [Fact]
    public void The_benefits_summary_counts_members_and_splits_premiums_between_employer_and_employee()
    {
        IReadOnlyList<ReportPosition> positions =
        [
            Position("110", "Riley", 60_000m, CoverageTier.Family, pickUp: true),
            Position("110", "Quinn", 50_000m, CoverageTier.Family),
            Position("110", null, 40_000m, CoverageTier.EmployeeOnly),
        ];

        BenefitsSummaryDto summary = PersonnelReportBuilder.Benefits(Header, positions, _rules);

        RetirementSummaryDto opers = summary.Retirement.Single();
        Assert.Equal((3, 150_000m, 21_000m, 6_000m), (opers.Members, opers.PensionablePay, opers.EmployerShare, opers.PickedUp));
        BenefitTierDto family = summary.Insurance.Single().Tiers.Single(t => t.Tier == CoverageTier.Family);
        Assert.Equal((2, 43_200m, 4_800m), (family.Positions, family.EmployerCost, family.EmployeeShare)); // 2 × 2,000 × 12, 90/10
        Assert.Equal(positions.Sum(p => p.Cost.Benefits), summary.Total);
    }
}
