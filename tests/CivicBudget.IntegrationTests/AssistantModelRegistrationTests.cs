using CivicBudget.Infrastructure.Assistant;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CivicBudget.IntegrationTests;

/// <summary>
/// The operator's Assistant settings: no key means no model at all, either provider registers the
/// same interface, and a setting that could not work stops the app instead of failing on the first
/// question. No database or network is needed.
/// </summary>
public class AssistantModelRegistrationTests
{
    private static ServiceProvider Register(params (string Key, string Value)[] settings) =>
        new ServiceCollection().AddLogging()
            .AddAssistantModel(new ConfigurationBuilder().AddInMemoryCollection(settings.ToDictionary(s => $"Assistant:{s.Key}", s => (string?)s.Value)).Build())
            .BuildServiceProvider();

    [Fact]
    public void Without_a_key_there_is_no_model()
    {
        Assert.Null(Register(("Provider", "Ollama"), ("Model", "glm-5.3-flash")).GetService<IChatClient>());
    }

    [Theory]
    [InlineData("Anthropic", null)]
    [InlineData("Ollama", "glm-5.3-flash")]
    public void Either_provider_is_the_same_interface_with_tool_calling_in_front(string provider, string? model)
    {
        List<(string, string)> settings = [("ApiKey", "test-key"), ("Provider", provider)];
        if (model is not null)
        {
            settings.Add(("Model", model));
        }

        IChatClient client = Register([.. settings]).GetRequiredService<IChatClient>();

        Assert.NotNull(client.GetService<FunctionInvokingChatClient>());
    }

    [Fact]
    public void An_unknown_provider_or_an_ollama_setup_without_a_model_stops_the_app()
    {
        Assert.Contains("Anthropic or Ollama", Assert.Throws<InvalidOperationException>(() => Register(("ApiKey", "k"), ("Provider", "Other"))).Message, StringComparison.Ordinal);
        Assert.Contains("Assistant:Model", Assert.Throws<InvalidOperationException>(() => Register(("ApiKey", "k"), ("Provider", "Ollama"))).Message, StringComparison.Ordinal);
    }
}
