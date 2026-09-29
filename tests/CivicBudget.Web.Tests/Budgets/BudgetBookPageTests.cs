using Bunit.TestDoubles;
using CivicBudget.Application.Common;
using CivicBudget.Application.Reports;
using CivicBudget.Domain.Budgets;
using CivicBudget.Web.Components.Admin.Budgets;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Budgets;

/// <summary>
/// The budget book page: the message editor for the fiscal officer, a read-only message otherwise,
/// the section choices carried into the download link, and the defaults saved from the same choices.
/// </summary>
public class BudgetBookPageTests : BunitContext
{
    private static readonly Guid VersionId = Guid.CreateVersion7();
    private readonly FakeBooks _books = new();

    public BudgetBookPageTests()
    {
        Services.AddSingleton<IBudgetBookService>(_books);
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("dana");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static BudgetBookPageDto Page(bool canEdit = true, BudgetStatus status = BudgetStatus.Draft) => new(
        VersionId, 2027, "Original", status, "Proposed budget for 2027", "Dear council:\n\nA steady year.", "Rebecca Lang", "Mayor",
        canEdit, new BudgetBookOptions(true, true, false, true), CanSaveDefaults: canEdit, ["715 Council & Mayor"]);

    private IRenderedComponent<BudgetBook> RenderPage() => Render<BudgetBook>(p => p.Add(x => x.VersionId, VersionId));

    [Fact]
    public void The_fiscal_officer_writes_the_message_and_saves_it()
    {
        _books.Page = Page();
        IRenderedComponent<BudgetBook> page = RenderPage();
        page.WaitForAssertion(() => page.Find("#message-body"));

        page.Find("#message-body").Input("A new letter.");
        page.Find("#message-signer").Change("Sam Ortiz");
        Assert.Contains("13 of 8000 characters", page.Find("#message-count").TextContent);
        page.FindAll("button").Single(b => b.TextContent == "Save message").Click();

        Assert.Equal(("A new letter.", "Sam Ortiz", "Mayor"), (_books.Saved!.Body, _books.Saved.SignedBy, _books.Saved.SignerTitle));
    }

    [Fact]
    public void The_download_link_carries_the_chosen_sections_and_the_choices_can_become_the_defaults()
    {
        _books.Page = Page();
        IRenderedComponent<BudgetBook> page = RenderPage();
        page.WaitForAssertion(() => page.Find("#book-lines"));
        Assert.True(page.FindAll("button").Single(b => b.TextContent == "Use these for published books").HasAttribute("disabled")); // nothing changed yet

        page.Find("#book-lines").Change(true);
        page.Find("#book-glossary").Change(false);

        Assert.EndsWith("book.pdf?outlook=true&personnel=true&lineItems=true&glossary=false", page.Find("a[download]").GetAttribute("href"));
        page.FindAll("button").Single(b => b.TextContent == "Use these for published books").Click();
        Assert.Equal(new BudgetBookOptions(true, true, true, false), _books.SavedDefaults);
    }

    [Fact]
    public void A_draft_says_it_prints_as_proposed_and_names_departments_without_a_narrative()
    {
        _books.Page = Page();

        IRenderedComponent<BudgetBook> page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("says \"Proposed\" on the cover", page.Markup));
        Assert.Contains("No narrative yet from 715 Council &amp; Mayor", page.Markup);
    }

    [Fact]
    public void Someone_who_may_not_write_it_reads_the_message()
    {
        _books.Page = Page(canEdit: false, status: BudgetStatus.Adopted);

        IRenderedComponent<BudgetBook> page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("Proposed budget for 2027", page.Markup));
        Assert.Empty(page.FindAll("#message-body"));
        Assert.Contains("Settled when council adopted the budget", page.Markup);
        Assert.DoesNotContain("Use these for published books", page.Markup);
    }

    [Fact]
    public void A_department_head_is_told_the_book_is_the_whole_government()
    {
        _books.Page = null;

        IRenderedComponent<BudgetBook> page = RenderPage();

        page.WaitForAssertion(() => Assert.Contains("The book covers every fund", page.Markup));
    }

    private sealed class FakeBooks : IBudgetBookService
    {
        public BudgetBookPageDto? Page { get; set; }
        public SaveBudgetMessageRequest? Saved { get; private set; }
        public BudgetBookOptions? SavedDefaults { get; private set; }

        public Task<BudgetBookPageDto?> GetAsync(Guid versionId, CancellationToken ct = default) => Task.FromResult(Page);

        public Task<Result> SaveMessageAsync(SaveBudgetMessageRequest request, CancellationToken ct = default)
        {
            Saved = request;
            return Task.FromResult(Result.Success());
        }

        public Task<Result> SaveDefaultsAsync(BudgetBookOptions options, CancellationToken ct = default)
        {
            SavedDefaults = options;
            return Task.FromResult(Result.Success());
        }

        public Task<BudgetBookOptions> GetDefaultsAsync(CancellationToken ct = default) => Task.FromResult(Page!.Defaults);

        public Task<BudgetBookFile?> RenderAsync(Guid versionId, BudgetBookOptions options, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
