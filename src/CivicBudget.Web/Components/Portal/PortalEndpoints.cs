using CivicBudget.Application.Export;
using CivicBudget.Application.Portal;
using Microsoft.AspNetCore.Mvc;

namespace CivicBudget.Web.Components.Portal;

internal static class PortalEndpoints
{
    /// <summary>
    /// Downloads of the published data. Minimal API endpoints rather than components because they
    /// return files, not HTML. Same read path as the pages (the portal context), same output cache.
    /// </summary>
    public static IEndpointConventionBuilder MapPortalEndpoints(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/transparency/{slug}/{year:int}");

        group.MapGet("/download.csv", async (string slug, int year, [FromServices] ISnapshotQueryService snapshots, CancellationToken ct) =>
        {
            ExportTable? table = await BuildTableAsync(snapshots, slug, year, ct);
            return table is null
                ? Results.NotFound()
                : Results.File(CsvWriter.ToCsv(table), "text/csv; charset=utf-8", $"{slug}-fy{year}-budget.csv");
        });

        group.MapGet("/download.xlsx", async (string slug, int year, [FromServices] ISnapshotQueryService snapshots, [FromServices] ISpreadsheetExporter exporter, CancellationToken ct) =>
        {
            ExportTable? table = await BuildTableAsync(snapshots, slug, year, ct);
            return table is null
                ? Results.NotFound()
                : Results.File(exporter.ToXlsx(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{slug}-fy{year}-budget.xlsx");
        });

        // The government's logo for the portal header. Public like the pages, versioned by the
        // upload time in the URL, and served only for governments with a published budget.
        endpoints.MapGet("/transparency/{slug}/logo", async (string slug, [FromServices] ISnapshotQueryService snapshots, CancellationToken ct) =>
        {
            PortalLogoDto? logo = await snapshots.GetLogoAsync(slug, ct);
            return logo is null ? Results.NotFound() : Results.Bytes(logo.Data, logo.ContentType, lastModified: logo.UpdatedAtUtc);
        });

        return group;
    }

    private static async Task<ExportTable?> BuildTableAsync(ISnapshotQueryService snapshots, string slug, int year, CancellationToken ct)
    {
        PortalBudgetDto? budget = await snapshots.GetBudgetAsync(slug, year, ct);
        if (budget is null)
        {
            return null;
        }

        IReadOnlyList<PortalLineDto> lines = await snapshots.GetLinesAsync(slug, year, ct);
        return new ExportTable(
            $"FY{year} {budget.VersionLabel}",
            ["Account number", "Fund code", "Fund", "Department code", "Department", "Account code", "Account", "Type", "Category", $"FY{year} budget", $"FY{year - 1} budget", $"FY{year - 2} actual"],
            lines.Select(l => new object?[]
            {
                l.AccountNumber, l.FundCode, l.FundName, l.DepartmentCode, l.DepartmentName, l.AccountCode, l.AccountName,
                CivicBudget.Web.Components.Common.Display.Enum(l.AccountType), CivicBudget.Web.Components.Common.Display.Enum(l.Category), l.Amount, l.CurrentYearBudget, l.PriorYearActual,
            }).ToList());
    }
}
