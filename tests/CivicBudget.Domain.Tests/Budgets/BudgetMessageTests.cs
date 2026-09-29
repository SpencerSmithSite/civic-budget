using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Common;
using CivicBudget.Domain.FiscalYears;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Publishing;
using CivicBudget.Domain.Reports;

namespace CivicBudget.Domain.Tests.Budgets;

/// <summary>
/// The budget message that opens the budget book: written while the budget is open, settled by
/// adoption like everything else, and carried into an amendment and next year's budget as a draft.
/// </summary>
public class BudgetMessageTests
{
    private const string Letter = "To the residents of Maple Ridge:\n\nThis budget holds the line.";

    [Fact]
    public void A_message_is_trimmed_and_optional_parts_left_blank_stay_empty()
    {
        BudgetVersion version = TestData.DraftVersion();

        version.SetMessage("  ", "  " + Letter + "  ", " Rebecca Lang ", "");

        Assert.Equal((null, Letter, "Rebecca Lang", null), (version.MessageHeading, version.MessageBody, version.MessageSignedBy, version.MessageSignerTitle));
    }

    [Fact]
    public void An_empty_body_clears_the_whole_message()
    {
        BudgetVersion version = TestData.DraftVersion();
        version.SetMessage("Budget message", Letter, "Rebecca Lang", "Mayor");

        version.SetMessage("Budget message", " ", "Rebecca Lang", "Mayor");

        Assert.Equal((null, null, null, null), (version.MessageHeading, version.MessageBody, version.MessageSignedBy, version.MessageSignerTitle));
    }

    [Fact]
    public void A_message_has_limits()
    {
        BudgetVersion version = TestData.DraftVersion();

        Assert.Throws<DomainException>(() => version.SetMessage(null, new string('x', BudgetVersion.MessageMaxLength + 1), null, null));
        Assert.Throws<DomainException>(() => version.SetMessage(new string('x', BudgetVersion.MessageHeadingMaxLength + 1), Letter, null, null));
        Assert.Throws<DomainException>(() => version.SetMessage(null, Letter, new string('x', BudgetVersion.MessageSignerMaxLength + 1), null));
    }

    [Fact]
    public void Adoption_settles_the_message()
    {
        Assert.Throws<DomainException>(() => TestData.AdoptedVersion().SetMessage(null, Letter, null, null));
    }

    [Fact]
    public void An_amendment_and_next_years_budget_start_from_the_message()
    {
        Fund general = TestData.GeneralFund();
        BudgetVersion version = TestData.DraftVersion();
        version.AddLine(general, TestData.Police(), TestData.Salaries(), 100m);
        version.SetMessage("A steady year", Letter, "Rebecca Lang", "Mayor");
        version.Propose();
        version.Adopt("2027-1", "user", DateTimeOffset.UnixEpoch);

        BudgetVersion amendment = version.CreateAmendment("Grant");
        (BudgetVersion next, _) = BudgetVersion.CreateOriginalFrom(version, new FiscalYear(TestData.GovernmentId, 2028, 1).Id,
            new Dictionary<Guid, Fund> { [general.Id] = general }, BudgetSeedOptions.CopyAsIs);

        Assert.All([amendment, next], v => Assert.Equal(("A steady year", Letter, "Rebecca Lang", "Mayor"), (v.MessageHeading, v.MessageBody, v.MessageSignedBy, v.MessageSignerTitle)));
        next.SetMessage(null, "A new letter.", null, null); // the copy is a draft to rewrite
        Assert.Equal(Letter, version.MessageBody);
    }

    [Fact]
    public void A_book_has_optional_sections_that_default_to_a_useful_book()
    {
        var settings = new BudgetBookSettings(TestData.GovernmentId);

        Assert.Equal((true, true, false, true), (settings.IncludeOutlook, settings.IncludePersonnel, settings.IncludeLineItems, settings.IncludeGlossary));
    }

    [Fact]
    public void A_published_book_is_never_empty()
    {
        Assert.Throws<DomainException>(() => new PublishedBudgetBook(Guid.CreateVersion7(), TestData.GovernmentId, [], DateTimeOffset.UnixEpoch));
    }
}
