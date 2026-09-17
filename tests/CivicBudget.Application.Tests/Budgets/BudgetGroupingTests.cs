using CivicBudget.Application.Budgets;
using CivicBudget.Domain.Accounts;

namespace CivicBudget.Application.Tests.Budgets;

/// <summary>The by-department tree and its subtotals, without a database or a component.</summary>
public class BudgetGroupingTests
{
    private static readonly Guid General = Guid.CreateVersion7();
    private static readonly Guid Street = Guid.CreateVersion7();
    private static readonly Guid Police = Guid.CreateVersion7();
    private static readonly Guid Streets = Guid.CreateVersion7();

    private static BudgetLineDto Line(Guid fund, string fundCode, Guid? dept, string? deptCode, string account, AccountType type, ReportingCategory category, decimal amount, decimal current) =>
        new(Guid.CreateVersion7(), fund, fundCode, fundCode + " Fund", dept, deptCode, deptCode is null ? null : deptCode + " Dept",
            Guid.CreateVersion7(), account, "Account " + account, type, category, amount, 0m, current, null, true);

    private static readonly BudgetLineDto[] Lines =
    [
        Line(General, "1000", null, null, "4110", AccountType.Revenue, ReportingCategory.Taxes, 500m, 480m),          // fund-level, no department
        Line(General, "1000", Police, "PD", "5110", AccountType.Expenditure, ReportingCategory.PersonalServices, 300m, 280m),
        Line(General, "1000", Police, "PD", "5120", AccountType.Expenditure, ReportingCategory.PersonalServices, 50m, 40m),
        Line(General, "1000", Police, "PD", "5410", AccountType.Expenditure, ReportingCategory.SuppliesAndMaterials, 20m, 20m),
        Line(Street, "2011", Streets, "ST", "5110", AccountType.Expenditure, ReportingCategory.PersonalServices, 100m, 90m),
        Line(General, "1000", Streets, "ST", "5320", AccountType.Expenditure, ReportingCategory.ContractualServices, 30m, 25m),
    ];

    [Fact]
    public void Groups_department_then_fund_then_category_in_code_order()
    {
        IReadOnlyList<LineGroup> tree = BudgetGrouping.ByDepartment(Lines);

        Assert.Equal(["PD PD Dept", "ST ST Dept"], tree.Select(g => g.Title));

        LineGroup police = tree[0];
        LineGroup policeGeneral = Assert.Single(police.Children);
        Assert.Equal("1000 1000 Fund", policeGeneral.Title);
        Assert.Equal(["Personal Services", "Supplies & Materials"], policeGeneral.Children.Select(c => c.Title));

        LineGroup streets = tree[1];
        Assert.Equal(["1000 1000 Fund", "2011 2011 Fund"], streets.Children.Select(f => f.Title));
    }

    [Fact]
    public void Fund_level_lines_without_a_department_are_left_out_of_the_department_view()
    {
        IReadOnlyList<LineGroup> tree = BudgetGrouping.ByDepartment(Lines);
        decimal total = tree.Sum(g => g.Amount);

        Assert.Equal(500m, Lines.Sum(l => l.Amount) - total); // exactly the revenue line is excluded
    }

    [Fact]
    public void Subtotals_roll_up_at_every_level()
    {
        LineGroup police = BudgetGrouping.ByDepartment(Lines)[0];
        LineGroup personalServices = police.Children[0].Children[0];

        Assert.Equal(350m, personalServices.Amount);
        Assert.Equal(320m, personalServices.CurrentYearBudget);
        Assert.Equal(30m, personalServices.DollarChange);
        Assert.Equal(9.4m, personalServices.PercentChange);   // 30 / 320 = 9.375 rounds to 9.4

        Assert.Equal(370m, police.Children[0].Amount);        // fund subtotal
        Assert.Equal(370m, police.Amount);                    // department total
    }

    [Fact]
    public void Transfer_categories_are_titled_by_direction()
    {
        BudgetLineDto[] transfers =
        [
            Line(General, "1000", Police, "PD", "4910", AccountType.TransferIn, ReportingCategory.Transfers, 10m, 10m),
            Line(General, "1000", Police, "PD", "5910", AccountType.TransferOut, ReportingCategory.Transfers, 5m, 5m),
        ];

        IReadOnlyList<LineGroup> categories = BudgetGrouping.ByCategory(transfers);

        Assert.Equal(["Transfers In", "Transfers Out"], categories.Select(c => c.Title));
    }
}
