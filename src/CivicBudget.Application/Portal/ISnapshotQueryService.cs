namespace CivicBudget.Application.Portal;

/// <summary>
/// Everything the public portal reads. Implemented in Infrastructure over <c>PublicPortalDbContext</c>,
/// which maps only the snapshot tables and filters to Active, so nothing here can reach a draft.
/// A null return means "no published budget matches": the page renders a friendly not-found.
/// </summary>
public interface ISnapshotQueryService
{
    /// <summary>Governments with at least one published budget (for the portal root).</summary>
    Task<IReadOnlyList<(string Slug, string Name)>> ListGovernmentsAsync(CancellationToken ct = default);

    /// <summary>The header for a year, or the latest published year when <paramref name="fiscalYear"/> is null.</summary>
    Task<PortalBudgetDto?> GetBudgetAsync(string slug, int? fiscalYear, CancellationToken ct = default);

    Task<BreakdownDto?> ExpendituresByFundAsync(string slug, int fiscalYear, CancellationToken ct = default);
    Task<BreakdownDto?> RevenuesByCategoryAsync(string slug, int fiscalYear, CancellationToken ct = default);
    Task<BreakdownDto?> ExpendituresByCategoryAsync(string slug, int fiscalYear, CancellationToken ct = default);
    Task<BreakdownDto?> ExpendituresByDepartmentAsync(string slug, int fiscalYear, CancellationToken ct = default);

    Task<PortalFundDto?> GetFundAsync(string slug, int fiscalYear, string fundCode, CancellationToken ct = default);
    Task<PortalDepartmentDto?> GetDepartmentAsync(string slug, int fiscalYear, string fundCode, string departmentCode, CancellationToken ct = default);

    /// <summary>Every line of the year, for the download and the full account table.</summary>
    Task<IReadOnlyList<PortalLineDto>> GetLinesAsync(string slug, int fiscalYear, CancellationToken ct = default);

    Task<IReadOnlyList<YearTotalsDto>> YearOverYearAsync(string slug, CancellationToken ct = default);

    Task<IReadOnlyList<PortalSearchHitDto>> SearchAsync(string slug, int fiscalYear, string query, CancellationToken ct = default);

    /// <summary>The government's uploaded logo, or null when it uses the CivicBudget mark. Only for governments with a published budget.</summary>
    Task<PortalLogoDto?> GetLogoAsync(string slug, CancellationToken ct = default);
}
