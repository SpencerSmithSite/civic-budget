using CivicBudget.Application.Notifications;
using Microsoft.AspNetCore.Components;

namespace CivicBudget.Web.Security;

/// <summary>
/// Absolute links for emails. A configured public address wins ("App:PublicUrl", for a deployment
/// behind a proxy or on several hostnames); otherwise the address the page is being served from,
/// which Blazor knows on a static page and on a circuit alike.
/// </summary>
public sealed class AppLinks(NavigationManager navigation, IConfiguration configuration) : IAppLinks
{
    public string Absolute(string relativePath) =>
        Base().TrimEnd('/') + "/" + relativePath.TrimStart('/');

    private string Base()
    {
        if (configuration["App:PublicUrl"] is { Length: > 0 } configured)
        {
            return configured;
        }

        try
        {
            return navigation.BaseUri;
        }
        catch (InvalidOperationException)
        {
            // Outside a page (an endpoint with no Blazor render): there is no address to borrow.
            return "http://localhost/";
        }
    }
}
