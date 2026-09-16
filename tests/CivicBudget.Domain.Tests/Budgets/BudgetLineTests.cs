using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Domain.Tests.Budgets;

public class BudgetLineTests
{
    [Fact]
    public void Expenditure_lines_require_a_department()
    {
        BudgetVersion version = TestData.DraftVersion();
        Assert.Throws<DomainException>(() => version.AddLine(TestData.GeneralFund(), department: null, TestData.Salaries(), 1m));
    }

    [Fact]
    public void Revenue_lines_may_omit_the_department()
    {
        BudgetVersion version = TestData.DraftVersion();
        BudgetLine line = version.AddLine(TestData.GeneralFund(), department: null, TestData.PropertyTax(), 250_000m);
        Assert.Null(line.DepartmentId);
    }

    [Fact]
    public void Revenue_lines_may_carry_a_department()
    {
        // A government may attribute revenue to a department (water sales → Water Utility).
        BudgetVersion version = TestData.DraftVersion();
        var utility = TestData.Streets();
        BudgetLine line = version.AddLine(TestData.GeneralFund(), utility, TestData.PropertyTax(), 250_000m);
        Assert.Equal(utility.Id, line.DepartmentId);
    }

    [Fact]
    public void Same_fund_department_account_cannot_appear_twice()
    {
        BudgetVersion version = TestData.DraftVersion();
        var fund = TestData.GeneralFund();
        var police = TestData.Police();
        var salaries = TestData.Salaries();
        version.AddLine(fund, police, salaries, 1m);

        Assert.Throws<DomainException>(() => version.AddLine(fund, police, salaries, 2m));
    }

    [Fact]
    public void Same_account_in_a_different_department_is_a_different_line()
    {
        BudgetVersion version = TestData.DraftVersion();
        var fund = TestData.GeneralFund();
        var salaries = TestData.Salaries();
        version.AddLine(fund, TestData.Police(), salaries, 1m);
        version.AddLine(fund, TestData.Streets(), salaries, 2m);

        Assert.Equal(2, version.Lines.Count);
    }

    [Fact]
    public void Fund_without_department_and_fund_with_department_are_distinct_revenue_lines()
    {
        BudgetVersion version = TestData.DraftVersion();
        var fund = TestData.GeneralFund();
        var tax = TestData.PropertyTax();
        version.AddLine(fund, null, tax, 1m);
        version.AddLine(fund, TestData.Police(), tax, 2m);

        Assert.Equal(2, version.Lines.Count);
    }

    [Fact]
    public void Rejects_entities_from_another_government()
    {
        BudgetVersion version = TestData.DraftVersion();
        Guid other = TestData.OtherGovernmentId;

        Assert.Throws<DomainException>(() => version.AddLine(TestData.GeneralFund(other), TestData.Police(), TestData.Salaries(), 1m));
        Assert.Throws<DomainException>(() => version.AddLine(TestData.GeneralFund(), TestData.Police(other), TestData.Salaries(), 1m));
        Assert.Throws<DomainException>(() => version.AddLine(TestData.GeneralFund(), TestData.Police(), TestData.Salaries(other), 1m));
        Assert.Throws<DomainException>(() => version.SetBeginningBalance(TestData.GeneralFund(other), 1m));
    }

    [Fact]
    public void Rejects_inactive_fund_department_or_account()
    {
        BudgetVersion version = TestData.DraftVersion();
        Fund inactiveFund = TestData.GeneralFund();
        inactiveFund.Deactivate();
        var inactiveDept = TestData.Police();
        inactiveDept.Deactivate();
        var inactiveAccount = TestData.Salaries();
        inactiveAccount.Deactivate();

        Assert.Throws<DomainException>(() => version.AddLine(inactiveFund, TestData.Police(), TestData.Salaries(), 1m));
        Assert.Throws<DomainException>(() => version.AddLine(TestData.GeneralFund(), inactiveDept, TestData.Salaries(), 1m));
        Assert.Throws<DomainException>(() => version.AddLine(TestData.GeneralFund(), TestData.Police(), inactiveAccount, 1m));
    }

    [Fact]
    public void Amounts_are_stored_to_the_cent()
    {
        BudgetVersion version = TestData.DraftVersion();
        BudgetLine line = version.AddLine(TestData.GeneralFund(), TestData.Police(), TestData.Salaries(), 100.005m, 99.994m, 98.125m);

        Assert.Equal(100.01m, line.Amount);
        Assert.Equal(99.99m, line.PriorYearActual);
        Assert.Equal(98.13m, line.CurrentYearBudget);

        version.UpdateLineAmount(line.Id, 1234.565m);
        Assert.Equal(1234.57m, line.Amount);
    }

    [Fact]
    public void Dollar_and_percent_change_compare_proposed_to_current_budget()
    {
        BudgetVersion version = TestData.DraftVersion();
        BudgetLine line = version.AddLine(TestData.GeneralFund(), TestData.Police(), TestData.Salaries(), 110_000m, 95_000m, 100_000m);

        Assert.Equal(10_000m, line.DollarChange);
        Assert.Equal(10.0m, line.PercentChange);
    }

    [Fact]
    public void Percent_change_is_null_for_a_new_line_item()
    {
        BudgetVersion version = TestData.DraftVersion();
        BudgetLine line = version.AddLine(TestData.GeneralFund(), TestData.Police(), TestData.Salaries(), 5_000m, 0m, 0m);

        Assert.Equal(5_000m, line.DollarChange);
        Assert.Null(line.PercentChange);
    }

    [Fact]
    public void Justification_is_trimmed_and_blank_becomes_null()
    {
        BudgetVersion version = TestData.DraftVersion();
        BudgetLine line = version.AddLine(TestData.GeneralFund(), TestData.Police(), TestData.Salaries(), 1m, justification: "  Two officers  ");
        Assert.Equal("Two officers", line.Justification);

        version.UpdateLineJustification(line.Id, "   ");
        Assert.Null(line.Justification);
    }

    [Fact]
    public void Removing_a_line_that_is_not_in_the_version_fails()
    {
        BudgetVersion version = TestData.DraftVersion();
        Assert.Throws<DomainException>(() => version.RemoveLine(Guid.CreateVersion7()));
    }

    [Fact]
    public void Beginning_balance_is_upserted_per_fund()
    {
        BudgetVersion version = TestData.DraftVersion();
        var fund = TestData.GeneralFund();

        version.SetBeginningBalance(fund, 10_000m);
        version.SetBeginningBalance(fund, 12_500.555m);

        Assert.Single(version.BeginningBalances);
        Assert.Equal(12_500.56m, version.GetBeginningBalance(fund.Id));
        Assert.Equal(0m, version.GetBeginningBalance(Guid.CreateVersion7()));
    }
}
