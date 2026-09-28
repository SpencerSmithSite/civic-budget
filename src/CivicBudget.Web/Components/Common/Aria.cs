namespace CivicBudget.Web.Components.Common;

/// <summary>
/// ARIA states as the strings screen readers expect. Blazor renders a C# bool attribute as an HTML
/// boolean attribute (true becomes an empty value, false removes it), so aria-expanded="@open"
/// never says "true" or "false". Write aria-expanded="@Aria.Bool(open)" instead.
/// </summary>
public static class Aria
{
    public static string Bool(bool value) => value ? "true" : "false";
}
