using Bunit.TestDoubles;
using CivicBudget.Application.Common;
using CivicBudget.Application.Reports;
using CivicBudget.Application.Security;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Budgets;
using CivicBudget.Domain.Funds;
using CivicBudget.Web.Components.Admin.Reports;
using CivicBudget.Web.Components.Common;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.Web.Tests.Reports;

/// <summary>
/// The certificate page shows the issued form and the detailed schedule from the same rows, with
/// the reconciliations; the settings page previews the columns and never lets one account sit in two.
/// </summary>
public class CertificatePagesTests : BunitContext
{
    private static readonly Guid VersionId = Guid.CreateVersion7();
    private static readonly Guid RealEstate = Guid.NewGuid(), IncomeTax = Guid.NewGuid();
    private readonly FakeCertificates _certificates = new();
    private readonly BunitAuthorizationContext _auth;

    public CertificatePagesTests()
    {
        Services.AddSingleton<ICertificateService>(_certificates);
        Services.AddSingleton<IMeasureColumnService>(new FakeMeasureColumns());
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<AdminPageState>();
        _auth = AddAuthorization();
        _auth.SetAuthorized("dana");
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    private static CertificateRowDto Row(string code, string name, decimal carryover, decimal taxes, decimal other, decimal appropriations) =>
        new(code, name, FundCategory.General, carryover + 10m, 10m, 0m, 0m, 0m, carryover, [taxes], other, appropriations, carryover);

    private static CertificateReportDto Certificate(bool over)
    {
        CertificateRowDto general = Row("1000", "General Fund", 620_000m, 1_709_800m, 226_600m, over ? 3_000_000m : 1_711_573m);
        return new CertificateReportDto(
            new CertificateHeaderDto(VersionId, "Village of Maple Ridge", "Harmon", 2026, "Amendment 1", 2, BudgetStatus.Adopted, "2026-11", null, "Dana Whitfield", "Fiscal Officer", "Dana", DateTimeOffset.UtcNow),
            "Unencumbered Balance 1/1", ["Taxes"], "Other Sources", true, new DateOnly(2025, 12, 31),
            [new CertificateSectionDto("General", [general], general with { FundCode = "", FundName = "Total General" })],
            general with { FundCode = "", FundName = "All funds" },
            [new CertificateCheckDto("Appropriations stay within each fund's total available (ORC 5705.39)", !over, over ? "1000 General Fund appropriates too much." : "Every fund.")],
            "FY2026 Original", [new RevenueChangeDto("1000", "1000-4130", "Municipal Income Tax", 1_250_000m, 1_287_500m, "Two new employers")], []);
    }

    [Fact]
    public async Task The_certificate_shows_the_issued_form_then_the_detailed_schedule_from_the_same_rows()
    {
        _certificates.Report = Certificate(over: false);

        IRenderedComponent<CertificateReport> page = Render<CertificateReport>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Contains("Amended Certificate of Estimated Resources No. 1", page.Find("h1").TextContent));
        AngleSharp.Dom.IElement issued = page.Find("table[aria-label='Certificate as issued']");
        Assert.Equal(["Fund", "Unencumbered Balance 1/1", "Taxes", "Other Sources", "Total"], issued.QuerySelectorAll("thead th").Select(th => th.TextContent));
        Assert.Contains("2,556,400.00", issued.TextContent);
        Assert.Contains("Harmon County", page.Find(".cb-cert-doc").TextContent);
        Assert.Contains("Two new employers", page.Find("table[aria-label='Revenue changes']").TextContent);
        Assert.Equal("admin/export/reports/" + VersionId + "/certificate.pdf", page.Find("a:contains('Download PDF')").GetAttribute("href"));

        await page.Find("button:contains('Detailed schedule')").ClickAsync(new());

        AngleSharp.Dom.IElement detailed = page.Find("table[aria-label='Detailed schedule']");
        Assert.Contains("620,010.00", detailed.TextContent);                          // cash, before carried encumbrances
        Assert.Empty(page.FindAll("table[aria-label='Certificate as issued']"));
    }

