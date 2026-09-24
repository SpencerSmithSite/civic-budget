namespace CivicBudget.Web;

/// <summary>
/// Whether a request is for a static file (styles, scripts, fonts, images) that middleware in front
/// of the app should let through untouched. "Has a dot in it" is not the test: exports such as
/// <c>/admin/export/.../lines.xlsx</c> and the portal's <c>download.csv</c> have dots too, and they
/// are data, not assets.
/// </summary>
public static class StaticAssetPath
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".css", ".js", ".mjs", ".map", ".svg", ".png", ".jpg", ".jpeg", ".webp", ".gif", ".ico",
        ".woff", ".woff2", ".ttf",
    };

    public static bool IsStaticAsset(PathString path) =>
        path.HasValue && Extensions.Contains(Path.GetExtension(path.Value.AsSpan()).ToString());
}
