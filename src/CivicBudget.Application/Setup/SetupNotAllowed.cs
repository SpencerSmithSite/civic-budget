namespace CivicBudget.Application.Setup;

/// <summary>
/// The setup services check the caller themselves, like every other service that writes. The pages
/// carry the same rule as an [Authorize] policy, but a page attribute is one typo away from letting a
/// Viewer create funds, and nothing else would stop it.
/// </summary>
internal static class SetupNotAllowed
{
    public const string FiscalAuthority = "Only an Administrator or the Fiscal Officer can change the chart of accounts and fiscal years.";
    public const string Admin = "Only an Administrator can change the government's settings.";
}
