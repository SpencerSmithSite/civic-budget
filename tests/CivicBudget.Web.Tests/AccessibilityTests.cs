using Bunit.TestDoubles;
using CivicBudget.Application.Common;
using CivicBudget.Web.Components.Account.Shared;
using CivicBudget.Web.Components.Admin.Budgets;
using CivicBudget.Web.Components.Common;
using CivicBudget.Web.Components.Portal.Common;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests;

/// <summary>
/// The accessibility behaviors the self-scan asked for: errors that are gathered, announced, and
/// focused; states a screen reader can read; headings that say only what they are; and messages
/// that stay until someone has read them.
/// </summary>
public class AccessibilityTests : BunitContext
{
    public AccessibilityTests()
    {
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void Aria_states_are_the_strings_true_and_false()
    {
        Assert.Equal("true", Aria.Bool(true));
        Assert.Equal("false", Aria.Bool(false));
    }

    [Fact]
    public void A_failed_result_is_one_focusable_alert_listing_every_error()
    {
        Result failed = Result.Failure([new ValidationError("Code", "Code is required."), new ValidationError("", "The fund is in use.")]);

        IRenderedComponent<ResultAlert> alert = Render<ResultAlert>(p => p.Add(x => x.Result, failed));

        var box = alert.Find(".alert");
        Assert.Equal("alert", box.GetAttribute("role"));
        Assert.Equal("-1", box.GetAttribute("tabindex"));
        Assert.Equal(["Code is required.", "The fund is in use."], alert.FindAll("li").Select(li => li.TextContent));
        // A new failure takes focus, so the person lands on what went wrong.
        Assert.Single(JSInterop.Invocations, i => i.Identifier == "Blazor._internal.domWrapper.focus");
    }

    [Fact]
    public void A_refused_amount_is_marked_invalid_and_announced()
    {
        IRenderedComponent<AmountCell> cell = Render<AmountCell>(p => p
            .Add(x => x.Value, 100m)
            .Add(x => x.CanEdit, true)
            .Add(x => x.Label, "Proposed amount for 4110 Real Estate Taxes, 1000"));

        cell.Find("input").Change("abc");

        Assert.Equal("true", cell.Find("input").GetAttribute("aria-invalid"));
        ToastMessage toast = Assert.Single(Services.GetRequiredService<ToastService>().Toasts);
        Assert.Equal(ToastKind.Danger, toast.Kind);
        Assert.Contains("Proposed amount for 4110 Real Estate Taxes", toast.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failure_toast_stays_until_dismissed_and_a_success_goes_by_itself()
    {
        ToastService toasts = Services.GetRequiredService<ToastService>();
        IRenderedComponent<ToastHost> host = Render<ToastHost>();

        await host.InvokeAsync(() => { toasts.Danger("Save failed."); toasts.Success("Amount saved."); });
        await Task.Delay(TimeSpan.FromSeconds(4.5));

        ToastMessage left = Assert.Single(toasts.Toasts);
        Assert.Equal(ToastKind.Danger, left.Kind);
    }

    [Fact]
    public void The_page_heading_is_the_title_alone()
    {
        IRenderedComponent<PageHeader> header = Render<PageHeader>(p => p
            .Add(x => x.Title, "Original budget")
            .Add(x => x.Tip, "Edit amounts in place.")
            .Add(x => x.TitleAddon, (RenderFragment)(b => b.AddMarkupContent(0, "<span class=\"cb-pill\">Draft</span>"))));

        Assert.Equal("Original budget", header.Find("h1").TextContent);
        Assert.NotNull(header.Find(".cb-page-title .cb-pill"));
        Assert.NotNull(header.Find(".cb-page-title .cb-tip"));
    }

    [Fact]
    public void Portal_panel_radios_are_a_named_group()
    {
        IRenderedComponent<PortalPanels> panels = Render<PortalPanels>(p => p
            .Add(x => x.Name, "flow")
            .Add(x => x.Label, "Spending or revenue")
            .Add(x => x.FirstTitle, "Where does the money go?")
            .Add(x => x.SecondTitle, "Where does it come from?"));

        var fieldset = panels.Find("fieldset");
        Assert.Equal("Spending or revenue", fieldset.QuerySelector("legend")!.TextContent);
        Assert.Equal(2, fieldset.QuerySelectorAll("input[type=radio]").Length);
    }

    [Fact]
    public void A_posted_form_gathers_its_errors_in_one_box_that_takes_focus_on_load()
    {
        var context = new EditContext(new object());
        var store = new ValidationMessageStore(context);
        store.Add(context.Field("Email"), "The Email field is required.");

        IRenderedComponent<CascadingValue<EditContext>> rendered = Render<CascadingValue<EditContext>>(p => p
            .Add(x => x.Value, context)
            .AddChildContent<FormErrors>(f => f.Add(x => x.Message, "Error: Invalid login attempt.")));

        var box = rendered.Find("#form-errors");
        Assert.Equal("alert", box.GetAttribute("role"));
        Assert.True(box.HasAttribute("data-focus-on-load"));
        Assert.Equal(["Invalid login attempt.", "The Email field is required."], box.QuerySelectorAll("li").Select(li => li.TextContent));
    }
}
