using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CivicBudget.Application.Common;
using CivicBudget.Application.Erp;
using CivicBudget.Application.Export;
using CivicBudget.Domain.Accounts;
using CivicBudget.Domain.Funds;
using CivicBudget.Domain.Personnel;
using CivicBudget.Infrastructure.Erp.Http;
using CivicBudget.Infrastructure.Export;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The partner kit is a promise, so these tests hold its three parts to the code: every schema in
/// docs/partners/openapi.json has exactly the fields and required fields of its record, every
/// enumeration lists exactly the values CivicBudget accepts, and every sample file reads cleanly
/// through the code that reads the real thing. A field added on one side and not the other fails here.
/// No database is needed.
/// </summary>
public class ErpApiContractTests
{
    private static readonly string Partners = Path.Combine(RepoRoot(), "docs", "partners");

    private static readonly Dictionary<string, Type> Enums = new()
    {
        ["FundCategory"] = typeof(FundCategory),
        ["AccountType"] = typeof(AccountType),
        ["ReportingCategory"] = typeof(ReportingCategory),
        ["PayBasis"] = typeof(PayBasis),
        ["CoverageTier"] = typeof(CoverageTier),
    };

    // Stricter than CivicBudget itself, which ignores fields it does not know: a sample must not
    // teach a vendor a field that does not exist.
    private static readonly JsonSerializerOptions Strict = new(ErpApiContract.Json) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };

    private static readonly JsonElement Schemas = OpenApi().GetProperty("components").GetProperty("schemas");

    public static TheoryData<string> WireRecords()
    {
        var data = new TheoryData<string>();
        foreach (Type type in ApiTypes())
        {
            data.Add(type.Name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(WireRecords))]
    public void Each_schema_has_exactly_its_records_fields_and_required_fields(string typeName)
    {
        Type type = ApiTypes().Single(t => t.Name == typeName);
        Assert.True(Schemas.TryGetProperty(typeName["Api".Length..], out JsonElement schema), $"openapi.json has no schema for {typeName}.");
        ParameterInfo[] fields = type.GetConstructors().Single().GetParameters();

        string[] names = [.. fields.Select(f => JsonNamingPolicy.CamelCase.ConvertName(f.Name!)).Order()];
        string[] documented = [.. schema.GetProperty("properties").EnumerateObject().Select(p => p.Name).Order()];
        Assert.Equal(names, documented);

        string[] required = [.. fields.Where(f => !f.HasDefaultValue).Select(f => JsonNamingPolicy.CamelCase.ConvertName(f.Name!)).Order()];
        string[] documentedRequired = schema.TryGetProperty("required", out JsonElement r) ? [.. r.EnumerateArray().Select(x => x.GetString()!).Order()] : [];
        Assert.Equal(required, documentedRequired);

        // A field that may be null says so in its type ("type": ["string", "null"]); one that may not, does not.
        var nullability = new NullabilityInfoContext();
        foreach (ParameterInfo field in fields)
        {
            JsonElement property = schema.GetProperty("properties").GetProperty(JsonNamingPolicy.CamelCase.ConvertName(field.Name!));
            if (property.TryGetProperty("type", out JsonElement t))
            {
                bool documentedNull = t.ValueKind == JsonValueKind.Array && t.EnumerateArray().Any(x => x.GetString() == "null");
                bool nullable = nullability.Create(field).WriteState == NullabilityState.Nullable;
                Assert.True(nullable == documentedNull, $"{typeName}.{field.Name}: nullable in code is {nullable}, in openapi.json {documentedNull}.");
            }
        }
    }

    [Fact]
    public void Each_enumeration_lists_exactly_the_values_CivicBudget_accepts()
    {
        foreach ((string name, Type type) in Enums)
        {
            string[] documented = [.. Schemas.GetProperty(name).GetProperty("enum").EnumerateArray().Select(v => v.GetString()!)];
            Assert.Equal(Enum.GetNames(type), documented);
        }
    }

    [Fact]
    public void The_document_describes_nothing_the_code_does_not_have_and_every_reference_resolves()
    {
        string[] expected = [.. ApiTypes().Select(t => t.Name["Api".Length..]).Concat(Enums.Keys).Order()];
        Assert.Equal(expected, Schemas.EnumerateObject().Select(s => s.Name).Order());

        string text = File.ReadAllText(Path.Combine(Partners, "openapi.json"));
        foreach (string reference in System.Text.RegularExpressions.Regex.Matches(text, "#/components/schemas/(\\w+)").Select(m => m.Groups[1].Value).Distinct())
        {
            Assert.True(Schemas.TryGetProperty(reference, out _), $"$ref to missing schema {reference}.");
        }

        Assert.EndsWith($"/{ErpApiContract.Version}", OpenApi().GetProperty("servers")[0].GetProperty("url").GetString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("chart.json", typeof(ApiChart))]
    [InlineData("actuals.json", typeof(ApiActuals))]
    [InlineData("employees.json", typeof(ApiEmployees))]
    [InlineData("budget-journal.json", typeof(ApiBudgetJournal))]
    [InlineData("journal-posted.json", typeof(ApiJournalResult))]
    [InlineData("journal-refused.json", typeof(ApiJournalResult))]
    [InlineData("problem.json", typeof(ApiProblem))]
    public void Each_json_sample_reads_under_the_published_rules_and_uses_only_published_fields(string file, Type type)
    {
        object? read = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(Partners, "samples", file)), type, Strict);

        Assert.NotNull(read);
    }

    [Fact]
    public void The_json_samples_translate_into_CivicBudgets_contracts()
    {
        ErpChart chart = ErpApiContract.ToContract(Sample<ApiChart>("chart.json"));
        ErpActuals actuals = ErpApiContract.ToContract(Sample<ApiActuals>("actuals.json"));
        ErpEmployees roster = ErpApiContract.ToContract(Sample<ApiEmployees>("employees.json"));

        Assert.Equal(AccountNumberFormat.UanVillage, chart.NumberFormat);
        Assert.Contains(chart.Objects, o => o.Code == "5110" && o.Type == AccountType.Expenditure && o.Category == ReportingCategory.PersonalServices);
        Assert.Equal(12, actuals.ThroughPeriod);
        Assert.Contains(actuals.Activity, a => a.DepartmentCode is null && a.ObjectCode == "4110");
        Assert.Contains(roster.Employees, e => e.Grade == "SGT" && e.Step == 3);
    }

    [Fact]
    public void A_field_left_out_is_refused_rather_than_read_as_zero()
    {
        const string NoAmount = """{"fundCode":"1000","objectCode":"4110","period":3}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApiActivity>(NoAmount, ErpApiContract.Json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ApiFund>("""{"code":"1000","name":"General","category":1,"active":true}""", ErpApiContract.Json)); // enums by name only
    }

    [Fact]
    public void Each_csv_sample_reads_through_the_same_code_as_an_upload()
    {
        var reader = new ClosedXmlSpreadsheetReader();

        Result<ErpChart> chart = new ErpChartFileSource(reader).Read("chart.csv", SampleStream("chart.csv"));
        Result<ErpActuals> actuals = new ErpActualsFileSource(reader).Read("actuals.csv", SampleStream("actuals.csv"), AccountNumberFormat.UanVillage);
        Result<ErpEmployees> roster = new ErpEmployeeFileSource(reader).Read("employees.csv", SampleStream("employees.csv"), new DateOnly(2026, 9, 11));

        Assert.True(chart.IsSuccess, Errors(chart));
        Assert.Equal((2, 2, 3), (chart.Value.Funds.Count, chart.Value.Departments.Count, chart.Value.Objects.Count));
        Assert.True(actuals.IsSuccess, Errors(actuals));
        Assert.Equal((4, 1, 1), (actuals.Value.Activity.Count, actuals.Value.Encumbrances.Count, actuals.Value.Cash.Count));
        Assert.True(roster.IsSuccess, Errors(roster));
        Assert.Equal([60m, 40m], roster.Value.Employees.Single(e => e.EmployeeId == "E3002").Funds.Select(f => f.Percent).OrderDescending());
    }

    [Fact]
    public void The_journal_csv_sample_is_exactly_what_CivicBudget_writes()
    {
        ErpBudgetJournal journal = ErpApiContract.ToContract(Sample<ApiBudgetJournal>("budget-journal.json"));

        string written = Encoding.UTF8.GetString(CsvWriter.ToCsv(BudgetJournalFile.ToTable(journal)));

        Assert.Equal(File.ReadAllText(Path.Combine(Partners, "samples", "budget-journal.csv")).ReplaceLineEndings("\r\n"), written.TrimStart('﻿'));
    }

    private static IEnumerable<Type> ApiTypes() =>
        typeof(ApiChart).Assembly.GetTypes().Where(t => t.Namespace == typeof(ApiChart).Namespace && t.IsPublic && t.Name.StartsWith("Api", StringComparison.Ordinal));

    private static T Sample<T>(string file) => JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(Partners, "samples", file)), ErpApiContract.Json)!;

    private static MemoryStream SampleStream(string file) => new(File.ReadAllBytes(Path.Combine(Partners, "samples", file)));

    private static string Errors<T>(Result<T> result) => string.Join("; ", result.Errors.Select(e => e.Message));

    private static JsonElement OpenApi() => JsonDocument.Parse(File.ReadAllText(Path.Combine(Partners, "openapi.json"))).RootElement;

    private static string RepoRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CivicBudget.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Could not find the repository root (CivicBudget.slnx).");
    }
}
