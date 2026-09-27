using System.Text;
using CivicBudget.Application.Reports;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;
using CivicBudget.Infrastructure.Reports;

namespace CivicBudget.Application.Tests.Reports;

/// <summary>
/// The certificate of estimated resources: the carryover arithmetic, the revenue columns, the fund
/// type subtotals, the reconciliations, the revenue changes an amended certificate explains, the
/// settings that would count money twice, and the PDF.
/// </summary>
public class CertificateBuilderTests
{
    private static readonly Guid General = Guid.NewGuid(), Street = Guid.NewGuid(), Water = Guid.NewGuid();
    private static readonly Guid RealEstate = Guid.NewGuid(), IncomeTax = Guid.NewGuid(), GasTax = Guid.NewGuid(), Charges = Guid.NewGuid(), TransferIn = Guid.NewGuid();

    private static readonly CertificateHeaderDto Header = new(Guid.NewGuid(), "Village of Maple Ridge", "Harmon", 2026, "Amendment 1", 2, BudgetStatus.Adopted, "2026-11",
        new DateTimeOffset(2026, 6, 15, 23, 30, 0, TimeSpan.Zero), "Dana Whitfield", "Fiscal Officer", "Dana", new DateTimeOffset(2026, 9, 27, 18, 0, 0, TimeSpan.Zero));

    private static CertificateResourceLine Revenue(Guid fund, Guid account, string number, decimal amount, AccountType type = AccountType.Revenue, string? why = null) =>
        new(fund, null, account, number, "Account " + number, type, amount, why);

    private static readonly List<CertificateResourceLine> Resources =
    [
        Revenue(General, RealEstate, "1000-4110", 410_000m),
        Revenue(General, IncomeTax, "1000-4130", 1_250_000m),
        Revenue(General, Charges, "1000-4320", 15_000m),
        Revenue(Street, GasTax, "2011-4220", 245_000m),
        Revenue(Street, TransferIn, "2011-4910", 20_000m, AccountType.TransferIn),
        Revenue(Water, Charges, "5101-4320", 720_000m),
    ];

    private static readonly List<CertificateFund> Funds =
    [
        new(General, "1000", "General Fund", FundCategory.General, 620_000m, 2_000_000m),
        new(Street, "2011", "Street", FundCategory.SpecialRevenue, 82_000m, 400_000m),   // over: 82,000 + 265,000 available
        new(Water, "5101", "Water", FundCategory.Enterprise, 372_000m, 600_000m),
    ];

    private static CertificateInputs Inputs(CertificateCarryover? carryover = null, Dictionary<Guid, CertificateAdjustment>? adjustments = null,
        IReadOnlyList<CertificateColumn>? columns = null, string? priorLabel = null, IReadOnlyList<CertificateResourceLine>? prior = null) => new(
        Header, "Unencumbered Balance 1/1", "Other Sources",
        columns ?? [new CertificateColumn("Taxes", new HashSet<Guid> { RealEstate, IncomeTax })],
        DefaultColumns: false, Funds, Resources, new DateOnly(2026, 1, 1), carryover,
        adjustments ?? [], priorLabel, prior ?? []);

    private static readonly CertificateCarryover ClosedYear = new(new DateOnly(2025, 12, 31),
        new Dictionary<Guid, decimal> { [General] = 632_200m, [Street] = 86_207m, [Water] = 378_755m },
        new Dictionary<Guid, decimal> { [General] = 12_200m, [Street] = 4_207m, [Water] = 6_755m });

