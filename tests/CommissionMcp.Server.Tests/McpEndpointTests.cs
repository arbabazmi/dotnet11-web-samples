using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace CommissionMcp.Server.Tests;

/// <summary>Talks to the real MCP endpoint through the official MCP client, like any other MCP client would.</summary>
public class McpEndpointTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private async Task<McpClient> ConnectAsync(WebApplicationFactory<Program> f)
    {
        var http = f.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp") }, http, ownsHttpClient: false);
        return await McpClient.CreateAsync(transport);
    }

    private static string Text(CallToolResult r) => string.Concat(r.Content.OfType<TextContentBlock>().Select(t => t.Text));

    [Fact]
    public async Task Lists_the_three_tools_with_read_only_hints()
    {
        await using var client = await ConnectAsync(factory);
        var tools = await client.ListToolsAsync();

        Assert.Equal(["adjust_payout", "get_advisor_summary", "get_statement"], tools.Select(t => t.Name).Order().ToArray());
        Assert.True(tools.Single(t => t.Name == "get_statement").ProtocolTool.Annotations?.ReadOnlyHint);
        Assert.True(tools.Single(t => t.Name == "get_advisor_summary").ProtocolTool.Annotations?.ReadOnlyHint);
        var write = tools.Single(t => t.Name == "adjust_payout").ProtocolTool.Annotations;
        Assert.False(write?.ReadOnlyHint);
        Assert.True(write?.DestructiveHint);
    }

    [Fact]
    public async Task Summary_tool_returns_the_advisor()
    {
        await using var client = await ConnectAsync(factory);
        var result = await client.CallToolAsync("get_advisor_summary", new Dictionary<string, object?> { ["advisorId"] = "A-102" });

        Assert.NotEqual(true, result.IsError);
        using var doc = JsonDocument.Parse(Text(result));
        Assert.Equal("A-102", doc.RootElement.GetProperty("advisor").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Statement_tool_returns_lines_for_october()
    {
        await using var client = await ConnectAsync(factory);
        var result = await client.CallToolAsync("get_statement",
            new Dictionary<string, object?> { ["advisorId"] = "A-102", ["month"] = "2026-10" });

        using var doc = JsonDocument.Parse(Text(result));
        Assert.Equal("2026-10", doc.RootElement.GetProperty("month").GetString());
        Assert.True(doc.RootElement.GetProperty("lines").GetArrayLength() >= 4);
    }

    [Fact]
    public async Task Unknown_advisor_comes_back_as_a_tool_error_not_a_crash()
    {
        await using var client = await ConnectAsync(factory);
        var result = await client.CallToolAsync("get_advisor_summary", new Dictionary<string, object?> { ["advisorId"] = "A-999" });

        Assert.True(result.IsError);
        Assert.Contains("not found", Text(result));
    }

    [Fact]
    public async Task Adjust_tool_changes_the_open_month_and_summary_reflects_it()
    {
        // Own factory so the in-memory store starts clean for this test.
        await using var own = new WebApplicationFactory<Program>();
        await using var client = await ConnectAsync(own);

        var adjust = await client.CallToolAsync("adjust_payout", new Dictionary<string, object?>
            { ["advisorId"] = "A-102", ["amount"] = -250.5, ["reason"] = "Correction for duplicate trade" });
        Assert.NotEqual(true, adjust.IsError);

        var summary = await client.CallToolAsync("get_advisor_summary", new Dictionary<string, object?> { ["advisorId"] = "A-102" });
        using var doc = JsonDocument.Parse(Text(summary));
        Assert.Equal(-250.5m, doc.RootElement.GetProperty("openMonthAdjustments").GetDecimal());
    }

    [Fact]
    public async Task Adjust_tool_rejects_over_limit_amount()
    {
        await using var own = new WebApplicationFactory<Program>();
        await using var client = await ConnectAsync(own);

        var result = await client.CallToolAsync("adjust_payout", new Dictionary<string, object?>
            { ["advisorId"] = "A-102", ["amount"] = 9000, ["reason"] = "Too large on purpose" });
        Assert.True(result.IsError);
        Assert.Contains("limit", Text(result));
    }
}
