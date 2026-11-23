using CommissionMcp.Web.Services;
using Microsoft.Extensions.Configuration;

namespace CommissionMcp.Web.Tests;

public class ChatClientFactoryTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value)).Build();

    [Fact]
    public void No_provider_means_the_offline_demo_model() =>
        Assert.IsType<ScriptedChatClient>(ChatClientFactory.Create(Config()));

    [Fact]
    public void Ollama_needs_a_model_but_no_key_or_endpoint()
    {
        var client = ChatClientFactory.Create(Config(("Chat:Provider", "Ollama"), ("Chat:Model", "qwen2.5:7b")));
        Assert.NotNull(client);
        Assert.IsNotType<ScriptedChatClient>(client);
    }

    [Fact]
    public void Ollama_without_a_model_is_rejected_with_a_clear_message()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ChatClientFactory.Create(Config(("Chat:Provider", "Ollama"))));
        Assert.Contains("Chat:Model", ex.Message);
    }

    [Fact]
    public void OpenAI_still_requires_a_key()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ChatClientFactory.Create(Config(("Chat:Provider", "OpenAI"), ("Chat:Model", "m"))));
        Assert.Contains("Chat:ApiKey", ex.Message);
    }

    [Fact]
    public void Unknown_provider_names_the_valid_ones()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => ChatClientFactory.Create(Config(("Chat:Provider", "Nope"))));
        Assert.Contains("Ollama", ex.Message);
    }
}
