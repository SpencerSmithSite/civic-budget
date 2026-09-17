using CivicBudget.Application.Budgets;
using CivicBudget.Domain.Accounts;
using CivicBudget.Web.Components.Admin.Budgets;

namespace CivicBudget.Web.Tests.Budgets;

public class DepartmentEntryViewTests : BunitContext
{
    private static readonly Guid General = Guid.CreateVersion7();
    private static readonly Guid Police = Guid.CreateVersion7();

    private static BudgetLineDto Line(string account, ReportingCategory category, decimal amount, decimal current, bool canEdit) =>
        new(Guid.CreateVersion7(), General, "1000", "General Fund", Police, "PD", "Police",
            Guid.CreateVersion7(), account, "Account " + account, AccountType.Expenditure, category, amount, 0m, current, null, canEdit);

    [Fact]
    public void Renders_category_subtotals_and_fund_totals()
    {
        IRenderedComponent<DepartmentEntryView> view = Render<DepartmentEntryView>(p => p
            .Add(x => x.Lines,
            [
                Line("5110", ReportingCategory.PersonalServices, 300m, 280m, canEdit: false),
                Line("5120", ReportingCategory.PersonalServices, 50m, 40m, canEdit: false),
                Line("5410", ReportingCategory.SuppliesAndMaterials, 20m, 20m, canEdit: false),
            ]));

        Assert.Contains("PD Police", view.Find("h2").TextContent);
        Assert.Contains("Subtotal, Personal Services", view.Markup);
        Assert.Contains("$350.00", view.Markup);                         // category subtotal
        Assert.Contains("Total, 1000 General Fund", view.Markup);
        Assert.Contains("$370.00", view.Markup);                         // fund total
        Assert.Contains("Department total: $370.00", view.Markup);
        Assert.Empty(view.FindAll("input"));                              // nothing editable
    }

    [Fact]
    public async Task Editable_lines_render_inputs_that_report_the_new_amount()
    {
        (Guid LineId, decimal Amount)? received = null;
        BudgetLineDto line = Line("5110", ReportingCategory.PersonalServices, 300m, 280m, canEdit: true);
        IRenderedComponent<DepartmentEntryView> view = Render<DepartmentEntryView>(p => p
            .Add(x => x.Lines, [line])
            .Add(x => x.OnAmountChanged, change => received = change));

        await view.Find("input").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "325" });

        Assert.Equal((line.Id, 325m), received);
    }
}
