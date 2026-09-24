namespace CivicBudget.Web.Components.Account;

/// <summary>
/// Whether a return URL stays on this site. <c>Uri.IsWellFormedUriString(url, UriKind.Relative)</c>
/// is not enough: it accepts "//evil.example", which browsers treat as another host, so a crafted
/// sign-in link could send someone off the site straight after they type their password. The rules
/// follow ASP.NET Core's <c>IUrlHelper.IsLocalUrl</c>, plus bare relative paths like "admin" that
/// the Account pages use.
/// </summary>
public static class LocalUrl
{
    public static bool IsLocal(string? url)
    {
        // Control characters (a smuggled CR/LF) are never part of a URL we mean to follow.
        if (string.IsNullOrEmpty(url) || url.Any(char.IsControl))
        {
            return false;
        }

        if (url[0] == '/')
        {
            // "/" alone, or "/path" but never "//host" or "/\host".
            return url.Length == 1 || (url[1] != '/' && url[1] != '\\');
        }

        if (url.StartsWith("~/", StringComparison.Ordinal))
        {
            return url.Length == 2 || (url[2] != '/' && url[2] != '\\');
        }

        // A bare relative path ("admin", "Account/Lockout"): no scheme and no backslash tricks.
        return url[0] != '\\' && !url.Contains(':', StringComparison.Ordinal);
    }
}
