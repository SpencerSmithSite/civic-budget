using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;

namespace CivicBudget.Web.Help;

/// <summary>One page the assistant can point to: its route template, what it is for, and who may open it.</summary>
/// <param name="Policies">Every policy the page's [Authorize] attributes name (a folder's and the page's own); empty for any signed-in user.</param>
public sealed record PageEntry(string Route, string Title, string Purpose, string Keywords, IReadOnlyList<string> Policies)
{
    /// <summary>Route values the page needs, e.g. VersionId; the assistant fills them from the budget tools.</summary>
    public IReadOnlyList<string> Parameters { get; } = [.. Regex.Matches(Route, @"\{(\w+)").Select(m => m.Groups[1].Value)];

    private readonly Regex _pattern = new("^" + Regex.Replace(Regex.Escape(Route), @"\\\{[^}]*}", "[^/]+") + "/?$", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));

    /// <summary>Whether a concrete path ("/admin/budgets/0199.../plan") is this page, ignoring a query string.</summary>
    public bool Matches(string path) => _pattern.IsMatch(path.Split('?', '#')[0]);
}

/// <summary>
/// Every page with a <see cref="HelpTopicAttribute"/>, read once from the app's own components: its
/// routes, its help text, and its <see cref="AuthorizeAttribute"/> policies. Built from the pages
/// themselves so it cannot fall behind them.
/// </summary>
public static class PageCatalog
{
    public static IReadOnlyList<PageEntry> Pages { get; } = Build();

    private static List<PageEntry> Build() =>
        [.. typeof(PageCatalog).Assembly.GetTypes()
            .Where(t => typeof(IComponent).IsAssignableFrom(t) && t.GetCustomAttribute<HelpTopicAttribute>() is not null)
            .SelectMany(t =>
            {
                HelpTopicAttribute help = t.GetCustomAttribute<HelpTopicAttribute>()!;
                List<string> policies = [.. t.GetCustomAttributes<AuthorizeAttribute>().Select(a => a.Policy).OfType<string>().Distinct()];
                return t.GetCustomAttributes<RouteAttribute>().Select(r => new PageEntry(r.Template, help.Title, help.Purpose, help.Keywords, policies));
            })
            .OrderBy(p => p.Route, StringComparer.Ordinal)];

    /// <summary>
    /// The pages that best match what someone asked for. A word in the title counts most, then a
    /// keyword (chosen on purpose), then the purpose's own words; two of the request's words together
    /// in a title or keywords ("user settings") count extra. Plain word overlap is enough for fifty
    /// pages, and it never surprises anyone.
    /// </summary>
    public static IReadOnlyList<PageEntry> Search(IEnumerable<PageEntry> pages, string topic, int take = 6)
    {
        string[] words = [.. Regex.Split(topic.ToLowerInvariant(), @"[^a-z0-9.]+").Where(w => w.Length > 2 && !StopWords.Contains(w))];
        if (words.Length == 0)
        {
            return [];
        }

        return [.. pages
            .Select(p => (Page: p, Score: Score(p, words)))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).ThenBy(x => x.Page.Parameters.Count)
            .Select(x => x.Page)
            .DistinctBy(p => p.Title)
            .Take(take)];
    }

    private static int Score(PageEntry page, string[] words)
    {
        string title = page.Title.ToLowerInvariant(), keywords = page.Keywords.ToLowerInvariant(), purpose = page.Purpose.ToLowerInvariant();
        int score = words.Sum(w => (title.Contains(w, StringComparison.Ordinal) ? 3 : 0)
            + (keywords.Contains(w, StringComparison.Ordinal) ? 2 : 0)
            + (purpose.Contains(w, StringComparison.Ordinal) ? 1 : 0));
        for (int i = 0; i + 1 < words.Length; i++)
        {
            string pair = words[i] + " " + words[i + 1];
            score += title.Contains(pair, StringComparison.Ordinal) || keywords.Contains(pair, StringComparison.Ordinal) ? 4 : 0;
        }

        return score;
    }

    private static readonly HashSet<string> StopWords =
        ["the", "and", "how", "can", "where", "what", "run", "need", "want", "for", "from", "into", "about", "get", "show", "find", "use",
         "our", "with", "this", "that", "page", "report", "bring", "update", "change", "see", "open", "make", "please", "would", "like"];
}
