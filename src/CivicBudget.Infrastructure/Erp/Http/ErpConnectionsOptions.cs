using System.Net;
using Microsoft.Extensions.Options;

namespace CivicBudget.Infrastructure.Erp.Http;

/// <summary>
/// Each government's connection to its ERP, set by the operator (user-secrets locally, Secrets Manager
/// or Key Vault in the cloud) and never stored in the database or shown on a page:
/// <code>
/// Erp:Connections:{government id}:BaseUrl    https://erp.example.com/civicbudget
/// Erp:Connections:{government id}:ApiKey     the key the ERP issued for this government
/// Erp:Connections:{government id}:EntityId   the ERP's own id for the government (optional; the portal slug otherwise)
/// </code>
/// Keyed by the government's id rather than its slug, because an Administrator can change the slug.
/// </summary>
public sealed class ErpConnectionsOptions
{
    public const string SectionName = "Erp";

    public Dictionary<string, ErpConnection> Connections { get; set; } = [];

    /// <summary>How long to wait for the ERP before giving up; a year's actuals can take a while to assemble.</summary>
    public int TimeoutSeconds { get; set; } = 60;

    // Compared as ids, not text: SQL Server shows an id in capitals and .NET writes it in lower case,
    // and an operator will paste whichever they have in front of them.
    public ErpConnection? For(Guid governmentId) =>
        Connections.FirstOrDefault(c => Guid.TryParse(c.Key, out Guid id) && id == governmentId).Value;
}

public sealed class ErpConnection
{
    public string BaseUrl { get; set; } = "";

    public string ApiKey { get; set; } = "";

    public string? EntityId { get; set; }
}

/// <summary>
/// Refuses to start with a connection that could not work or would leak its key: an id that is not a
/// government id, a missing key, or an address that is not HTTPS (plain HTTP only to this machine, for
/// the reference ERP). Failing at startup beats a Fiscal Officer finding out on the Fetch button.
/// </summary>
public sealed class ErpConnectionsOptionsValidator : IValidateOptions<ErpConnectionsOptions>
{
    public ValidateOptionsResult Validate(string? name, ErpConnectionsOptions options)
    {
        var failures = new List<string>();
        if (options.TimeoutSeconds is < 5 or > 600)
        {
            failures.Add("Erp:TimeoutSeconds must be between 5 and 600.");
        }

        foreach ((string key, ErpConnection connection) in options.Connections)
        {
            string at = $"Erp:Connections:{key}";
            if (!Guid.TryParse(key, out _))
            {
                failures.Add($"{at}: the key must be the government's id (a GUID).");
            }

            if (string.IsNullOrWhiteSpace(connection.ApiKey))
            {
                failures.Add($"{at}:ApiKey is required.");
            }

            if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out Uri? url))
            {
                failures.Add($"{at}:BaseUrl must be an absolute address.");
            }
            else if (url.Scheme != Uri.UriSchemeHttps && !(url.Scheme == Uri.UriSchemeHttp && IsThisMachine(url)))
            {
                failures.Add($"{at}:BaseUrl must use HTTPS (plain HTTP is allowed only to localhost).");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static bool IsThisMachine(Uri url) =>
        url.IsLoopback || (IPAddress.TryParse(url.Host, out IPAddress? address) && IPAddress.IsLoopback(address));
}
