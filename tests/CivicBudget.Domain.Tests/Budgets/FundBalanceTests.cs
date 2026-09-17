using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Governments;

namespace CivicBudget.Domain.Tests.Budgets;

public class FundBalanceTests
{
    private static readonly Guid General = Guid.CreateVersion7();
    private static readonly Guid Street = Guid.CreateVersion7();

    private static readonly LineAmount[] Lines =
    [
        new(General, AccountType.Revenue, 900_000m),
        new(General, AccountType.Revenue, 100_000m),
        new(General, AccountType.TransferIn, 25_000m),
        new(General, AccountType.Expenditure, 700_000m),
        new(General, AccountType.Expenditure, 200_000m),
        new(General, AccountType.TransferOut, 75_000m),
        new(Street, AccountType.Revenue, 300_000m),      // must not leak into General's totals
        new(Street, AccountType.Expenditure, 350_000m),
    ];

    [Fact]
    public void Sums_each_account_type_for_the_requested_fund_only()
    {
        FundBalanceSummary summary = FundBalanceCalculator.Calculate(General, beginningBalance: 150_000m, Lines);

        Assert.Equal(150_000m, summary.BeginningBalance);
        Assert.Equal(1_000_000m, summary.Revenues);
        Assert.Equal(25_000m, summary.TransfersIn);
        Assert.Equal(900_000m, summary.Expenditures);
        Assert.Equal(75_000m, summary.TransfersOut);
    }

    [Fact]
    public void Derives_resources_appropriations_and_ending_balance()
    {
        FundBalanceSummary summary = FundBalanceCalculator.Calculate(General, 150_000m, Lines);

        Assert.Equal(1_175_000m, summary.EstimatedResources);     // 150k + 1,000k + 25k
        Assert.Equal(975_000m, summary.Appropriations);           // 900k + 75k
        Assert.Equal(200_000m, summary.ProjectedEndingBalance);
        Assert.True(summary.IsWithinAppropriationLimit);
        Assert.Equal(0m, summary.AmountOverLimit);
    }

    [Fact]
    public void Flags_a_fund_whose_appropriations_exceed_resources()
    {
        FundBalanceSummary summary = FundBalanceCalculator.Calculate(Street, beginningBalance: 20_000m, Lines);

        Assert.Equal(320_000m, summary.EstimatedResources);
        Assert.Equal(350_000m, summary.Appropriations);
        Assert.Equal(-30_000m, summary.ProjectedEndingBalance);
        Assert.False(summary.IsWithinAppropriationLimit);
        Assert.Equal(30_000m, summary.AmountOverLimit);
    }

    [Fact]
    public void Appropriations_equal_to_resources_are_within_the_limit()
    {
        // "May not exceed" means equal is allowed. Spending down to a zero balance is legal, if unwise.
        FundBalanceSummary summary = FundBalanceCalculator.Calculate(
            General, 0m, [new(General, AccountType.Revenue, 100m), new(General, AccountType.Expenditure, 100m)]);

        Assert.True(summary.IsWithinAppropriationLimit);
        Assert.Equal(0m, summary.ProjectedEndingBalance);
    }

    [Fact]
    public void Fund_with_only_a_beginning_balance_has_it_as_its_resources()
    {
        FundBalanceSummary summary = FundBalanceCalculator.Calculate(Guid.CreateVersion7(), 5_000m, Lines);

        Assert.Equal(5_000m, summary.EstimatedResources);
        Assert.Equal(0m, summary.Appropriations);
    }

    [Fact]
    public void CalculateAll_covers_every_fund_in_the_version()
    {
        BudgetVersion version = TestData.DraftVersion();
        var general = TestData.GeneralFund();
        var street = TestData.StreetFund();
        version.AddLine(general, null, TestData.PropertyTax(), 500m);
        version.AddLine(general, TestData.Police(), TestData.Salaries(), 400m);
        version.AddLine(street, TestData.Streets(), TestData.Supplies(), 100m);
        version.SetBeginningBalance(general, 50m);

        IReadOnlyList<FundBalanceSummary> all = FundBalanceCalculator.CalculateAll(version);

        Assert.Equal(2, all.Count);
        FundBalanceSummary g = all.Single(s => s.FundId == general.Id);
        Assert.Equal(550m, g.EstimatedResources);
        Assert.Equal(400m, g.Appropriations);
        FundBalanceSummary s = all.Single(x => x.FundId == street.Id);
        Assert.Equal(0m, s.EstimatedResources);
        Assert.Equal(100m, s.Appropriations);
        Assert.False(s.IsWithinAppropriationLimit);
    }

    [Theory]
    [InlineData(AppropriationLimitMode.Warn, AppropriationLimitSeverity.Warning, false)]
    [InlineData(AppropriationLimitMode.Block, AppropriationLimitSeverity.Error, true)]
    public void Over_limit_severity_follows_the_governments_mode(AppropriationLimitMode mode, AppropriationLimitSeverity expected, bool blocks)
    {
        FundBalanceSummary over = FundBalanceCalculator.Calculate(Street, 20_000m, Lines);

        AppropriationLimitResult result = AppropriationLimitCheck.Evaluate(over, mode);

        Assert.Equal(expected, result.Severity);
        Assert.Equal(blocks, result.BlocksWorkflow);
        Assert.Equal(30_000m, result.AmountOverLimit);
    }

    [Theory]
    [InlineData(AppropriationLimitMode.Warn)]
    [InlineData(AppropriationLimitMode.Block)]
    public void Within_limit_is_ok_in_either_mode(AppropriationLimitMode mode)
    {
        FundBalanceSummary within = FundBalanceCalculator.Calculate(General, 150_000m, Lines);

        AppropriationLimitResult result = AppropriationLimitCheck.Evaluate(within, mode);

        Assert.Equal(AppropriationLimitSeverity.Ok, result.Severity);
        Assert.False(result.BlocksWorkflow);
    }
}
