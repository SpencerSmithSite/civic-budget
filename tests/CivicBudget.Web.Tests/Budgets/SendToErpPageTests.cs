using Bunit.TestDoubles;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.Erp;
using CivicBudget.Web.Components.Admin.Budgets;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Budgets;

/// <summary>
/// The send page shows what the journal posts before anything goes, holds the year while a send is
/// unfinished, and says so plainly when VIP already has the budget.
/// </summary>
public class SendToErpPageTests : BunitContext
{
    private static readonly Guid VersionId = Guid.CreateVersion7();
    private readonly FakeTransmissionService _sends = new();

    public SendToErpPageTests()
    {
        Services.AddSingleton<IBudgetTransmissionService>(_sends);
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        AddAuthorization().SetAuthorized("dana");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static JournalChange Change(string number, decimal inErp, decimal inBudget) =>
        new(new LineKey(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()), number, "Account " + number, inErp, inBudget);

    private static TransmissionDto Sent(TransmissionStatus status, string? reference = null) => new(
        Guid.CreateVersion7(), "Original", TransmissionMethod.Api, status, "VIP (simulated)", "FY2026 Original", new DateOnly(2026, 1, 1),
        new DateTimeOffset(2026, 1, 5, 14, 30, 0, TimeSpan.Zero), "system", null, reference, status == TransmissionStatus.Failed ? "No answer from VIP (simulated): timed out" : null,
        [new TransmissionLineDto("1000-110-5120", 38_000m, null)]);

    private static SendPageDto Page(IReadOnlyList<JournalChange> changes, TransmissionDto? open = null, IReadOnlyList<TransmissionDto>? history = null) => new(
        VersionId, 2026, "Amendment 1", new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), "VIP", "VIP (simulated)", null,
        "FY2026 Amendment 1, resolution 2026-11", new DateOnly(2026, 6, 15), changes, open, history ?? (open is null ? [] : [open]));

    [Fact]
    public async Task Shows_what_the_journal_posts_and_sends_it_with_the_description_and_date()
    {
        _sends.Page = Page([Change("1000-110-5120", 38_000m, 53_000m), Change("1000-110-5310", 62_000m, 71_000m)], history: [Sent(TransmissionStatus.Accepted, "BJ2026-00007")]);
        _sends.Answer = Sent(TransmissionStatus.Accepted, "BJ2026-00301");

        IRenderedComponent<SendToErp> page = Render<SendToErp>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Contains("+15,000.00", page.Find("table[aria-label='What the journal posts']").TextContent));
        Assert.Equal("FY2026 Amendment 1, resolution 2026-11", page.Find("#journalDescription").GetAttribute("value"));
        Assert.Equal("2026-06-15", page.Find("#postingDate").GetAttribute("value"));
        Assert.Contains("Only what changed since the last journal", page.Find(".cb-kpis").TextContent);
        Assert.Contains("+$24,000.00", page.Find(".cb-kpis").TextContent);

        await page.Find("#journalDescription").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "Supplemental appropriation" });
        await page.Find("button:contains('Send to VIP')").ClickAsync(new());
        Assert.Null(_sends.Sent);                                                            // nothing goes until the dialog is confirmed
        await page.Find(".modal-footer .btn-primary").ClickAsync(new());

        Assert.Equal(new SendBudgetRequest(VersionId, "Supplemental appropriation", new DateOnly(2026, 6, 15)), _sends.Sent);
    }

    [Fact]
    public async Task A_send_with_no_answer_holds_the_year_and_offers_a_safe_retry()
    {
        TransmissionDto failed = Sent(TransmissionStatus.Failed);
        _sends.Page = Page([Change("1000-110-5120", 38_000m, 53_000m)], open: failed);
        _sends.Answer = failed with { Status = TransmissionStatus.Accepted, ErpReference = "BJ2026-00301" };

        IRenderedComponent<SendToErp> page = Render<SendToErp>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Contains("got no answer from VIP (simulated)", page.Find(".cb-open-send").TextContent));
        Assert.True(page.Find("button:contains('Send to VIP')").HasAttribute("disabled"));   // no new send while one is unsettled
        Assert.True(page.Find("#journalDescription").HasAttribute("disabled"));

        await page.Find(".cb-open-send button:contains('Try again')").ClickAsync(new());

        Assert.Equal(failed.Id, _sends.Retried);
    }

    [Fact]
    public void Says_so_when_vip_already_has_the_budget()
    {
        _sends.Page = Page([], history: [Sent(TransmissionStatus.Accepted, "BJ2026-00007")]);

        IRenderedComponent<SendToErp> page = Render<SendToErp>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Contains("VIP already has this budget", page.Markup));
        Assert.Contains("BJ2026-00007", page.Markup);
        Assert.Empty(page.FindAll("button:contains('Send to VIP')"));
        Assert.Contains("Posted", page.Find("table[aria-label='FY2026 sends']").TextContent);
    }
}
