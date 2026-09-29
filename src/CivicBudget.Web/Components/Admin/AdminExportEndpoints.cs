using CivicBudget.Application.Budgets;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Export;
using CivicBudget.Application.Reports;
using CivicBudget.Application.Security;
using CivicBudget.Application.Setup;
using Microsoft.AspNetCore.Mvc;

namespace CivicBudget.Web.Components.Admin;

/// <summary>
/// XLSX downloads for the admin app: the workspace lines (in the import's layout), each report,
/// and the setup lists. Minimal API endpoints because they return files; they run under the same
/// cookie sign-in and policies as the pages, and <c>CurrentUserMiddleware</c> gives the services
/// the tenant, so the Application layer neither knows nor cares that a download called it.
/// </summary>
internal static class AdminExportEndpoints
{
    private const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public static IEndpointConventionBuilder MapAdminExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/admin/export").RequireAuthorization(Policies.CanViewBudget);

        // Every file that leaves is a security event: who took which export, and from where. One
        // filter on the group, so an export added later is logged without anyone remembering to.
        group.AddEndpointFilter(async (context, next) =>
        {
            object? result = await next(context);
            if (result is not Microsoft.AspNetCore.Http.HttpResults.NotFound)
            {
                ICurrentUser user = context.HttpContext.RequestServices.GetRequiredService<ICurrentUser>();
                await context.HttpContext.RequestServices.GetRequiredService<ISecurityEventLog>().RecordAsync(
                    Domain.Security.SecurityEventKind.DataExported, user.GovernmentId, user.UserId, context.HttpContext.User.Identity?.Name,
                    context.HttpContext.Request.Path.Value);
            }

            return result;
        });

