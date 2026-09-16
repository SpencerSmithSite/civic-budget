using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Tests.Budgets;

public class BudgetAmendmentTests
{
    private static BudgetVersion AdoptedWithData()
    {
        BudgetVersion version = TestData.DraftVersion();
        version.AddLine(TestData.GeneralFund(), TestData.Police(), TestData.Salaries(), 100_000m, 95_000m, 98_000m, "Two officers");
        version.AddLine(TestData.GeneralFund(), null, TestData.PropertyTax(), 250_000m);
        version.SetBeginningBalance(TestData.GeneralFund(), 40_000m);
        version.Propose();
        version.Adopt("2026-14", "user-fd", DateTimeOffset.UtcNow);
        return version;
    }

    [Fact]
    public void Amendment_is_a_new_draft_with_the_next_version_number()
    {
        BudgetVersion adopted = AdoptedWithData();

        BudgetVersion amendment = adopted.CreateAmendment("Mid-year police overtime");

        Assert.NotEqual(adopted.Id, amendment.Id);
        Assert.Equal(2, amendment.VersionNumber);
        Assert.Equal("Amendment 1", amendment.Label);
        Assert.True(amendment.IsAmendment);
        Assert.Equal(BudgetStatus.Draft, amendment.Status);
        Assert.Equal("Mid-year police overtime", amendment.AmendmentReason);
        Assert.Equal(adopted.FiscalYearId, amendment.FiscalYearId);
        Assert.Null(amendment.ResolutionNumber);
    }

    [Fact]
    public void Amendment_copies_lines_and_balances_with_new_identities()
    {
        BudgetVersion adopted = AdoptedWithData();

        BudgetVersion amendment = adopted.CreateAmendment("reason");

        Assert.Equal(adopted.Lines.Count, amendment.Lines.Count);
        foreach ((BudgetLine original, BudgetLine copy) in adopted.Lines.Zip(amendment.Lines))
        {
            Assert.NotEqual(original.Id, copy.Id);
            Assert.Equal(amendment.Id, copy.BudgetVersionId);
            Assert.Equal(original.FundId, copy.FundId);
            Assert.Equal(original.DepartmentId, copy.DepartmentId);
            Assert.Equal(original.AccountId, copy.AccountId);
            Assert.Equal(original.Amount, copy.Amount);
            Assert.Equal(original.PriorYearActual, copy.PriorYearActual);
            Assert.Equal(original.CurrentYearBudget, copy.CurrentYearBudget);
            Assert.Equal(original.Justification, copy.Justification);
        }

        Assert.Equal(40_000m, amendment.GetBeginningBalance(adopted.BeginningBalances.Single().FundId));
        Assert.NotEqual(adopted.BeginningBalances.Single().Id, amendment.BeginningBalances.Single().Id);
    }

    [Fact]
    public void Editing_the_amendment_leaves_the_adopted_version_untouched()
    {
        BudgetVersion adopted = AdoptedWithData();
        BudgetVersion amendment = adopted.CreateAmendment("reason");

        amendment.UpdateLineAmount(amendment.Lines.First().Id, 1m);

        Assert.Equal(100_000m, adopted.Lines.First().Amount);
    }

    [Fact]
    public void Second_amendment_is_numbered_three()
    {
        BudgetVersion first = AdoptedWithData().CreateAmendment("one");
        first.Propose();
        first.Adopt("2027-02", "user-fd", DateTimeOffset.UtcNow);

        BudgetVersion second = first.CreateAmendment("two");

        Assert.Equal(3, second.VersionNumber);
        Assert.Equal("Amendment 2", second.Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Amendment_requires_a_reason(string reason) =>
        Assert.Throws<DomainException>(() => AdoptedWithData().CreateAmendment(reason));

    [Fact]
    public void Only_adopted_versions_can_be_amended()
    {
        BudgetVersion draft = TestData.DraftVersion();
        Assert.Throws<DomainException>(() => draft.CreateAmendment("reason"));

        draft.Propose();
        Assert.Throws<DomainException>(() => draft.CreateAmendment("reason"));
    }

    [Fact]
    public void Adopted_amendment_supersedes_the_previous_adopted_version()
    {
        BudgetVersion original = AdoptedWithData();
        BudgetVersion amendment = original.CreateAmendment("reason");
        amendment.Propose();
        amendment.Adopt("2027-02", "user-fd", DateTimeOffset.UtcNow);

        original.MarkSupersededBy(amendment);

        Assert.Equal(amendment.Id, original.SupersededByVersionId);
    }

    [Fact]
    public void A_version_cannot_be_superseded_by_an_unadopted_amendment()
    {
        BudgetVersion original = AdoptedWithData();
        BudgetVersion amendment = original.CreateAmendment("reason");

        Assert.Throws<DomainException>(() => original.MarkSupersededBy(amendment));
    }
}