    [Fact]
    public void Carryover_is_cash_less_encumbrances_reserves_and_nonspendable_plus_or_minus_unpaid_advances()
    {
        CertificateReportDto c = CertificateBuilder.Build(Inputs(ClosedYear, new()
        {
            [General] = new CertificateAdjustment(Nonspendable: 5_000m, Reserves: 10_000m, UnpaidAdvances: 25_000m), // lent 25,000 to Street
            [Street] = new CertificateAdjustment(0m, 0m, -25_000m),
        }));

        CertificateRowDto general = c.Funds.Single(f => f.FundCode == "1000");
        Assert.Equal(632_200m - 12_200m - 5_000m - 10_000m + 25_000m, general.Carryover);
        Assert.Equal([1_660_000m], general.RevenueColumns);                      // real estate and income tax
        Assert.Equal(15_000m, general.OtherSources);
        Assert.Equal(general.Carryover + 1_675_000m, general.TotalAvailable);
        Assert.Equal(82_000m - 25_000m, c.Funds.Single(f => f.FundCode == "2011").Carryover);
        Assert.True(c.CarryoverFromErp);
        Assert.Equal(new DateOnly(2025, 12, 31), c.CarryoverAsOf);
    }

    [Fact]
    public void Funds_are_grouped_by_type_with_subtotals_and_a_grand_total()
    {
        CertificateReportDto c = CertificateBuilder.Build(Inputs(ClosedYear));

        Assert.Equal(["General", "Special Revenue", "Enterprise"], c.Sections.Select(s => s.Label));
        Assert.Equal(("Total Special Revenue", 265_000m), (c.Sections[1].Subtotal.FundName, c.Sections[1].Subtotal.EstimatedRevenue));
        Assert.Equal(620_000m + 82_000m + 372_000m, c.Total.Carryover);
        Assert.Equal(Resources.Sum(r => r.Amount), c.Total.EstimatedRevenue);
        Assert.Equal(20_000m + 245_000m, c.Funds.Single(f => f.FundCode == "2011").OtherSources);   // a transfer in is another source
    }

    [Fact]
    public void The_checks_catch_a_fund_over_its_total_and_a_budget_that_starts_from_another_balance()
    {
        var drifted = new CertificateCarryover(ClosedYear.AsOf,
            new Dictionary<Guid, decimal>(ClosedYear.Cash) { [Water] = 400_000m }, ClosedYear.Encumbrances);

        CertificateReportDto c = CertificateBuilder.Build(Inputs(drifted));

        CertificateCheckDto limit = c.Checks.Single(k => k.Label.StartsWith("Appropriations stay within", StringComparison.Ordinal));
        Assert.False(limit.Passed);
        Assert.Contains("2011 Street appropriates $53,000.00 more than its $347,000.00", limit.Detail, StringComparison.Ordinal);
        CertificateCheckDto balance = c.Checks.Single(k => k.Label.StartsWith("The budget starts", StringComparison.Ordinal));
        Assert.False(balance.Passed);
        Assert.Contains("5101 Water: the budget starts with $372,000.00, the certificate certifies $393,245.00", balance.Detail, StringComparison.Ordinal);
        Assert.True(c.Checks.Single(k => k.Label.EndsWith("= estimated revenue", StringComparison.Ordinal)).Passed);
    }

    [Fact]
    public void Before_the_erp_closes_the_prior_year_the_budgets_estimate_is_the_balance_and_the_report_says_so()
    {
        CertificateReportDto c = CertificateBuilder.Build(Inputs(adjustments: new() { [General] = new CertificateAdjustment(0m, 10_000m, 0m) }));

        Assert.False(c.CarryoverFromErp);
        Assert.Equal(620_000m, c.Funds.Single(f => f.FundCode == "1000").Carryover);   // the adjustment is not applied twice
        Assert.Null(c.Funds.Single(f => f.FundCode == "1000").Cash);
        Assert.Contains(c.Notes, n => n.Contains("has not closed the year ending December 31, 2025", StringComparison.Ordinal));
        Assert.Contains(c.Notes, n => n.Contains("apply once the carryover comes from the ERP", StringComparison.Ordinal));
        Assert.DoesNotContain(c.Checks, k => k.Label.StartsWith("The budget starts", StringComparison.Ordinal)); // nothing to reconcile against yet
    }

