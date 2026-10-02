namespace CivicBudget.Web.Components.Layout;

/// <summary>
/// The sidebar's groups, which a user can fold away: which group a page belongs to, so the group
/// holding the page someone opens is never left folded, and the cookie that remembers the folded
/// ones so the server can prerender the menu the way the user left it.
/// </summary>
public static class SidebarGroups
{
    public const string Budget = "budget";
    public const string Setup = "setup";
    public const string Administration = "admin";
    public const string Portal = "portal";

    /// <summary>The cookie: the folded groups' keys joined with dots (cookie values may not hold commas).</summary>
    public const string Cookie = "cb.navfolded";

    private static readonly string[] All = [Budget, Setup, Administration, Portal];

    // The second segment of an admin address ("admin/funds/..." is "funds") decides its group.
    private static readonly Dictionary<string, string> BySection = new(StringComparer.OrdinalIgnoreCase)
    {
        ["my-department"] = Budget,
        ["budgets"] = Budget,
        ["reports"] = Budget,
        ["getting-started"] = Setup,
        ["funds"] = Setup,
        ["departments"] = Setup,
        ["accounts"] = Setup,
        ["fiscal-years"] = Setup,
        ["chart-sync"] = Setup,
        ["actuals-sync"] = Setup,
        ["report-settings"] = Setup,
        ["personnel-settings"] = Setup,
        ["personnel-sync"] = Setup,
        ["users"] = Administration,
        ["settings"] = Administration,
        ["outbox"] = Administration,
        ["security-log"] = Administration,
    };

    /// <summary>The group whose links lead to this page ("admin/funds/12" is Setup), or null for a page outside the menu.</summary>
    public static string? For(string relativePath)
    {
        string path = relativePath.Split('?', '#')[0].Trim('/');
        string[] segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || !segments[0].Equals("admin", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return segments.Length == 1 ? Budget : BySection.GetValueOrDefault(segments[1]);
    }

    /// <summary>The folded groups from the cookie; anything that is not a group's key is ignored.</summary>
    public static HashSet<string> Parse(string? cookie) =>
        [.. (cookie ?? "").Split('.', StringSplitOptions.RemoveEmptyEntries).Where(All.Contains)];

    public static string Format(IEnumerable<string> folded) => string.Join('.', All.Where(folded.Contains));
}
