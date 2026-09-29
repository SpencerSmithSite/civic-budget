using CivicBudget.Application.Assistant;
using CivicBudget.Application.Budgets;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Tests.Assistant;

/// <summary>
/// "Check my budget": what stops a budget going forward is a must-fix, what council will ask about
/// is worth a look, and a clean budget says nothing.
/// </summary>
public class BudgetReviewTests
{
    private static readonly Guid VersionId = Guid.NewGuid();

    [Fact]
    public void A_clean_budget_has_no_findings()
    {
        Assert.Empty(BudgetReview.Check(Workspace(), plan: null, certificate: null));
    }

    [Fact]
    public void A_fund_over_the_limit_is_a_must_fix_only_when_it_blocks_the_workflow()
    {
        IReadOnlyList<ReviewFinding> blocking = BudgetReview.Check(Workspace(funds: [Fund("2011", "Street", AppropriationLimitSeverity.Error, 21_908.65m)]), null, null);
        IReadOnlyList<ReviewFinding> warning = BudgetReview.Check(Workspace(funds: [Fund("2011", "Street", AppropriationLimitSeverity.Warning, 500m)]), null, null);

        ReviewFinding finding = Assert.Single(blocking);
        Assert.True(finding.MustFix);
        Assert.Contains("$21,908.65", finding.Finding, StringComparison.Ordinal);
        Assert.False(Assert.Single(warning).MustFix);
    }

    [Fact]
    public void Departments_not_submitted_are_listed_while_the_budget_is_a_draft()
    {
        BudgetWorkspaceDto workspace = Workspace(requests: [Request("110", "Police", DepartmentRequestStatus.Returned), Request("210", "Fire", DepartmentRequestStatus.Submitted)]);

        ReviewFinding finding = Assert.Single(BudgetReview.Check(workspace, null, null));

        Assert.Contains("110 Police (returned)", finding.Finding, StringComparison.Ordinal);
        Assert.DoesNotContain("Fire", finding.Finding, StringComparison.Ordinal);
        Assert.Empty(BudgetReview.Check(workspace with { Version = workspace.Version with { Status = BudgetStatus.Proposed } }, null, null));
    }

    [Fact]
    public void A_large_change_without_a_justification_is_worth_a_look_and_a_small_one_is_not()
    {
        BudgetWorkspaceDto workspace = Workspace(lines:
        [
            Line("Overtime", current: 20_000m, amount: 26_000m),                      // +30%, +$6,000: flagged
            Line("Fuel", current: 20_000m, amount: 26_000m, justification: "Prices"),  // explained
            Line("Postage", current: 1_000m, amount: 1_500m),                          // +50% but $500
            Line("Salaries", current: 900_000m, amount: 950_000m),                     // +$50,000 but 5.6%
        ]);

        ReviewFinding finding = Assert.Single(BudgetReview.Check(workspace, null, null));

        Assert.False(finding.MustFix);
        Assert.StartsWith("1 line changes", finding.Finding, StringComparison.Ordinal);
        Assert.Contains("Overtime +$6,000.00", finding.Finding, StringComparison.Ordinal);
    }

    [Fact]
    public void A_later_plan_year_over_the_limit_is_named_but_the_budget_year_is_left_to_the_fund_check()
    {
        var plan = new BudgetPlanDto(VersionId, 2027, "Original", BudgetStatus.Draft, 3, true, [], true,
            [new PlanFundDto(Guid.NewGuid(), "1000", "General", [PlanYear(2027, over: true), PlanYear(2028, over: false), PlanYear(2029, over: true)])], []);

        ReviewFinding finding = Assert.Single(BudgetReview.Check(Workspace(), plan, null));

        Assert.EndsWith("overspends in FY2029.", finding.Finding, StringComparison.Ordinal);
    }

    [Fact]
    public void Expenditure_lines_zeroed_from_this_years_budget_are_counted()
    {
        BudgetWorkspaceDto workspace = Workspace(lines: [Line("Uniforms", current: 800m, amount: 0m, justification: "Moved to Fire")]);

        ReviewFinding finding = Assert.Single(BudgetReview.Check(workspace, null, null));

        Assert.StartsWith("1 expenditure line is budgeted at zero", finding.Finding, StringComparison.Ordinal);
    }

    private static BudgetWorkspaceDto Workspace(IReadOnlyList<BudgetLineDto>? lines = null, IReadOnlyList<FundBalanceDto>? funds = null, IReadOnlyList<DepartmentRequestDto>? requests = null) =>
        new(new BudgetVersionSummaryDto(VersionId, 2027, 1, "Original", BudgetStatus.Draft, null, null, 0), AccountNumberFormat.UanVillage, true, true,
            lines ?? [], funds ?? [], [], [], [], requests ?? []);

    private static BudgetLineDto Line(string name, decimal current, decimal amount, string? justification = null) =>
        new(Guid.NewGuid(), Guid.NewGuid(), "1000", "General", Guid.NewGuid(), "110", "Police", Guid.NewGuid(), "5000", name, "1000-110-5000",
            AccountType.Expenditure, ReportingCategory.ContractualServices, amount, 0m, current, justification, CanEdit: true);

    private static FundBalanceDto Fund(string code, string name, AppropriationLimitSeverity severity, decimal over)
    {
        var id = Guid.NewGuid();
        return new FundBalanceDto(id, code, name, FundCategory.SpecialRevenue, new FundBalanceSummary(id, 0m, 100m, 0m, 100m + over, 0m),
            new AppropriationLimitResult(id, severity, over), true);
    }

    private static DepartmentRequestDto Request(string code, string name, DepartmentRequestStatus status) =>
        new(Guid.NewGuid(), code, name, status, "A narrative.", null, null, null, null, 3, 0m, 0m, 0m, true, true, false, true);

    private static PlanFundYearDto PlanYear(int year, bool over) => new(year, 0m, 100m, 0m, over ? 150m : 90m, 0m, over ? -50m : 10m, over, over ? 50m : 0m);
}
