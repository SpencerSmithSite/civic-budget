using Bunit.TestDoubles;
using CivicBudget.Application.Notifications;
using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using CivicBudget.Domain.Notifications;
using CivicBudget.Web.Components.Admin;
using CivicBudget.Web.Components.Admin.Notifications;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Security;

/// <summary>
/// The outbox shows what was written and makes its links usable without letting an email inject
/// markup; the getting-started list marks what is done and who does the rest.
/// </summary>
public class OutboxAndGettingStartedTests : BunitContext
{
    public OutboxAndGettingStartedTests()
    {
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void The_outbox_says_the_demo_keeps_mail_and_makes_links_clickable_but_nothing_else()
    {
        Services.AddSingleton<IOutboxService>(new FakeOutbox(new OutboxEmailDto(Guid.NewGuid(), EmailKind.PasswordReset, "viewer@mapleridge.example", "Council Member Lee",
            "Reset your CivicBudget password", "Choose one here:\nhttps://civicbudget.test/Account/ResetPassword?userId=1&code=abc\n<b>not bold</b>",
            DateTimeOffset.UtcNow, EmailStatus.Held, 0, null, null)));
        AddAuthorization().SetAuthorized("admin").SetRoles(Roles.Admin);

        IRenderedComponent<Outbox> page = Render<Outbox>();

        page.WaitForAssertion(() => Assert.Contains("This deployment has no mail server", page.Markup));
        Assert.Equal("https://civicbudget.test/Account/ResetPassword?userId=1&code=abc", page.Find(".cb-outbox-body a").GetAttribute("href"));
        Assert.Empty(page.FindAll(".cb-outbox-body b"));
        Assert.Contains("<b>not bold</b>", page.Find(".cb-outbox-body").TextContent);
        Assert.Contains("Kept here", page.Markup);
    }

    [Fact]
    public void Getting_started_marks_what_is_done_and_sends_administrator_steps_to_an_administrator()
    {
        Services.AddSingleton<ISetupChecklistService>(new FakeChecklist(new SetupChecklistDto("Village of Cedar Falls",
        [
            new SetupStepDto("Your government", "Name and address.", true, false, "admin/settings", "Review", null, AdminOnly: true),
            new SetupStepDto("Chart of accounts", "Funds and accounts.", false, false, "admin/chart-sync", "Bring it in from the ERP", "0 funds."),
            new SetupStepDto("Your team", "People.", false, false, "admin/users", "Add people", "1 user.", AdminOnly: true),
            new SetupStepDto("Personnel", "Rates.", false, true, "admin/personnel-settings", "Set up personnel", null),
        ])));
        AddAuthorization().SetAuthorized("officer").SetRoles(Roles.FinanceDirector).SetPolicies(Policies.CanMaintainSetup);

        IRenderedComponent<GettingStarted> page = Render<GettingStarted>();

        page.WaitForAssertion(() => Assert.Contains("1 of 3", page.Markup));
        Assert.Single(page.FindAll(".cb-checklist-step.is-done"));
        Assert.Equal("admin/chart-sync", page.FindAll("a.btn-primary")[0].GetAttribute("href"));
        Assert.Equal(2, page.FindAll(".cb-checklist-action").Count(a => a.TextContent.Contains("An Administrator does this", StringComparison.Ordinal)));
    }

    private sealed class FakeOutbox(OutboxEmailDto email) : IOutboxService
    {
        public Task<OutboxPageDto?> ListAsync(int skip, int take, CancellationToken ct = default) =>
            Task.FromResult<OutboxPageDto?>(new OutboxPageDto(false, 1, skip == 0 ? [email] : []));
    }

    private sealed class FakeChecklist(SetupChecklistDto list) : ISetupChecklistService
    {
        public Task<SetupChecklistDto> GetAsync(CancellationToken ct = default) => Task.FromResult(list);
    }
}
