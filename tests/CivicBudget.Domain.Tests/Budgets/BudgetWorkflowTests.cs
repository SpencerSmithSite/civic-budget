using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;

namespace CivicBudget.Domain.Tests.Budgets;

public class BudgetWorkflowTests
{
    private static readonly DateTimeOffset Now = new(2026, 12, 15, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void New_version_starts_as_draft_original()
    {
        BudgetVersion version = TestData.DraftVersion();

        Assert.Equal(BudgetStatus.Draft, version.Status);
        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("Original", version.Label);
        Assert.False(version.IsAmendment);
        Assert.True(version.IsEditable);
    }

    [Fact]
    public void Draft_can_be_proposed_then_adopted()
    {
        BudgetVersion version = TestData.DraftVersion();

        version.Propose();
        Assert.Equal(BudgetStatus.Proposed, version.Status);

        version.Adopt("2026-14", "user-fd", Now);
        Assert.Equal(BudgetStatus.Adopted, version.Status);
        Assert.Equal("2026-14", version.ResolutionNumber);
        Assert.Equal("user-fd", version.AdoptedByUserId);
        Assert.Equal(Now, version.AdoptedOnUtc);
        Assert.False(version.IsEditable);
    }

    [Fact]
    public void Proposed_can_be_returned_to_draft()
    {
        BudgetVersion version = TestData.DraftVersion();
        version.Propose();

        version.ReturnToDraft();

        Assert.Equal(BudgetStatus.Draft, version.Status);
    }

    [Fact]
    public void Cannot_skip_from_draft_to_adopted() =>
        Assert.Throws<DomainException>(() => TestData.DraftVersion().Adopt("2026-14", "user-fd", Now));

    [Fact]
    public void Cannot_propose_twice()
    {
        BudgetVersion version = TestData.DraftVersion();
        version.Propose();

        Assert.Throws<DomainException>(version.Propose);
    }

    [Fact]
    public void Cannot_return_a_draft_to_draft() =>
        Assert.Throws<DomainException>(() => TestData.DraftVersion().ReturnToDraft());

    [Fact]
    public void Adopted_cannot_be_returned_to_draft() =>
        Assert.Throws<DomainException>(() => TestData.AdoptedVersion().ReturnToDraft());

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Adoption_requires_a_resolution_number(string resolution)
    {
        BudgetVersion version = TestData.DraftVersion();
        version.Propose();

        Assert.Throws<DomainException>(() => version.Adopt(resolution, "user-fd", Now));
        Assert.Equal(BudgetStatus.Proposed, version.Status);
    }

    [Fact]
    public void Only_one_open_version_per_fiscal_year()
    {
        BudgetVersion adopted = TestData.AdoptedVersion();
        BudgetVersion draft = TestData.DraftVersion();

        BudgetVersion.EnsureNoOpenVersion([adopted]);                                     // fine
        Assert.Throws<DomainException>(() => BudgetVersion.EnsureNoOpenVersion([adopted, draft]));
    }
}
