using System.Text.Json;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Domain.Accounts;
using CivicBudget.Infrastructure;
using CivicBudget.Infrastructure.Erp;
using CivicBudget.Infrastructure.Erp.Http;
using CivicBudget.ReferenceErp;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The HTTP adapter against the reference ERP, over real HTTP on this machine: all four exchanges
/// arrive exactly as the ERP holds them, a journal posts once however often it is sent, and each way
/// a connection can go wrong ends in a message a Fiscal Officer can act on, or (for a journal whose
/// fate is unknown) an exception the send service turns into a safe retry. No database is needed.
/// </summary>
public sealed class HttpErpAdapterTests : IAsyncLifetime
{
    private const string Key = "test-key-for-the-reference-erp";
    private static readonly Guid MapleId = Guid.CreateVersion7();
    private static readonly ErpEntity Maple = new(MapleId, "maple-ridge-oh", "Village of Maple Ridge", 1, AccountNumberFormat.UanVillage);

    private WebApplication _erp = null!;
    private string _url = "";

    public async Task InitializeAsync()
    {
        _erp = ReferenceErpServer.Build(["--urls", "http://127.0.0.1:0", $"--{ReferenceErpServer.KeySetting}={Key}"]);
        await _erp.StartAsync();
        _url = _erp.Urls.First();
    }

    public async Task DisposeAsync()
    {
        await _erp.StopAsync();
        await _erp.DisposeAsync();
    }

    private HttpErpAdapter Adapter(string? baseUrl = null, string key = Key, string? entityId = null) =>
        Provider(new Dictionary<string, string?>
        {
            [$"Erp:Connections:{MapleId}:BaseUrl"] = baseUrl ?? _url,
            [$"Erp:Connections:{MapleId}:ApiKey"] = key,
            [$"Erp:Connections:{MapleId}:EntityId"] = entityId,
        }).GetRequiredService<HttpErpAdapter>();

