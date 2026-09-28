namespace CivicBudget.Application.Security;

/// <summary>A government's full data as a ZIP of CSV files.</summary>
public sealed record GovernmentExportFile(string FileName, byte[] Content);

/// <summary>
/// "Download everything" for an Administrator: every table the government has, one CSV file each,
/// so a government can take its data with it or keep its own copy. Null for anyone else. The file is
/// built in memory; a government's budget data is a few megabytes at most.
/// </summary>
public interface IGovernmentExportService
{
    Task<GovernmentExportFile?> ExportAsync(CancellationToken ct = default);
}
