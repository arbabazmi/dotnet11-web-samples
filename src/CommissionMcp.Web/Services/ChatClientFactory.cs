using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CommissionMcp.Web.Services;

/// <summary>
/// Builds the chat client from configuration. Secrets (an API key) come from user secrets or environment variables
/// and are never stored in the repository.
///   Chat:Provider  "Demo" (default, offline), "Ollama" (local, no key) or "OpenAI" (any OpenAI-compatible endpoint)
///   Chat:Model     model name, required for Ollama and OpenAI
///   Chat:Endpoint  base URL; Ollama defaults to http://localhost:11434/v1
///   Chat:ApiKey    required for OpenAI, ignored by a local Ollama
/// </summary>
public static class ChatClientFactory
{
    public const string OllamaDefaultEndpoint = "http://localhost:11434/v1";

    public static IChatClient Create(IConfiguration config)
    {
        var provider = config["Chat:Provider"] ?? "Demo";

        if (provider.Equals("Demo", StringComparison.OrdinalIgnoreCase))
            return new ScriptedChatClient();

        var isOllama = provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase);
        if (!isOllama && !provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unknown Chat:Provider '{provider}'. Use 'Demo', 'Ollama' or 'OpenAI'.");

        var model = config["Chat:Model"];
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Chat:Model is not set.");

        var endpoint = config["Chat:Endpoint"];
        var key = config["Chat:ApiKey"];
        if (isOllama)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) endpoint = OllamaDefaultEndpoint;
            if (string.IsNullOrWhiteSpace(key)) key = "ollama";   // Ollama ignores the key, but the client needs a value
        }
        else if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException("Chat:ApiKey is not set. Use user secrets or the Chat__ApiKey environment variable.");
        }

        // A local model on a CPU can take a long time to load and to produce its first token.
        var options = new OpenAIClientOptions { NetworkTimeout = TimeSpan.FromMinutes(5) };
        if (!string.IsNullOrWhiteSpace(endpoint)) options.Endpoint = new Uri(endpoint);
        return new OpenAIClient(new ApiKeyCredential(key!), options).GetChatClient(model).AsIChatClient();
    }

    /// <summary>The client the UI uses: the provider client plus the middleware that runs tools and asks for approval.</summary>
    public static IChatClient CreateWithTools(IConfiguration config) =>
        Create(config).AsBuilder().UseFunctionInvocation().Build();
}