    private static ServiceProvider Provider(Dictionary<string, string?> settings) =>
        new ServiceCollection().AddLogging()
            .AddErpConnections(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider();

    // Records holding lists compare by reference, so the two sides are compared as JSON.
    private static string Json(object value) => JsonSerializer.Serialize(value);

    [Fact]
    public async Task Chart_actuals_and_employees_arrive_exactly_as_the_ERP_holds_them()
    {
        HttpErpAdapter adapter = Adapter();

        Result<ErpChart> chart = await ((IErpChartApi)adapter).FetchAsync(Maple);
        Result<ErpActuals> actuals = await ((IErpActualsApi)adapter).FetchAsync(Maple, 2025);
        Result<ErpEmployees> roster = await ((IErpEmployeesApi)adapter).FetchAsync(Maple);

        Assert.True(chart.IsSuccess && actuals.IsSuccess && roster.IsSuccess);
        Assert.Equal(Json((await new SimulatedErpChartApi().FetchAsync(Maple)).Value), Json(chart.Value));
        Assert.Equal(Json((await new SimulatedErpActualsApi(TimeProvider.System).FetchAsync(Maple, 2025)).Value), Json(actuals.Value));
        Assert.Equal(Json((await new SimulatedErpEmployeesApi(TimeProvider.System).FetchAsync(Maple)).Value), Json(roster.Value));
    }

    [Fact]
    public async Task A_journal_posts_once_however_often_it_is_sent()
    {
        HttpErpAdapter adapter = Adapter();
        var journal = new ErpBudgetJournal(Guid.CreateVersion7(), 2027, "FY2027 Original, resolution 2026-50", new DateOnly(2027, 1, 1),
            [new ErpBudgetJournalLine("1000-110-5110", 578_966.80m), new ErpBudgetJournalLine("1000-4110", 437_081m)]);

        ErpJournalAnswer first = await adapter.PostBudgetJournalAsync(Maple, journal);
        ErpJournalAnswer retry = await adapter.PostBudgetJournalAsync(Maple, journal);
        ErpJournalAnswer another = await adapter.PostBudgetJournalAsync(Maple, journal with { ExternalId = Guid.CreateVersion7() });

        Assert.True(first.Posted);
        Assert.Equal(first.JournalNumber, retry.JournalNumber);
        Assert.NotEqual(first.JournalNumber, another.JournalNumber);
    }

    [Fact]
    public async Task A_journal_naming_an_account_the_ERP_lacks_is_refused_whole_with_the_account()
    {
        var journal = new ErpBudgetJournal(Guid.CreateVersion7(), 2027, "FY2027 Amendment 1", new DateOnly(2027, 6, 15),
            [new ErpBudgetJournalLine("1000-110-5110", -12_000m), new ErpBudgetJournalLine("1000-110-5990", 12_000m)]);

        ErpJournalAnswer answer = await Adapter().PostBudgetJournalAsync(Maple, journal);

        Assert.False(answer.Posted);
        Assert.Equal(["1000-110-5990"], answer.RefusedAccounts.Keys);
        Assert.Contains("posted nothing", answer.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_key_is_explained_and_posts_nothing()
    {
        HttpErpAdapter adapter = Adapter(key: "not-the-key");

        Result<ErpChart> chart = await ((IErpChartApi)adapter).FetchAsync(Maple);
        ErpJournalAnswer answer = await adapter.PostBudgetJournalAsync(Maple, new ErpBudgetJournal(Guid.CreateVersion7(), 2027, "x", new DateOnly(2027, 1, 1), [new ErpBudgetJournalLine("1000-4110", 1m)]));

        Assert.Contains("did not accept CivicBudget's key", chart.Errors.Single().Message, StringComparison.Ordinal);
        Assert.False(answer.Posted);
        Assert.Contains("did not accept CivicBudget's key", answer.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_ERPs_own_words_reach_the_user_for_an_unknown_entity_or_a_year_without_books()
    {
        Result<ErpChart> unknown = await ((IErpChartApi)Adapter(entityId: "somewhere-else")).FetchAsync(Maple);
        Result<ErpActuals> noBooks = await ((IErpActualsApi)Adapter()).FetchAsync(Maple, 2031);

        Assert.Equal("This ERP has no entity 'somewhere-else'.", unknown.Errors.Single().Message);
        Assert.Equal("The ERP has no books for FY2031.", noBooks.Errors.Single().Message);
    }

    [Fact]
    public async Task Only_the_governments_the_operator_connected_can_use_it()
    {
        HttpErpAdapter adapter = Adapter();
        ErpEntity other = Maple with { GovernmentId = Guid.CreateVersion7() };

        Assert.True(adapter.IsConnected(MapleId));
        Assert.False(adapter.IsConnected(other.GovernmentId));
        Assert.Equal("No ERP connection is set up for this government.", (await ((IErpChartApi)adapter).FetchAsync(other)).Errors.Single().Message);
    }

    [Fact]
    public void A_connection_is_found_whether_its_id_was_pasted_in_capitals_or_not()
    {
        ServiceProvider provider = Provider(new Dictionary<string, string?>
        {
            [$"Erp:Connections:{MapleId.ToString().ToUpperInvariant()}:BaseUrl"] = "https://erp.example.com",
            [$"Erp:Connections:{MapleId.ToString().ToUpperInvariant()}:ApiKey"] = Key,
        });

        Assert.True(provider.GetRequiredService<HttpErpAdapter>().IsConnected(MapleId));
    }

    [Fact]
    public async Task An_ERP_that_does_not_answer_fails_a_read_gently_but_leaves_a_journal_open_to_retry()
    {
        // Nothing listens on port 9 (discard) on a test machine, so the connection is refused at once.
        HttpErpAdapter adapter = Adapter(baseUrl: "http://127.0.0.1:9");

        Result<ErpChart> chart = await ((IErpChartApi)adapter).FetchAsync(Maple);

        Assert.Contains("could not be reached", chart.Errors.Single().Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<HttpRequestException>(() => adapter.PostBudgetJournalAsync(Maple,
            new ErpBudgetJournal(Guid.CreateVersion7(), 2027, "x", new DateOnly(2027, 1, 1), [new ErpBudgetJournalLine("1000-4110", 1m)])));
    }

    [Theory]
    [InlineData("not-a-guid", "https://erp.example.com", "key", "government's id")]
    [InlineData(null, "http://erp.example.com", "key", "must use HTTPS")]
    [InlineData(null, "erp.example.com", "key", "absolute address")]
    [InlineData(null, "https://erp.example.com", "", "ApiKey is required")]
    public void A_connection_that_could_not_work_or_would_leak_its_key_stops_the_app(string? id, string url, string key, string expected)
    {
        string at = id ?? Guid.CreateVersion7().ToString();
        ServiceProvider provider = Provider(new Dictionary<string, string?>
        {
            [$"Erp:Connections:{at}:BaseUrl"] = url,
            [$"Erp:Connections:{at}:ApiKey"] = key,
        });

        OptionsValidationException refused = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ErpConnectionsOptions>>().Value);

        Assert.Contains(refused.Failures, f => f.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Plain_http_is_allowed_only_to_this_machine()
    {
        Assert.True(new ErpConnectionsOptionsValidator().Validate(null, new ErpConnectionsOptions
        {
            Connections = { [MapleId.ToString()] = new ErpConnection { BaseUrl = "http://localhost:5090", ApiKey = "k" } },
        }).Succeeded);
    }
}
