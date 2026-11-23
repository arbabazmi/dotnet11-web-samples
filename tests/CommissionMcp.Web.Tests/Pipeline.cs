extern alias server;

using System.Text.Json;
using CommissionMcp.Web.Services;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CommissionMcp.Web.Tests;

/// <summary>
/// The real chat pipeline without a browser or a model: the in-memory MCP server, the app's tool provider, the
/// function-invocation middleware, the offline scripted client, and the real UIAgent/AgentContext from the Blazor AI package.
/// </summary>
public sealed class Pipeline : IAsyncDisposable
{
    private readonly WebApplicationFactory<server::Program> _server = new();
    private McpClient _observer = null!;          // a second, independent MCP client used only to check the data
    private McpToolProvider _tools = null!;
    private UIAgent _agent = null!;

    public ScriptedChatClient Model { get; } = new(TimeSpan.Zero);
    public AgentContext Chat { get; private set; } = null!;
    public IReadOnlyList<AITool> Tools { get; private set; } = null!;

    /// <param name="model">A real chat client to use instead of the offline scripted one (for the opt-in live tests).</param>
    public static async Task<Pipeline> CreateAsync(IChatClient? model = null)
    {
        var p = new Pipeline();
        var endpoint = new Uri(p._server.Server.BaseAddress, "/mcp");

        p._tools = new McpToolProvider(Options.Create(new McpOptions { Endpoint = endpoint.ToString() }), p._server.CreateClient());
        var tools = await p._tools.GetToolsAsync();
        p.Tools = tools;

        var client = (model ?? p.Model).AsBuilder().UseFunctionInvocation().Build();
        p._agent = new UIAgent(client, new ChatOptions { Tools = [.. tools], Temperature = 0 });
        p.Chat = new AgentContext(p._agent);

        var http = p._server.CreateClient();
        p._observer = await McpClient.CreateAsync(new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = endpoint }, http, ownsHttpClient: false));
        return p;
    }

    public IEnumerable<Microsoft.AspNetCore.Components.AI.ContentBlock> Blocks => Chat.Turns.SelectMany(t => t.ResponseBlocks);

    public string AssistantText => string.Concat(Blocks.OfType<RichContentBlock>().Select(b => b.RawText));

    public FunctionApprovalBlock? Approval => Blocks.OfType<FunctionApprovalBlock>().FirstOrDefault();

    public async Task WaitForAsync(ConversationStatus status, int timeoutMs = 10_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (Chat.Status != status)
        {
            if (Chat.Status == ConversationStatus.Error) throw new InvalidOperationException("Conversation failed: " + Chat.Error);
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"Waited for {status}, still {Chat.Status}.");
            await Task.Delay(20);
        }
    }

    /// <summary>Reads the open-month adjustments total straight from the MCP server, bypassing the chat.</summary>
    public async Task<decimal> OpenAdjustmentsAsync(string advisorId)
    {
        var r = await _observer.CallToolAsync("get_advisor_summary", new Dictionary<string, object?> { ["advisorId"] = advisorId });
        using var doc = JsonDocument.Parse(string.Concat(r.Content.OfType<TextContentBlock>().Select(t => t.Text)));
        return doc.RootElement.GetProperty("openMonthAdjustments").GetDecimal();
    }

    public async ValueTask DisposeAsync()
    {
        Chat.Dispose();
        _agent.Dispose();
        await _observer.DisposeAsync();
        await _tools.DisposeAsync();
        await _server.DisposeAsync();
    }
}
