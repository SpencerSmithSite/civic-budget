using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.Accounts;
using CivicBudget.Infrastructure.Erp;
using CivicBudget.Infrastructure.Erp.Http;

namespace CivicBudget.ReferenceErp;

/// <summary>
/// A small ERP that implements CivicBudget's published API (docs/partners/openapi.json) over the demo
/// governments' fictional chart, books, payroll, and journal rules, which it borrows from the simulated
/// ERP. It exists for two readers: an ERP vendor, who can see every endpoint working end to end before
/// building their own, and CivicBudget's tests, which run the HTTP adapter against it.
///
/// It is a reference, not a product: one API key for every entity, and posted journals kept in memory.
/// A real ERP issues a key per government and checks it against the entity asked for.
/// </summary>
public static class ReferenceErpServer
{
    public const string KeySetting = "ReferenceErp:ApiKey";

    /// <summary>The entities it hosts, by the id a connection names (the demo governments' portal slugs).</summary>
    private static readonly Dictionary<string, ErpEntity> Entities = new(StringComparer.OrdinalIgnoreCase)
    {
        ["maple-ridge-oh"] = new(Guid.Parse("0199a000-0000-7000-8000-000000000001"), "maple-ridge-oh", "Village of Maple Ridge", 1, AccountNumberFormat.UanVillage),
        ["pine-hollow-twp-oh"] = new(Guid.Parse("0199a000-0000-7000-8000-000000000002"), "pine-hollow-twp-oh", "Pine Hollow Township", 7, new AccountNumberFormat(4, 3, 4, ".", "Department")),
    };

    public static WebApplication Build(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        string apiKey = builder.Configuration[KeySetting] is { Length: > 0 } key
            ? key
            : throw new InvalidOperationException($"Set {KeySetting} to the key CivicBudget will send (for example --{KeySetting}=local-test-key).");

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<SimulatedErpChartApi>();
        builder.Services.AddSingleton<SimulatedErpActualsApi>();
        builder.Services.AddSingleton<SimulatedErpEmployeesApi>();
        builder.Services.AddSingleton<SimulatedErpBudgetApi>();
        builder.Services.AddProblemDetails();

        WebApplication app = builder.Build();
        RouteGroupBuilder entity = app.MapGroup($"/{ErpApiContract.Version}/entities/{{entityId}}")
            .AddEndpointFilter((context, next) => HasKey(context.HttpContext.Request, apiKey)
                ? next(context)
                : ValueTask.FromResult<object?>(Results.Problem(title: "Unauthorized", detail: "Send the API key as a Bearer token.", statusCode: StatusCodes.Status401Unauthorized)));

        entity.MapGet("/chart", async (string entityId, SimulatedErpChartApi chart, CancellationToken ct) =>
            Find(entityId) is { } e ? Answer(await chart.FetchAsync(e, ct), ErpApiContract.FromContract) : UnknownEntity(entityId));

        entity.MapGet("/actuals/{fiscalYear:int}", async (string entityId, int fiscalYear, SimulatedErpActualsApi actuals, CancellationToken ct) =>
            Find(entityId) is { } e ? Answer(await actuals.FetchAsync(e, fiscalYear, ct), ErpApiContract.FromContract) : UnknownEntity(entityId));

        entity.MapGet("/employees", async (string entityId, SimulatedErpEmployeesApi employees, CancellationToken ct) =>
            Find(entityId) is { } e ? Answer(await employees.FetchAsync(e, ct), ErpApiContract.FromContract) : UnknownEntity(entityId));

        entity.MapPost("/budget-journals", async (string entityId, HttpRequest request, SimulatedErpBudgetApi journals, CancellationToken ct) =>
        {
            if (Find(entityId) is not { } e)
            {
                return UnknownEntity(entityId);
            }

            ApiBudgetJournal? journal;
            try
            {
                journal = await request.ReadFromJsonAsync<ApiBudgetJournal>(ErpApiContract.Json, ct);
            }
            catch (JsonException ex)
            {
                return Results.Problem(title: "Unreadable journal", detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }

            // The key a retry is recognized by must be the journal's own id, so a client cannot post
            // one journal twice by changing the header, or have two journals collide.
            if (journal is null || request.Headers[HttpErpAdapter.IdempotencyHeader] != journal.ExternalId.ToString())
            {
                return Results.Problem(title: "Missing idempotency key", detail: $"Send the journal's externalId in the {HttpErpAdapter.IdempotencyHeader} header.", statusCode: StatusCodes.Status400BadRequest);
            }

            ErpJournalAnswer answer = await journals.PostBudgetJournalAsync(e, ErpApiContract.ToContract(journal), ct);
            return Results.Json(ErpApiContract.FromContract(answer), ErpApiContract.Json,
                statusCode: answer.Posted ? StatusCodes.Status200OK : StatusCodes.Status422UnprocessableEntity);
        });

        return app;
    }

    private static ErpEntity? Find(string entityId) => Entities.GetValueOrDefault(entityId);

    private static IResult UnknownEntity(string entityId) =>
        Results.Problem(title: "Unknown entity", detail: $"This ERP has no entity '{entityId}'.", statusCode: StatusCodes.Status404NotFound);

    // The simulated ERP explains a year it has no books for as a failure; over HTTP that is a 404 with its words.
    private static IResult Answer<TContract, TApi>(Result<TContract> result, Func<TContract, TApi> toApi) =>
        result.IsSuccess
            ? Results.Json(toApi(result.Value), ErpApiContract.Json)
            : Results.Problem(title: "Not available", detail: result.Errors[0].Message, statusCode: StatusCodes.Status404NotFound);

    // Compared in constant time, so the time an answer takes says nothing about how much of a guess was right.
    private static bool HasKey(HttpRequest request, string apiKey)
    {
        string header = request.Headers.Authorization.ToString();
        const string Bearer = "Bearer ";
        if (!header.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] sent = SHA256.HashData(Encoding.UTF8.GetBytes(header[Bearer.Length..].Trim()));
        byte[] expected = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return CryptographicOperations.FixedTimeEquals(sent, expected);
    }
}
