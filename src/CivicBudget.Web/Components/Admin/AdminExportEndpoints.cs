using CivicBudget.Application.Budgets;
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

        group.MapGet("/budgets/{versionId:guid}/lines.xlsx", async (Guid versionId, [FromServices] IBudgetEntryService entry, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
        {
            BudgetWorkspaceDto? workspace = await entry.GetWorkspaceAsync(versionId, ct);
            return workspace is null ? Results.NotFound() : File(exporter, ReportTables.Lines(workspace), $"budget-lines-fy{workspace.Version.Year}-{Slug(workspace.Version.Label)}");
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

        return group;
    }

    private static IResult File(ISpreadsheetExporter exporter, ExportTable table, string name) =>
        Results.File(exporter.ToXlsx(table), Xlsx, name + ".xlsx");

    private static string Slug(string text) => text.ToLowerInvariant().Replace(' ', '-');
}
