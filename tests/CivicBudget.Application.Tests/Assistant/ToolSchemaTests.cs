using System.Text.Json;
using CivicBudget.Application.Assistant;
using Microsoft.Extensions.AI;

namespace CivicBudget.Application.Tests.Assistant;

/// <summary>
/// What each tool tells the model it must send. A parameter the model may leave out has to be
/// optional in the schema; a nullable parameter without a default is marked required, and a model
/// that leaves it out gets an error instead of an answer (found by running a real model).
/// </summary>
public class ToolSchemaTests
{
    private static readonly Dictionary<string, string[]> Required = new()
    {
        ["list_budget_versions"] = [],
        ["fund_summary"] = [],
        ["budget_vs_actual"] = [],
        ["revenue_vs_receipts"] = [],
        ["fund_projection"] = [],
        ["department_budget"] = [],
        ["search_budget_lines"] = ["text"],
        ["multi_year_plan"] = [],
        ["certificate"] = [],
        ["check_budget"] = [],
        ["download_file"] = ["file"],
        ["propose_multi_year_plan"] = ["years", "revenuePercent", "expenditurePercent"],
        ["propose_line_changes"] = [],
        ["propose_fund_fix"] = ["fundCode"],
        ["propose_start_budget"] = ["fiscalYear"],
        ["propose_fetch_actuals"] = [],
        ["propose_budget_message"] = ["body"],
        ["propose_department_narrative"] = ["departmentCode", "narrative"],
    };

    [Fact]
    public void Only_what_a_tool_cannot_do_without_is_required()
    {
        // Building the tools reads no service, so the dependencies can be left out.
        var turn = new AssistantTurn(null);
        List<AIFunction> tools =
        [
            .. new BudgetTools(null!, null!, null!, null!, null!, null!).Tools(turn),
            .. new ActionTools(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!).Tools(turn),
        ];

        Assert.Equal(Required.Keys.Order(), tools.Select(t => t.Name).Order());
        foreach (AIFunction tool in tools)
        {
            string[] required = tool.JsonSchema.TryGetProperty("required", out JsonElement r) ? [.. r.EnumerateArray().Select(x => x.GetString()!)] : [];
            Assert.True(Required[tool.Name].SequenceEqual(required), $"{tool.Name} requires [{string.Join(", ", required)}]");
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), tool.Name);
        }
    }
}
