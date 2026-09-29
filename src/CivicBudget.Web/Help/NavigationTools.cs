using System.ComponentModel;
using System.Security.Claims;
using CivicBudget.Application.Assistant;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.AI;

namespace CivicBudget.Web.Help;

/// <summary>
/// The assistant's way around the app: find the pages that fit what someone wants to do, and open
/// one. Both ask the same authorization policies the pages use, so it only ever offers or opens a
/// page the user could open from the menu.
/// </summary>
public sealed class NavigationTools(AuthenticationStateProvider authentication, IAuthorizationService authorization) : IAssistantToolProvider
{
    public IEnumerable<AIFunction> Tools(AssistantTurn turn)
    {
        yield return AIFunctionFactory.Create(
            async ([Description("What the user wants to do or find, in their words: 'print the budget book', 'change my password', 'amended certificate'.")] string topic, CancellationToken ct) =>
            {
                IReadOnlyList<PageEntry> pages = PageCatalog.Search(await AllowedAsync(), topic);
                turn.Steps.Add(new AssistantStep($"Looked for pages about \"{topic}\""));
                return pages.Select(p => new
                {
                    title = p.Title,
                    purpose = p.Purpose,
                    path = p.Route,
                    fillIn = p.Parameters.Count == 0 ? null : $"Replace {string.Join(" and ", p.Parameters.Select(x => "{" + x + "}"))} with ids: VersionId from list_budget_versions; others are reached from their list page.",
                });
            },
            "find_pages",
            "Finds the pages of CivicBudget this user can open that fit a task, with what each is for and its path. Use it to answer 'how do I...' and 'where is...' questions.");

        yield return AIFunctionFactory.Create(
            async ([Description("The page's path with every {placeholder} filled in, e.g. /admin/budgets/0199.../book.")] string path, CancellationToken ct) =>
            {
                string normalized = "/" + path.Trim().TrimStart('/');
                if (normalized.Contains('{', StringComparison.Ordinal))
                {
                    return (object)new { problem = "Fill in the placeholders first (a budget version's id comes from list_budget_versions)." };
                }

                if ((await AllowedAsync()).FirstOrDefault(p => p.Matches(normalized)) is not { } page)
                {
                    return new { problem = "That is not a page this user can open." };
                }

                turn.NavigateTo = normalized;
                turn.Steps.Add(new AssistantStep($"Opened {page.Title}"));
                return new { opened = page.Title };
            },
            "open_page",
            "Opens a page for the user, when they ask to go somewhere. The path must be one find_pages or another tool gave, with its placeholders filled in.");
    }

    private async Task<List<PageEntry>> AllowedAsync()
    {
        ClaimsPrincipal user = (await authentication.GetAuthenticationStateAsync()).User;
        var allowed = new List<PageEntry>();
        foreach (PageEntry page in PageCatalog.Pages)
        {
            bool mayOpen = user.Identity?.IsAuthenticated == true;
            foreach (string policy in page.Policies)
            {
                mayOpen = mayOpen && (await authorization.AuthorizeAsync(user, policy)).Succeeded;
            }

            if (mayOpen)
            {
                allowed.Add(page);
            }
        }

        return allowed;
    }
}
