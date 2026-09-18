using CivicBudget.Application.Portal;

namespace CivicBudget.Web.Components.Portal.Common;

/// <summary>What the portal layout needs from the URL: which government and year the page is about.</summary>
public sealed record PortalRoute(string Slug, int? Year)
{
    public string Root => Year is { } y ? $"/transparency/{Slug}/{y}" : $"/transparency/{Slug}";
}

public static class PortalRoutes
{
    /// <summary>Parses "/transparency/{slug}/{year?}/..." from the current URI. Null outside the portal or at its root list.</summary>
    public static PortalRoute? Parse(Uri uri)
    {
        string[] segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !segments[0].Equals("transparency", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int? year = segments.Length >= 3 && int.TryParse(segments[2], out int y) ? y : null;
        return new PortalRoute(segments[1].ToLowerInvariant(), year);
    }
}
