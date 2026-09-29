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
/// Assistant:Model      the model id (claude-sonnet-5-5 by default)
/// </code>
/// </summary>
public sealed class AssistantModelOptions
{
    public const string SectionName = "Assistant";
    public const string DefaultModel = "claude-sonnet-5-5";

    public string? ApiKey { get; set; }

    public string Model { get; set; } = DefaultModel;

    /// <summary>Tool calls the model may chain for one question before it must answer.</summary>
    public int MaxToolRounds { get; set; } = 8;
}

public static class AssistantModelRegistration
{
    /// <summary>
    /// Registers Claude as the assistant's <see cref="IChatClient"/> when a key is configured. The
    /// Application layer sees only the interface, so another provider (a government cloud's model,
    /// say) is a different registration here and nothing else. Function invocation is the layer that
    /// runs the tools the model asks for, capped so a confused model cannot loop.
    /// </summary>
    public static IServiceCollection AddAssistantModel(this IServiceCollection services, IConfiguration configuration)
    {
        AssistantModelOptions options = configuration.GetSection(AssistantModelOptions.SectionName).Get<AssistantModelOptions>() ?? new();
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return services;
        }

        services.AddSingleton(sp =>
        {
            var client = new AnthropicClient { ApiKey = options.ApiKey, Timeout = TimeSpan.FromSeconds(90) };
            return client.AsIChatClient(options.Model, 1500)
                .AsBuilder()
                .UseFunctionInvocation(sp.GetRequiredService<ILoggerFactory>(), invoker =>
                {
                    invoker.MaximumIterationsPerRequest = options.MaxToolRounds;
                    invoker.IncludeDetailedErrors = false; // a tool's exception text stays on the server
                })
                .Build(sp);
        });
        return services;
    }
}
