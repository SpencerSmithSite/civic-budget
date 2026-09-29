using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CivicBudget.Infrastructure.Erp.Http;

/// <summary>
/// Talks to any ERP that implements the published API (docs/partners/openapi.json), so a vendor
/// connects by building four endpoints on their side, with no code added to CivicBudget. One adapter
/// serves every government; <see cref="ErpConnectionsOptions"/> says which governments have a
/// connection, where it is, and with which key.
///
/// The three reads turn every failure into a message for the page. The journal post is different:
/// when the ERP clearly refused, it says so, but when the answer is lost or the ERP fails on its side
/// nobody knows whether the journal posted, so it throws and the send stays open to retry under the
/// same id (ADR-0035).
/// </summary>
public sealed partial class HttpErpAdapter(
    IHttpClientFactory clients,
    IOptionsMonitor<ErpConnectionsOptions> options,
    ILogger<HttpErpAdapter> logger) : IErpChartApi, IErpActualsApi, IErpEmployeesApi, IErpBudgetApi
{
    public const string ClientName = "erp";

    /// <summary>The header an ERP uses to recognize a journal it has already posted.</summary>
    public const string IdempotencyHeader = "Idempotency-Key";

    public string Name => "ERP";

    public bool IsConnected(Guid governmentId) => options.CurrentValue.For(governmentId) is not null;

    public async Task<Result<ErpChart>> FetchAsync(ErpEntity entity, CancellationToken ct = default)
    {
        Result<ApiChart> read = await GetAsync<ApiChart>(entity, "chart", ct);
        return read.IsFailure ? Result.Failure<ErpChart>(read.Errors) : Translate(() => ErpApiContract.ToContract(read.Value));
    }

    public async Task<Result<ErpActuals>> FetchAsync(ErpEntity entity, int fiscalYear, CancellationToken ct = default)
    {
        Result<ApiActuals> read = await GetAsync<ApiActuals>(entity, $"actuals/{fiscalYear}", ct);
        if (read.IsFailure)
        {
            return Result.Failure<ErpActuals>(read.Errors);
        }

        // An answer for another year would be filed under the one asked for; refuse it instead.
        return read.Value.FiscalYear == fiscalYear
            ? Translate(() => ErpApiContract.ToContract(read.Value))
            : Result.Failure<ErpActuals>($"The ERP answered with FY{read.Value.FiscalYear} when FY{fiscalYear} was asked for.");
    }

    async Task<Result<ErpEmployees>> IErpEmployeesApi.FetchAsync(ErpEntity entity, CancellationToken ct)
    {
        Result<ApiEmployees> read = await GetAsync<ApiEmployees>(entity, "employees", ct);
        return read.IsFailure ? Result.Failure<ErpEmployees>(read.Errors) : Translate(() => ErpApiContract.ToContract(read.Value));
    }

    public async Task<ErpJournalAnswer> PostBudgetJournalAsync(ErpEntity entity, ErpBudgetJournal journal, CancellationToken ct = default)
    {
        if (options.CurrentValue.For(entity.GovernmentId) is not { } connection)
        {
            return ErpJournalAnswer.Refused(new Dictionary<string, string>(), NotConnected);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, Url(connection, entity, "budget-journals"))
        {
            Content = JsonContent.Create(ErpApiContract.FromContract(journal), options: ErpApiContract.Json),
        };
        Authorize(request, connection);
        request.Headers.Add(IdempotencyHeader, journal.ExternalId.ToString());

        // Exceptions from here (no answer, a timeout) go to the caller on purpose: see the class summary.
        using HttpResponseMessage response = await Client().SendAsync(request, ct);
        LogAnswer(logger, "POST", "budget-journals", entity.GovernmentId, (int)response.StatusCode);

        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created or HttpStatusCode.UnprocessableEntity)
        {
            ApiJournalResult result = await ReadAsync<ApiJournalResult>(response, ct)
                ?? throw new HttpRequestException("The ERP answered without a body.");
            if (result.Posted)
            {
                return ErpJournalAnswer.Accepted(result.JournalNumber
                    ?? throw new HttpRequestException("The ERP said the journal posted but gave no journal number."));
            }

            return ErpJournalAnswer.Refused(
                (result.Refused ?? []).ToDictionary(r => r.Account, r => r.Reason, StringComparer.OrdinalIgnoreCase),
                result.Message ?? "The ERP refused the journal.");
        }

        // The ERP turned the request down before posting anything: a bad key, an unknown entity, a
        // journal it could not read. Those are certain refusals, so they are not retried blindly.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return ErpJournalAnswer.Refused(new Dictionary<string, string>(), await ProblemAsync(response, entity, ct));
        }

        throw new HttpRequestException($"The ERP answered {(int)response.StatusCode} {response.ReasonPhrase}.", null, response.StatusCode);
    }

    private const string NotConnected = "No ERP connection is set up for this government.";

    private async Task<Result<T>> GetAsync<T>(ErpEntity entity, string path, CancellationToken ct)
        where T : class
    {
        if (options.CurrentValue.For(entity.GovernmentId) is not { } connection)
        {
            return Result.Failure<T>(NotConnected);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, Url(connection, entity, path));
        Authorize(request, connection);
        try
        {
            using HttpResponseMessage response = await Client().SendAsync(request, ct);
            LogAnswer(logger, "GET", path, entity.GovernmentId, (int)response.StatusCode);
            if (!response.IsSuccessStatusCode)
            {
                return Result.Failure<T>(await ProblemAsync(response, entity, ct));
            }

            return await ReadAsync<T>(response, ct) is { } body
                ? Result.Success(body)
                : Result.Failure<T>("The ERP answered without a body.");
        }
        catch (HttpRequestException ex)
        {
            LogUnreachable(logger, path, entity.GovernmentId, ex.Message);
            return Result.Failure<T>("The ERP could not be reached. Try again later, or upload an export file instead.");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            LogUnreachable(logger, path, entity.GovernmentId, "timed out");
            return Result.Failure<T>($"The ERP did not answer within {options.CurrentValue.TimeoutSeconds} seconds. Try again later, or upload an export file instead.");
        }
        catch (JsonException ex)
        {
            return Result.Failure<T>($"The ERP's answer is not in the published form: {ex.Message}");
        }
    }

    private static async Task<T?> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct) =>
        await response.Content.ReadFromJsonAsync<T>(ErpApiContract.Json, ct);

    // A code the ERP sends that CivicBudget's rules refuse (a segment wider than the format allows)
    // is a problem with the answer, not a crash.
    private static Result<T> Translate<T>(Func<T> translate)
    {
        try
        {
            return Result.Success(translate());
        }
        catch (DomainException ex)
        {
            return Result.Failure<T>($"The ERP's answer could not be used: {ex.Message}");
        }
    }

    private static async Task<string> ProblemAsync(HttpResponseMessage response, ErpEntity entity, CancellationToken ct)
    {
        ApiProblem? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<ApiProblem>(ErpApiContract.Json, ct);
        }
        catch (JsonException)
        {
            // Not a problem body; fall back to the status below.
        }

        string? said = problem?.Detail ?? problem?.Title;
        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "The ERP did not accept CivicBudget's key for this government. Ask the operator to check the connection.",
            HttpStatusCode.NotFound when said is null => $"The ERP does not know {entity.Name}. Ask the operator to check the connection's entity id.",
            _ => said ?? $"The ERP answered {(int)response.StatusCode} {response.ReasonPhrase}.",
        };
    }

    private HttpClient Client()
    {
        HttpClient client = clients.CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(options.CurrentValue.TimeoutSeconds);
        return client;
    }

    private static Uri Url(ErpConnection connection, ErpEntity entity, string path) =>
        new($"{connection.BaseUrl.TrimEnd('/')}/{ErpApiContract.Version}/entities/{Uri.EscapeDataString(connection.EntityId ?? entity.Slug)}/{path}");

    private static void Authorize(HttpRequestMessage request, ErpConnection connection) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey);

    // The key and the bodies are never logged: the bodies are the government's books and payroll.
    [LoggerMessage(Level = LogLevel.Information, Message = "ERP {Method} {Path} for government {GovernmentId} answered {Status}")]
    private static partial void LogAnswer(ILogger logger, string method, string path, Guid governmentId, int status);

    [LoggerMessage(Level = LogLevel.Warning, Message = "ERP {Path} for government {GovernmentId} failed: {Reason}")]
    private static partial void LogUnreachable(ILogger logger, string path, Guid governmentId, string reason);
}
