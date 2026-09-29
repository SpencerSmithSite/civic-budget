using System.Text.Json;
using Microsoft.Extensions.AI;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// A stand-in model: its first answer asks for the scripted tools; once it has their results it
/// answers in words. It keeps the system prompt and what each tool returned for the test to read.
/// </summary>
internal sealed class ScriptedModel(params (string Name, Dictionary<string, object?> Arguments)[] calls) : IChatClient
{
    public string SystemPrompt { get; private set; } = "";

    public List<string> ToolResults { get; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        List<ChatMessage> list = [.. messages];
        SystemPrompt = list.First(m => m.Role == ChatRole.System).Text;
        List<FunctionResultContent> results = [.. list.SelectMany(m => m.Contents).OfType<FunctionResultContent>()];
        if (results.Count == 0 && calls.Length > 0)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant,
                [.. calls.Select((c, i) => (AIContent)new FunctionCallContent($"call-{i}", c.Name, c.Arguments))])));
        }

        ToolResults.AddRange(results.Select(r => r.Result is JsonElement json ? json.GetRawText() : JsonSerializer.Serialize(r.Result)));
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "Here is what I found.")));
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
