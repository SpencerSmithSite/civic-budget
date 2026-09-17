using CivicBudget.Application.Budgets;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Governments;
using CivicBudget.Web.Components.Admin.Budgets;

namespace CivicBudget.Web.Tests.Budgets;

public class FundBalancePanelTests : BunitContext
{
    private static FundBalanceDto Balance(string code, decimal beginning, decimal revenue, decimal expenditure, AppropriationLimitMode mode, bool canEdit = false)
    {
        var summary = new FundBalanceSummary(Guid.CreateVersion7(), beginning, revenue, 0m, expenditure, 0m);
        return new FundBalanceDto(summary.FundId, code, code + " Fund", FundCategory.General, summary, AppropriationLimitCheck.Evaluate(summary, mode), canEdit);
    }

    [Fact]
    public void Shows_in_balance_when_every_fund_is_within_its_limit()
    {
        IRenderedComponent<FundBalancePanel> panel = Render<FundBalancePanel>(p => p
            .Add(x => x.Balances, [Balance("1000", 100m, 900m, 800m, AppropriationLimitMode.Block)]));

        Assert.Contains("In balance", panel.Find(".card-header").TextContent);
        Assert.Empty(panel.FindAll("tr.table-danger"));
        Assert.Contains("$1,000.00", panel.Markup); // estimated resources
        Assert.Contains("$200.00", panel.Markup);   // projected ending
    }

    [Fact]
    public void Flags_an_over_appropriated_fund_as_an_error_in_block_mode()
    {
        IRenderedComponent<FundBalancePanel> panel = Render<FundBalancePanel>(p => p
            .Add(x => x.Balances, [Balance("2011", 50m, 300m, 400m, AppropriationLimitMode.Block)]));

        Assert.Contains("Over limit", panel.Find(".card-header").TextContent);
        Assert.Single(panel.FindAll("tr.table-danger"));
        Assert.Contains("exceed estimated resources by <strong>$50.00</strong>", panel.Markup);
    }

    [Fact]
    public void Flags_the_same_fund_as_a_warning_in_warn_mode()
    {
        IRenderedComponent<FundBalancePanel> panel = Render<FundBalancePanel>(p => p
            .Add(x => x.Balances, [Balance("2011", 50m, 300m, 400m, AppropriationLimitMode.Warn)]));

        Assert.Contains("Warning", panel.Find(".card-header").TextContent);
        Assert.Single(panel.FindAll("tr.table-warning"));
        Assert.Empty(panel.FindAll("tr.table-danger"));
    }

    [Fact]
    public void Beginning_balance_is_an_input_only_when_the_user_may_edit_it()
    {
        IRenderedComponent<FundBalancePanel> readOnly = Render<FundBalancePanel>(p => p
            .Add(x => x.Balances, [Balance("1000", 100m, 900m, 800m, AppropriationLimitMode.Block, canEdit: false)]));
        IRenderedComponent<FundBalancePanel> editable = Render<FundBalancePanel>(p => p
            .Add(x => x.Balances, [Balance("1000", 100m, 900m, 800m, AppropriationLimitMode.Block, canEdit: true)]));

        Assert.Empty(readOnly.FindAll("input"));
        Assert.Single(editable.FindAll("input"));
    }

    [Fact]
    public async Task Editing_a_beginning_balance_raises_the_callback_with_the_parsed_amount()
    {
        (Guid FundId, decimal Amount)? received = null;
        FundBalanceDto balance = Balance("1000", 100m, 900m, 800m, AppropriationLimitMode.Block, canEdit: true);
        IRenderedComponent<FundBalancePanel> panel = Render<FundBalancePanel>(p => p
            .Add(x => x.Balances, [balance])
            .Add(x => x.OnBeginningBalanceChanged, change => received = change));

        await panel.Find("input").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "1,250.75" });

        Assert.NotNull(received);
        Assert.Equal(balance.FundId, received.Value.FundId);
        Assert.Equal(1250.75m, received.Value.Amount);
    }
}
