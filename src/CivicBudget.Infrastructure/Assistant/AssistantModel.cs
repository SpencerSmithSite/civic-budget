using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CivicBudget.Infrastructure.Assistant;

/// <summary>
/// The model behind the assistant, set by the operator (user-secrets locally, the secret store in the
/// cloud); never on a page or in the database:
/// <code>
/// Assistant:ApiKey     the provider's key; without it the assistant is simply not there
/// Assistant:Provider   Anthropic (the default) or Ollama
/// Assistant:Model      the model id; claude-sonnet-5-5 by default for Anthropic, required for Ollama
/// Assistant:BaseUrl    optional; another address for the provider, such as a local Ollama
/// </code>
/// </summary>
public sealed class AssistantModelOptions
{
    public const string SectionName = "Assistant";
    public const string Anthropic = "Anthropic";
    public const string Ollama = "Ollama";
    public const string DefaultAnthropicModel = "claude-sonnet-5-5";
    public const string OllamaCloud = "https://ollama.com";

    public string? ApiKey { get; set; }

    public string Provider { get; set; } = Anthropic;

    public string? Model { get; set; }

    public string? BaseUrl { get; set; }

    /// <summary>Tool calls the model may chain for one question before it must answer.</summary>
    public int MaxToolRounds { get; set; } = 8;
}

public static class AssistantModelRegistration
{
    /// <summary>
    /// Registers the assistant's <see cref="IChatClient"/> when a key is configured. The Application
    /// layer sees only the interface, so the provider is decided here and nothing else changes.
    /// Both providers go through Anthropic's SDK: Ollama (on its cloud or a local server) speaks
    /// Anthropic's Messages API, tools included, and wants the key as a Bearer token. Function
    /// invocation is the layer that runs the tools the model asks for, capped so a confused model
    /// cannot loop. A setting that cannot work stops the app at startup.
    /// </summary>
    public static IServiceCollection AddAssistantModel(this IServiceCollection services, IConfiguration configuration)
    {
        AssistantModelOptions options = configuration.GetSection(AssistantModelOptions.SectionName).Get<AssistantModelOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return services;
        }

        TimeSpan timeout = TimeSpan.FromSeconds(90);
        (AnthropicClient client, string model) = options.Provider switch
        {
            AssistantModelOptions.Anthropic => (
                options.BaseUrl is { Length: > 0 } url
                    ? new AnthropicClient { ApiKey = options.ApiKey, BaseUrl = url, Timeout = timeout }
                    : new AnthropicClient { ApiKey = options.ApiKey, Timeout = timeout },
                options.Model ?? AssistantModelOptions.DefaultAnthropicModel),
            AssistantModelOptions.Ollama => (
                new AnthropicClient { AuthToken = options.ApiKey, BaseUrl = options.BaseUrl ?? AssistantModelOptions.OllamaCloud, Timeout = timeout },
                options.Model ?? throw new InvalidOperationException("Set Assistant:Model to an Ollama model with tool support, such as glm-5.3-flash.")),
            _ => throw new InvalidOperationException($"Assistant:Provider must be {AssistantModelOptions.Anthropic} or {AssistantModelOptions.Ollama}, not '{options.Provider}'."),
        };

        services.AddSingleton(sp => client.AsIChatClient(model, 4000)
            .AsBuilder()
            .UseFunctionInvocation(sp.GetRequiredService<ILoggerFactory>(), invoker =>
            {
                invoker.MaximumIterationsPerRequest = options.MaxToolRounds;
                invoker.IncludeDetailedErrors = false; // a tool's exception text stays on the server
            })
            .Build(sp));
        return services;
    }
}