    [Fact]
    public void Several_columns_split_the_taxes_and_an_empty_one_is_flagged()
    {
        CertificateReportDto c = CertificateBuilder.Build(Inputs(ClosedYear, columns:
        [
            new CertificateColumn("Real estate taxes", new HashSet<Guid> { RealEstate }),
            new CertificateColumn("Local taxes", new HashSet<Guid> { IncomeTax }),
            new CertificateColumn("Levies", new HashSet<Guid>()),
        ]));

        Assert.Equal(["Real estate taxes", "Local taxes", "Levies"], c.RevenueColumnLabels);
        Assert.Equal([410_000m, 1_250_000m, 0m], c.Funds.Single(f => f.FundCode == "1000").RevenueColumns);
        CertificateCheckDto columns = c.Checks.Single(k => k.Label == "Every revenue column has accounts");
        Assert.False(columns.Passed);
        Assert.Contains("Levies has no accounts", columns.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_amended_certificate_lists_every_revenue_estimate_that_moved_with_its_reason()
    {
        List<CertificateResourceLine> prior = [.. Resources.Where(r => r.AccountId != Charges || r.FundId != General), Revenue(General, Charges, "1000-4320", 12_000m)];
        var now = Resources.Select(r => r.AccountId == IncomeTax ? r with { Amount = 1_300_000m, Justification = "Two new employers" } : r).ToList();

        CertificateReportDto c = CertificateBuilder.Build(Inputs(ClosedYear, priorLabel: "FY2026 Original", prior: prior) with { Resources = now });

        Assert.Equal(
            [("1000-4130", 1_250_000m, 1_300_000m, "Two new employers"), ("1000-4320", 12_000m, 15_000m, (string?)null)],
            c.Changes.Select(x => (x.AccountNumber, x.Prior, x.Now, x.Justification)));
        Assert.Equal("Amended Certificate of Estimated Resources No. 1", c.Header.Title);
    }

    [Fact]
    public void Settings_that_would_count_money_twice_or_not_at_all_are_refused()
    {
        var revenue = new HashSet<Guid> { RealEstate, IncomeTax };
        List<string> problems = CertificateService.Problems(new SaveCertificateSettingsRequest(null, null, "Fiscal Officer", "Balance", "Taxes",
        [
            new ReportColumnDto("Taxes", [RealEstate, IncomeTax]),
            new ReportColumnDto("Local", [IncomeTax, TransferIn]),
            new ReportColumnDto(" ", []), new ReportColumnDto("D", []), new ReportColumnDto("E", []),
        ]), revenue);

        Assert.Contains(problems, p => p.StartsWith("A report has room for 4 columns", StringComparison.Ordinal));
        Assert.Contains("Every column needs a heading.", problems);
        Assert.Contains("Local: only revenue accounts can be in this column.", problems);
        Assert.Contains("1 account is in more than one column, which would count the money twice.", problems);
        Assert.Contains(CertificateService.Problems(new SaveCertificateSettingsRequest(null, null, "FO", "Balance", "taxes", [new ReportColumnDto("Taxes", [RealEstate])]), revenue),
            p => p.Contains("Two columns are headed", StringComparison.Ordinal));
    }

    [Fact]
    public void The_pdf_is_two_landscape_letter_pages_in_the_embedded_typeface()
    {
        byte[] pdf = new CertificatePdfRenderer().Render(CertificateBuilder.Build(Inputs(ClosedYear, priorLabel: "FY2026 Original", prior: Resources)));

        string text = Encoding.Latin1.GetString(pdf);
        Assert.StartsWith("%PDF-", text, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(text, @"/Type\s*/Page\b"));
        Assert.Matches(@"/MediaBox\s*\[0 0 792 612\]", text);                       // 11 x 8.5 inches at 72 points
        Assert.Contains("/BaseFont/", text, StringComparison.Ordinal);
        Assert.Contains("Source#20Sans#203", text, StringComparison.Ordinal);                // the embedded typeface, a subset with its PDF name escaped
    }
}
