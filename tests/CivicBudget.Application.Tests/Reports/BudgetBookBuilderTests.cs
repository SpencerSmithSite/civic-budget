using CivicBudget.Application.Budgets;
using CivicBudget.Application.Reports;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;

namespace CivicBudget.Application.Tests.Reports;

/// <summary>
/// What the budget book's pages say, from the same reports the screens show: a fund's money by
/// source and by department, a department's spending by kind, the message in paragraphs, and the
/// optional sections in or out.
/// </summary>
public class BudgetBookBuilderTests
{
    private static readonly ReportHeaderDto Header = new(Guid.CreateVersion7(), "Village of Maple Ridge", 2027, "Original", BudgetStatus.Draft, null, "Dana", DateTimeOffset.UnixEpoch);

    private static BudgetLineDto Line(string fund, string? dept, string account, AccountType type, ReportingCategory category, decimal amount, decimal current = 0m, decimal prior = 0m) =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), fund, fund == "1000" ? "General Fund" : "Capital Projects",
            dept is null ? null : Guid.CreateVersion7(), dept, dept switch { "110" => "Police", "620" => "Streets", _ => null },
            Guid.CreateVersion7(), account, account, $"{fund}-{dept}-{account}", type, category, amount, prior, current, null, CanEdit: false);

    private static readonly List<BudgetLineDto> Lines =
    [
        Line("1000", null, "4110", AccountType.Revenue, ReportingCategory.Taxes, 400m, 380m, 370m),
        Line("1000", null, "4130", AccountType.Revenue, ReportingCategory.Taxes, 600m, 590m, 580m),
        Line("1000", null, "4210", AccountType.Revenue, ReportingCategory.Intergovernmental, 100m),
        Line("1000", "110", "5110", AccountType.Expenditure, ReportingCategory.PersonalServices, 500m, 480m),
        Line("1000", "110", "5410", AccountType.Expenditure, ReportingCategory.SuppliesAndMaterials, 50m),
        Line("1000", "620", "5110", AccountType.Expenditure, ReportingCategory.PersonalServices, 200m),
        Line("1000", null, "5910", AccountType.TransferOut, ReportingCategory.Transfers, 75m),
        Line("4901", null, "4910", AccountType.TransferIn, ReportingCategory.Transfers, 75m),
    ];

    private static BudgetBookSources Sources(string? message = "Dear residents:\n\nA steady year.\r\n\r\nThank you.", BudgetPlanDto? plan = null, PersonnelCostDto? personnel = null)
    {
        FundSummaryRowDto general = new("1000", "General Fund", FundCategory.General, 200m, 1_100m, 0m, 750m, 75m, true);
        FundSummaryRowDto capital = new("4901", "Capital Projects", FundCategory.CapitalProjects, 0m, 0m, 75m, 0m, 0m, true);
        DetailLineDto Detail(BudgetLineDto l) => new(l.FundCode, l.FundName, l.AccountCode, l.AccountName, l.AccountNumber, l.AccountType, l.Category, l.PriorYearActual, l.CurrentYearBudget, l.Amount, null);
        var police = new DepartmentDetailDto(Guid.CreateVersion7(), "110", "Police", "  Eight officers.  ", [.. Lines.Where(l => l.DepartmentCode == "110").Select(Detail)]);
        var streets = new DepartmentDetailDto(Guid.CreateVersion7(), "620", "Streets", " ", [.. Lines.Where(l => l.DepartmentCode == "620").Select(Detail)]);
        return new BudgetBookSources(
            new BookCoverDto("Village of Maple Ridge", 2027, "Original", BudgetStatus.Draft, null, null, null, null, "Dana", DateTimeOffset.UnixEpoch),
            null, message, "Rebecca Lang", "Mayor",
            new FundSummaryReportDto(Header, [general, capital], general),
            new CategoryReportDto(Header, [], new CategoryRowDto("Revenues", 0, 0, 0), [], new CategoryRowDto("Expenditures", 0, 0, 0)),
            new DepartmentDetailReportDto(Header, [streets, police], [], null),
            Lines,
            new Dictionary<string, string?> { ["1000"] = "Day-to-day services." },
            Certificate: null, plan, personnel);
    }

    private static readonly BudgetBookOptions Everything = new(true, true, true, true);

    [Fact]
    public void A_funds_page_shows_its_money_by_source_and_by_who_spends_it()
    {
        BookFundDto general = BudgetBookBuilder.Build(Sources(), Everything).Funds.Single(f => f.Code == "1000");

        Assert.Equal("Day-to-day services.", general.Description);
        Assert.Equal([("Taxes", 1_000m, 970m), ("Intergovernmental", 100m, 0m)], general.Resources.Select(r => (r.Label, r.Amount, r.CurrentYearBudget)));
        Assert.Equal([("110 Police", 550m), ("620 Streets", 200m), ("Transfers out", 75m)], general.Uses.Select(u => (u.Label, u.Amount)));
        Assert.Equal(["Transfers in"], BudgetBookBuilder.Build(Sources(), Everything).Funds.Single(f => f.Code == "4901").Resources.Select(r => r.Label));
    }

    [Fact]
    public void A_departments_page_counts_what_it_spends_by_kind()
    {
        BookDepartmentDto police = BudgetBookBuilder.Build(Sources(), Everything).Departments[0];

        Assert.Equal("110", police.Code); // in code order, whatever order the report gave
        Assert.Equal("Eight officers.", police.Narrative);
        Assert.Equal(["1000 General Fund"], police.Funds);
        Assert.Equal([("Personal services", 500m), ("Supplies and materials", 50m)], police.Categories.Select(c => (c.Label, c.Amount)));
        Assert.Equal((550m, 70m), (police.Total.Amount, police.Total.Change));
        Assert.Null(BudgetBookBuilder.Build(Sources(), Everything).Departments[^1].Narrative); // a blank narrative prints nothing
    }

    [Fact]
    public void The_message_keeps_its_paragraphs_and_a_heading()
    {
        BookMessageDto message = BudgetBookBuilder.Build(Sources(), Everything).Message!;

        Assert.Equal(BudgetBookBuilder.DefaultMessageHeading, message.Heading);
        Assert.Equal(["Dear residents:", "A steady year.", "Thank you."], message.Paragraphs);
        Assert.Equal(("Rebecca Lang", "Mayor"), (message.SignedBy, message.SignerTitle));
        Assert.Null(BudgetBookBuilder.Build(Sources(message: "  "), Everything).Message);
    }

    [Fact]
    public void Optional_sections_are_left_out_when_not_chosen_or_when_there_is_nothing_to_show()
    {
        var plan = new BudgetPlanDto(Guid.CreateVersion7(), 2027, "Original", BudgetStatus.Draft, 5, true, [], true, [], []);
        var onlyBudgetYear = plan with { Years = 1 };
        var personnel = new PersonnelCostDto(Header, [new PersonnelCostFundDto("1000", "General Fund", [], new PersonnelCostRowDto("Total", 1m, 0m, 0m, 0m, 0m))], new PersonnelCostRowDto("All", 1m, 0m, 0m, 0m, 0m), HasSettings: true);

        BudgetBookDto all = BudgetBookBuilder.Build(Sources(plan: plan, personnel: personnel), Everything);
        BudgetBookDto none = BudgetBookBuilder.Build(Sources(plan: plan, personnel: personnel), new BudgetBookOptions(false, false, false, false));
        BudgetBookDto nothingToShow = BudgetBookBuilder.Build(Sources(plan: onlyBudgetYear, personnel: personnel with { HasSettings = false }), Everything);

        Assert.True(all.Outlook is not null && all.Personnel is not null && all.LineItems is not null && all.Glossary is not null);
        Assert.True(none.Outlook is null && none.Personnel is null && none.LineItems is null && none.Glossary is null);
        Assert.Null(nothingToShow.Outlook);
        Assert.Null(nothingToShow.Personnel);
    }

    [Fact]
    public void The_line_item_appendix_groups_by_fund_then_fund_level_then_department()
    {
        IReadOnlyList<BookLineGroupDto> groups = BudgetBookBuilder.Build(Sources(), Everything).LineItems!;

        Assert.Equal(
            ["1000 General Fund · Fund-level", "1000 General Fund · 110 Police", "1000 General Fund · 620 Streets", "4901 Capital Projects · Fund-level"],
            groups.Select(g => g.Title));
        Assert.Equal(4, groups[0].Lines.Count);
    }

    [Fact]
    public void A_budget_council_has_not_adopted_is_a_draft_book()
    {
        Assert.True(Sources().Cover.IsDraft);
        Assert.False((Sources().Cover with { Status = BudgetStatus.Adopted }).IsDraft);
    }
}