    [Fact]
    public void A_failed_reconciliation_stands_out()
    {
        _certificates.Report = Certificate(over: true);

        IRenderedComponent<CertificateReport> page = Render<CertificateReport>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Single(page.FindAll(".cb-checks li.bad")));
        Assert.Contains("needs attention", page.Find(".cb-checks li.bad").TextContent);
    }

    [Fact]
    public void A_department_user_is_told_the_certificate_is_not_theirs_to_run()
    {
        _auth.SetRoles(Roles.DepartmentHead);
        _certificates.Report = null;

        IRenderedComponent<CertificateReport> page = Render<CertificateReport>(p => p.Add(x => x.VersionId, VersionId));

        page.WaitForAssertion(() => Assert.Contains("Not available for a department", page.Markup));
    }

    [Fact]
    public async Task Settings_preview_the_columns_and_an_account_can_only_be_in_one()
    {
        _certificates.Settings = new CertificateSettingsDto("Harmon", "Dana Whitfield", "Fiscal Officer", "Unencumbered Balance 1/1", "Other Sources",
            [new ReportColumnDto("Taxes", [RealEstate])], false,
            [new ReportAccountDto(RealEstate, "4110", "Real Estate Taxes", ReportingCategory.Taxes, true), new ReportAccountDto(IncomeTax, "4130", "Municipal Income Tax", ReportingCategory.Taxes, true)]);

        IRenderedComponent<ReportSettings> page = Render<ReportSettings>();
        page.WaitForAssertion(() => Assert.Contains("Taxes", page.Find(".cb-cert-preview").TextContent));

        await page.Find("button:contains('Add column')").ClickAsync(new());
        await page.Find("#column1").InputAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "Local taxes" });

        Assert.Equal(["Fund", "Unencumbered Balance 1/1", "Taxes", "Local taxes", "Other Sources", "Total"], page.FindAll(".cb-cert-preview > span").Select(s => s.TextContent));
        Assert.True(page.Find($"#c1-{RealEstate:N}").HasAttribute("disabled"));     // already in Taxes
        Assert.Contains("(in Taxes)", page.Find($"label[for='c1-{RealEstate:N}']").TextContent);

        await page.Find($"#c1-{IncomeTax:N}").ChangeAsync(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = true });
        await page.Find("button:contains('Save settings')").ClickAsync(new());

        Assert.Equal([("Taxes", new[] { RealEstate }), ("Local taxes", new[] { IncomeTax })],
            _certificates.Saved!.Columns.Select(c => (c.Label, c.AccountIds.ToArray())));
    }

    private sealed class FakeMeasureColumns : IMeasureColumnService
    {
        public Task<MeasureColumnsDto> GetAsync(CancellationToken ct = default) =>
            Task.FromResult(new MeasureColumnsDto([new ReportColumnDto("Personal services", [])], true, []));

        public Task<Result> SaveAsync(IReadOnlyList<ReportColumnDto> columns, CancellationToken ct = default) => Task.FromResult(Result.Success());
    }

    private sealed class FakeCertificates : ICertificateService
    {
        public CertificateReportDto? Report { get; set; }
        public CertificateSettingsDto? Settings { get; set; }
        public SaveCertificateSettingsRequest? Saved { get; private set; }

        public Task<CertificateReportDto?> GetAsync(Guid budgetVersionId, CancellationToken ct = default) => Task.FromResult(Report);
        public Task<CertificateSettingsDto> GetSettingsAsync(CancellationToken ct = default) => Task.FromResult(Settings!);

        public Task<Result> SaveSettingsAsync(SaveCertificateSettingsRequest request, CancellationToken ct = default)
        {
            Saved = request;
            return Task.FromResult(Result.Success());
        }

        public Task<IReadOnlyList<FundAdjustmentDto>?> GetAdjustmentsAsync(Guid budgetVersionId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Result> SaveAdjustmentsAsync(SaveFundAdjustmentsRequest request, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
