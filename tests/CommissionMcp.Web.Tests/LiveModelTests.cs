using System.Globalization;
using CommissionMcp.Web.Services;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Xunit.Abstractions;

namespace CommissionMcp.Web.Tests;

/// <summary>Skipped unless a real provider is configured through environment variables, so normal test runs never need a model.</summary>
public sealed class LiveFactAttribute : FactAttribute
{
    public LiveFactAttribute()
    {
        var provider = Environment.GetEnvironmentVariable("Chat__Provider");
        if (string.IsNullOrWhiteSpace(provider) || provider.Equals("Demo", StringComparison.OrdinalIgnoreCase))
            Skip = "Set Chat__Provider (Ollama or OpenAI) and Chat__Model to run against a real model.";
    }
}

/// <remarks>
/// Run with, for example:
///   $env:Chat__Provider = "Ollama"; $env:Chat__Model = "qwen2.5:7b"; dotnet test --filter LiveModelTests
/// A real model is not deterministic, so assertions only check the behaviour that must hold: which tools are called,
/// that the write tool pauses for approval, and that rejecting changes nothing.
/// </remarks>
public class LiveModelTests(ITestOutputHelper output)
{
    private const string AdjustPrompt = "Reduce A-102 payout by $250.50 because of a duplicate trade";
    private static readonly TimeSpan Patience = TimeSpan.FromMinutes(5);

    private static IChatClient RealClient() =>
        ChatClientFactory.Create(new ConfigurationBuilder().AddEnvironmentVariables().Build());

    private void Dump(Pipeline p, string title)
    {
        output.WriteLine($"----- {title}");
        foreach (var b in p.Blocks)
            output.WriteLine(b switch
            {
                FunctionApprovalBlock a => $"approval: {a.ToolName}({string.Join(", ", a.Arguments!.Select(kv => $"{kv.Key}={kv.Value}"))}) status={a.Status}",
                FunctionInvocationContentBlock f => $"tool call: {f.ToolName}({string.Join(", ", f.Arguments!.Select(kv => $"{kv.Key}={kv.Value}"))}) done={f.HasResult}",
                RichContentBlock r => $"text: {r.RawText}",
                _ => b.GetType().Name,
            });
    }

    [LiveFact]
    public async Task Real_model_answers_a_read_only_question_by_calling_the_tools()
    {
        await using var p = await Pipeline.CreateAsync(RealClient());

        await p.Chat.SendMessageAsync("Summarize advisor A-102 for October").WaitAsync(Patience);
        Dump(p, "read-only");

        var calls = p.Blocks.OfType<FunctionInvocationContentBlock>().ToList();
        Assert.NotEmpty(calls);
        Assert.All(calls, c => Assert.NotEqual("adjust_payout", c.ToolName));
        Assert.Contains(calls, c => c.ToolName is "get_advisor_summary" or "get_statement");
        Assert.Null(p.Approval);
        Assert.False(string.IsNullOrWhiteSpace(p.AssistantText));
        Assert.Equal(0m, await p.OpenAdjustmentsAsync("A-102"));
    }

    [LiveFact]
    public async Task Real_model_asks_for_approval_before_changing_data_and_applies_it_when_approved()
    {
        await using var p = await Pipeline.CreateAsync(RealClient());

        var send = p.Chat.SendMessageAsync(AdjustPrompt);
        await p.WaitForAsync(ConversationStatus.AwaitingInput, (int)Patience.TotalMilliseconds);
        Dump(p, "paused for approval");

        var approval = p.Approval;
        Assert.NotNull(approval);
        Assert.Equal("adjust_payout", approval.ToolName);
        var args = approval.Arguments!;
        Assert.Equal("A-102", args["advisorId"]?.ToString());
        // A real model's arguments arrive as raw JSON values (JsonElement), the demo model's as numbers: read both through text.
        Assert.Equal(-250.50m, decimal.Parse(args["amount"]!.ToString()!, CultureInfo.InvariantCulture));
        Assert.Equal(0m, await p.OpenAdjustmentsAsync("A-102"));   // nothing ran yet

        approval.Approve();
        await send.WaitAsync(Patience);
        Dump(p, "after approve");

        Assert.Equal(-250.50m, await p.OpenAdjustmentsAsync("A-102"));
    }

    [LiveFact]
    public async Task Real_model_is_told_when_the_user_rejects_and_nothing_changes()
    {
        await using var p = await Pipeline.CreateAsync(RealClient());

        var send = p.Chat.SendMessageAsync(AdjustPrompt);
        await p.WaitForAsync(ConversationStatus.AwaitingInput, (int)Patience.TotalMilliseconds);

        p.Approval!.Reject("The user declined this change.");
        await send.WaitAsync(Patience);
        Dump(p, "after reject");

        Assert.Equal(ApprovalStatus.Rejected, p.Approval.Status);
        Assert.Equal(0m, await p.OpenAdjustmentsAsync("A-102"));
        Assert.False(string.IsNullOrWhiteSpace(p.AssistantText));
    }
}
