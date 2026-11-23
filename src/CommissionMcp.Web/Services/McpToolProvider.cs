using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace CommissionMcp.Web.Services;

public sealed class McpOptions
{
    public string Endpoint { get; set; } = "http://localhost:5290/mcp";
}

/// <summary>What the UI shows about a tool: its name and whether using it asks the user first.</summary>
public sealed record ToolInfo(string Name, string? Description, bool NeedsApproval);

/// <summary>
/// Connects to the MCP server once and exposes its tools to the chat client.
/// Tools that are not marked read-only get wrapped so the function-invocation middleware asks the user first.
/// </summary>
public sealed class McpToolProvider(IOptions<McpOptions> options, HttpClient? http = null) : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private McpClient? _client;
    private IReadOnlyList<AITool>? _tools;

    public string Endpoint => options.Value.Endpoint;

    /// <summary>The tools found on the server, once connected. Empty before the first successful connection.</summary>
    public IReadOnlyList<ToolInfo> Info { get; private set; } = [];

    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken ct = default)
    {
        if (_tools is not null) return _tools;
        await _gate.WaitAsync(ct);
        try
        {
            if (_tools is not null) return _tools;

            var transportOptions = new HttpClientTransportOptions { Endpoint = new Uri(Endpoint) };
            var transport = http is null
                ? new HttpClientTransport(transportOptions)
                : new HttpClientTransport(transportOptions, http, ownsHttpClient: false);

            _client = await McpClient.CreateAsync(transport, cancellationToken: ct);
            var tools = await _client.ListToolsAsync(cancellationToken: ct);
            Info = tools.Select(t => new ToolInfo(t.Name, t.Description, NeedsApproval(t))).ToList();
            _tools = tools.Select(Wrap).ToList();
            return _tools;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Anything not explicitly read-only needs approval. A tool with no annotations is treated as a write tool.</summary>
    public static bool NeedsApproval(McpClientTool tool) => tool.ProtocolTool.Annotations?.ReadOnlyHint != true;

    private static AITool Wrap(McpClientTool tool) => NeedsApproval(tool) ? new ApprovalRequiredAIFunction(tool) : tool;

    public async ValueTask DisposeAsync()
    {
        if (_client is not null) await _client.DisposeAsync();
        _gate.Dispose();
    }
}
