using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CommissionMcp.Web.Services;

/// <summary>
/// Builds the chat client from configuration. Secrets (the API key) come from user secrets or environment
/// variables and are never stored in the repository.
///   Chat:Provider  "Demo" (default, offline) or "OpenAI" (any OpenAI-compatible endpoint)
///   Chat:Endpoint  optional base URL, for example http://localhost:11434/v1 for Ollama
///   Chat:Model     model name, required for OpenAI
///   Chat:ApiKey    API key, required for OpenAI
/// </summary>
public static class ChatClientFactory
{
    public static IChatClient Create(IConfiguration config)
    {
        var provider = config["Chat:Provider"] ?? "Demo";

        if (provider.Equals("Demo", StringComparison.OrdinalIgnoreCase))
            return new ScriptedChatClient();

        if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            var key = config["Chat:ApiKey"];
            var model = config["Chat:Model"];
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("Chat:ApiKey is not set. Use user secrets or the Chat__ApiKey environment variable.");
            if (string.IsNullOrWhiteSpace(model))
                throw new InvalidOperationException("Chat:Model is not set.");

            var options = new OpenAIClientOptions();
            if (!string.IsNullOrWhiteSpace(config["Chat:Endpoint"])) options.Endpoint = new Uri(config["Chat:Endpoint"]!);
            return new OpenAIClient(new ApiKeyCredential(key), options).GetChatClient(model).AsIChatClient();
        }

        throw new InvalidOperationException($"Unknown Chat:Provider '{provider}'. Use 'Demo' or 'OpenAI'.");
    }

    /// <summary>The client the UI uses: the provider client plus the middleware that runs tools and asks for approval.</summary>
    public static IChatClient CreateWithTools(IConfiguration config) =>
        Create(config).AsBuilder().UseFunctionInvocation().Build();
}
