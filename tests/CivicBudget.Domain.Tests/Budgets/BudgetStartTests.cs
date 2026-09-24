using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.Departments;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Domain.Tests.Budgets;

/// <summary>Starting a year's budget from the prior year's adopted one, as is or changed by a percentage.</summary>
public class BudgetStartTests
{
    private static readonly Guid Government = TestData.GovernmentId;
    private static readonly FiscalYear NextYear = new(Government, 2028, 1);

    private sealed record Prior(BudgetVersion Version, Fund General, Department Police, Account Salaries, Account Tax, Dictionary<Guid, Fund> Funds);

    private static Prior AdoptedPrior(decimal salaries = 100_000m, decimal tax = 150_000m)
    {
        Fund general = TestData.GeneralFund();
        Department police = TestData.Police();
        Account salariesAccount = TestData.Salaries();
        Account taxAccount = TestData.PropertyTax();
        BudgetVersion version = TestData.DraftVersion();
        version.AddLine(general, police, salariesAccount, salaries, 90_000m, 95_000m, "Two officers");
        version.AddLine(general, null, taxAccount, tax, 140_000m, 145_000m);
        version.SetBeginningBalance(general, 20_000m);
        version.Propose();
        version.Adopt("2027-40", "user-fd", DateTimeOffset.UnixEpoch);
        return new Prior(version, general, police, salariesAccount, taxAccount, new() { [general.Id] = general });
    }

    [Fact]
    public void Copying_as_is_makes_last_years_adopted_amounts_the_comparative_and_the_starting_request()
    {
        Prior prior = AdoptedPrior();

        (BudgetVersion version, IReadOnlyList<string> skipped) = BudgetVersion.CreateOriginalFrom(prior.Version, NextYear.Id, prior.Funds, BudgetSeedOptions.CopyAsIs);

        Assert.Empty(skipped);
        Assert.Equal((BudgetStatus.Draft, 1, NextYear.Id), (version.Status, version.VersionNumber, version.FiscalYearId));
        BudgetLine salaries = version.Lines.Single(l => l.AccountId == prior.Salaries.Id);
        Assert.Equal((100_000m, 100_000m, 0m), (salaries.Amount, salaries.CurrentYearBudget, salaries.PriorYearActual));
        Assert.Null(salaries.Justification); // last year's reasons are not this year's
    }

    [Fact]
    public void A_percentage_change_raises_the_request_but_not_the_comparative()
    {
        Prior prior = AdoptedPrior();

        (BudgetVersion version, _) = BudgetVersion.CreateOriginalFrom(prior.Version, NextYear.Id, prior.Funds, new BudgetSeedOptions(3m, SeedAdjustmentScope.AllLines, RoundToWholeDollars: false));

        BudgetLine salaries = version.Lines.Single(l => l.AccountId == prior.Salaries.Id);
        Assert.Equal(103_000m, salaries.Amount);           // Spencer's example: $100,000 + 3%
        Assert.Equal(100_000m, salaries.CurrentYearBudget);
        Assert.Equal(154_500m, version.Lines.Single(l => l.AccountId == prior.Tax.Id).Amount);
    }

    [Theory]
    [InlineData(SeedAdjustmentScope.AppropriationsOnly, 103_000, 150_000)]
    [InlineData(SeedAdjustmentScope.RevenueEstimatesOnly, 100_000, 154_500)]
    public void The_change_can_apply_to_appropriations_or_revenue_estimates_only(SeedAdjustmentScope scope, double expectedSalaries, double expectedTax)
    {
        Prior prior = AdoptedPrior();

        (BudgetVersion version, _) = BudgetVersion.CreateOriginalFrom(prior.Version, NextYear.Id, prior.Funds, new BudgetSeedOptions(3m, scope, RoundToWholeDollars: false));

        Assert.Equal((decimal)expectedSalaries, version.Lines.Single(l => l.AccountId == prior.Salaries.Id).Amount);
        Assert.Equal((decimal)expectedTax, version.Lines.Single(l => l.AccountId == prior.Tax.Id).Amount);
    }

    [Theory]
    [InlineData(false, 450_193.43)]
    [InlineData(true, 450_193)]
    public void Amounts_can_be_rounded_to_whole_dollars(bool round, double expected)
    {
        Prior prior = AdoptedPrior(salaries: 437_081m);

        (BudgetVersion version, _) = BudgetVersion.CreateOriginalFrom(prior.Version, NextYear.Id, prior.Funds, new BudgetSeedOptions(3m, SeedAdjustmentScope.AllLines, round));

        Assert.Equal((decimal)expected, version.Lines.Single(l => l.AccountId == prior.Salaries.Id).Amount);
    }

    [Fact]
    public void Each_funds_beginning_balance_is_last_years_projected_ending_balance()
    {
        Prior prior = AdoptedPrior();

        (BudgetVersion version, _) = BudgetVersion.CreateOriginalFrom(prior.Version, NextYear.Id, prior.Funds, BudgetSeedOptions.CopyAsIs);

        Assert.Equal(70_000m, version.GetBeginningBalance(prior.General.Id)); // 20,000 + 150,000 revenue - 100,000 appropriated
    }

    [Fact]
    public void Lines_on_a_retired_account_are_left_out_and_listed()
    {
        Prior prior = AdoptedPrior();
        prior.Tax.Deactivate();

        (BudgetVersion version, IReadOnlyList<string> skipped) = BudgetVersion.CreateOriginalFrom(prior.Version, NextYear.Id, prior.Funds, BudgetSeedOptions.CopyAsIs);

        Assert.Single(version.Lines);
        Assert.Contains(prior.Tax.Code, Assert.Single(skipped), StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_latest_adopted_budget_can_be_the_starting_point()
    {
        Prior prior = AdoptedPrior();
        BudgetVersion draft = TestData.DraftVersion();
        BudgetVersion amendment = prior.Version.CreateAmendment("Supplemental");
        amendment.Propose();
        amendment.Adopt("2027-41", "user-fd", DateTimeOffset.UnixEpoch);
        prior.Version.MarkSupersededBy(amendment);

        Assert.Throws<DomainException>(() => BudgetVersion.CreateOriginalFrom(draft, NextYear.Id, prior.Funds, BudgetSeedOptions.CopyAsIs));
        Assert.Throws<DomainException>(() => BudgetVersion.CreateOriginalFrom(prior.Version, NextYear.Id, prior.Funds, BudgetSeedOptions.CopyAsIs));
        Assert.Throws<DomainException>(() => BudgetVersion.CreateOriginalFrom(amendment, amendment.FiscalYearId, prior.Funds, BudgetSeedOptions.CopyAsIs));
    }

    [Theory]
    [InlineData(-100)]
    [InlineData(150)]
    public void An_implausible_percentage_is_refused(double percent)
    {
        Prior prior = AdoptedPrior();

        Assert.Throws<DomainException>(() => BudgetVersion.CreateOriginalFrom(prior.Version, NextYear.Id, prior.Funds, new BudgetSeedOptions((decimal)percent, SeedAdjustmentScope.AllLines, false)));
    }
}