        group.MapGet("/budgets/{versionId:guid}/lines.xlsx", async (Guid versionId, [FromServices] IBudgetEntryService entry, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
        {
            BudgetWorkspaceDto? workspace = await entry.GetWorkspaceAsync(versionId, ct);
            return workspace is null ? Results.NotFound() : File(exporter, ReportTables.Lines(workspace), $"budget-lines-fy{workspace.Version.Year}-{Slug(workspace.Version.Label)}");
        });

        group.MapGet("/budgets/{versionId:guid}/plan.xlsx", async (Guid versionId, [FromServices] IBudgetPlanService plans, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
        {
            BudgetPlanDto? plan = await plans.GetAsync(versionId, ct);
            return plan is null ? Results.NotFound() : File(exporter, ReportTables.Plan(plan), $"multi-year-plan-fy{plan.BudgetYear}-{Slug(plan.VersionLabel)}");
        });

        // The budget book with the sections chosen on its page; a section left out of the query uses the government's default.
        group.MapGet("/budgets/{versionId:guid}/book.pdf", async (Guid versionId, bool? outlook, bool? personnel, bool? lineItems, bool? glossary,
            [FromServices] IBudgetBookService books, CancellationToken ct) =>
        {
            BudgetBookOptions defaults = await books.GetDefaultsAsync(ct);
            var options = new BudgetBookOptions(outlook ?? defaults.Outlook, personnel ?? defaults.Personnel, lineItems ?? defaults.LineItems, glossary ?? defaults.Glossary);
            return await books.RenderAsync(versionId, options, ct) is { } book ? Results.File(book.Content, "application/pdf", book.FileName) : Results.NotFound();
        });

        group.MapGet("/reports/{versionId:guid}/fund-summary.xlsx", async (Guid versionId, [FromServices] IReportService reports, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
        {
            FundSummaryReportDto? report = await reports.FundSummaryAsync(versionId, ct);
            return report is null ? Results.NotFound() : File(exporter, ReportTables.FundSummary(report), $"fund-summary-{Slug(report.Header.Title)}");
        });

        group.MapGet("/reports/{versionId:guid}/department-detail.xlsx", async (Guid versionId, Guid? department, [FromServices] IReportService reports, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
        {
            DepartmentDetailReportDto? report = await reports.DepartmentDetailAsync(versionId, department, ct);
            return report is null ? Results.NotFound() : File(exporter, ReportTables.DepartmentDetail(report), $"department-detail-{Slug(report.Header.Title)}");
        });

        group.MapGet("/reports/{versionId:guid}/category.xlsx", async (Guid versionId, [FromServices] IReportService reports, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
        {
            CategoryReportDto? report = await reports.RevenueVsExpenditureAsync(versionId, ct);
            return report is null ? Results.NotFound() : File(exporter, ReportTables.RevenueVsExpenditure(report), $"revenue-vs-expenditure-{Slug(report.Header.Title)}");
        });

        // The certificate goes to the county budget commission, so it has a PDF as well as the spreadsheet.
        group.MapGet("/reports/{versionId:guid}/certificate.pdf", async (Guid versionId, [FromServices] ICertificateService certificates, [FromServices] ICertificatePdfRenderer pdf, CancellationToken ct) =>
            await certificates.GetAsync(versionId, ct) is { } certificate
                ? Results.File(pdf.Render(certificate), "application/pdf", $"certificate-{Slug(certificate.Header.GovernmentName)}-fy{certificate.Header.FiscalYear}-{Slug(certificate.Header.VersionLabel)}.pdf")
                : Results.NotFound());

        group.MapGet("/reports/{versionId:guid}/certificate.xlsx", async (Guid versionId, [FromServices] ICertificateService certificates, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
            await certificates.GetAsync(versionId, ct) is { } certificate
                ? File(exporter, ReportTables.Certificate(certificate), $"certificate-fy{certificate.Header.FiscalYear}-{Slug(certificate.Header.VersionLabel)}")
                : Results.NotFound());

        // The reports built on the ERP's books, and the appropriation measure. The service decides who gets which.
        group.MapGet("/reports/{versionId:guid}/budget-vs-actual.xlsx", async (Guid versionId, [FromServices] IActualsReportService reports, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
            await reports.BudgetVsActualAsync(versionId, ct) is { } r ? File(exporter, ReportTables.BudgetVsActual(r), $"budget-vs-actual-{Slug(r.Header.Title)}") : Results.NotFound());
        group.MapGet("/reports/{versionId:guid}/revenue-vs-receipts.xlsx", async (Guid versionId, [FromServices] IActualsReportService reports, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
            await reports.RevenueVsReceiptsAsync(versionId, ct) is { } r ? File(exporter, ReportTables.RevenueVsReceipts(r), $"revenue-vs-receipts-{Slug(r.Header.Title)}") : Results.NotFound());
        group.MapGet("/reports/{versionId:guid}/fund-projection.xlsx", async (Guid versionId, [FromServices] IActualsReportService reports, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
            await reports.FundProjectionAsync(versionId, ct) is { } r ? File(exporter, ReportTables.FundProjection(r), $"fund-projection-{Slug(r.Header.Title)}") : Results.NotFound());
        group.MapGet("/reports/{versionId:guid}/trends.xlsx", async (Guid versionId, [FromServices] IActualsReportService reports, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
            await reports.TrendsAsync(versionId, ct) is { } r ? File(exporter, ReportTables.Trends(r), $"trends-{Slug(r.Header.Title)}") : Results.NotFound());
        group.MapGet("/reports/{versionId:guid}/appropriation-measure.xlsx", async (Guid versionId, [FromServices] IActualsReportService reports, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
            await reports.AppropriationMeasureAsync(versionId, ct) is { } r ? File(exporter, ReportTables.AppropriationMeasure(r), $"appropriation-measure-{Slug(r.Header.Title)}") : Results.NotFound());

        // Personnel: the roster follows the department user's view; cost and benefits are whole-government.
        group.MapGet("/reports/{versionId:guid}/position-roster.xlsx", async (Guid versionId, [FromServices] IPersonnelReportService reports, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
            await reports.RosterAsync(versionId, ct) is { } r ? File(exporter, ReportTables.PositionRoster(r), $"position-roster-{Slug(r.Header.Title)}") : Results.NotFound());
        group.MapGet("/reports/{versionId:guid}/personnel-cost.xlsx", async (Guid versionId, [FromServices] IPersonnelReportService reports, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
            await reports.CostByFundAsync(versionId, ct) is { } r ? File(exporter, ReportTables.PersonnelCost(r), $"personnel-cost-{Slug(r.Header.Title)}") : Results.NotFound());
        group.MapGet("/reports/{versionId:guid}/benefits-summary.xlsx", async (Guid versionId, [FromServices] IPersonnelReportService reports, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
            await reports.BenefitsAsync(versionId, ct) is { } r ? File(exporter, ReportTables.BenefitsSummary(r), $"benefits-summary-{Slug(r.Header.Title)}") : Results.NotFound());

        // The ERP's import file for a budget journal: CSV, because that is what an ERP import reads.
        group.MapGet("/erp-journals/{transmissionId:guid}.csv", async (Guid transmissionId, [FromServices] IBudgetTransmissionService sends, CancellationToken ct) =>
            await sends.FileAsync(transmissionId, ct) is { } file
                ? Results.File(CsvWriter.ToCsv(file.Table), "text/csv", file.FileName)
                : Results.NotFound())
            .RequireAuthorization(Policies.CanSendToErp);

        RouteGroupBuilder setup = group.MapGroup("/setup").RequireAuthorization(Policies.CanMaintainSetup);

        setup.MapGet("/funds.xlsx", async ([FromServices] IFundService funds, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
            File(exporter, new ExportTable("Funds", ["Code", "Name", "Category", "Group", "Description", "Active"],
                (await funds.ListAsync(includeInactive: true, ct)).Select(f => new object?[] { f.Code, f.Name, f.Category.ToString(), f.Group.ToString(), f.Description, f.IsActive ? "Yes" : "No" }).ToList()), "funds"));

        setup.MapGet("/departments.xlsx", async ([FromServices] IDepartmentService departments, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
            File(exporter, new ExportTable("Departments", ["Code", "Name", "Description", "Active"],
                (await departments.ListAsync(includeInactive: true, ct)).Select(d => new object?[] { d.Code, d.Name, d.Description, d.IsActive ? "Yes" : "No" }).ToList()), "departments"));

        setup.MapGet("/accounts.xlsx", async ([FromServices] IAccountService accounts, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
            File(exporter, new ExportTable("Chart of accounts", ["Code", "Name", "Type", "Category", "Active"],
                (await accounts.ListAsync(includeInactive: true, ct)).Select(a => new object?[] { a.Code, a.Name, a.Type.ToString(), a.Category.ToString(), a.IsActive ? "Yes" : "No" }).ToList()), "chart-of-accounts"));

        // Everything the government has, one CSV file per table in a ZIP, for an Administrator. It is
        // under the export group, so it is logged and rate limited like every other file.
        group.MapGet("/government/all-data.zip", async ([FromServices] IGovernmentExportService export, CancellationToken ct) =>
                await export.ExportAsync(ct) is { } file ? Results.File(file.Content, "application/zip", file.FileName) : Results.NotFound())
            .RequireAuthorization(Policies.CanManageUsers);

        return group;
    }

    private static IResult File(ISpreadsheetExporter exporter, ExportTable table, string name) =>
        Results.File(exporter.ToXlsx(table), Xlsx, name + ".xlsx");

    private static string Slug(string text) => text.ToLowerInvariant().Replace(' ', '-');
}
