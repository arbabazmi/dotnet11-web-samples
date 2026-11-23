using System.Globalization;
using System.Text.RegularExpressions;
using CommissionMcp.Web.Services;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace CommissionMcp.Web.Tests;

/// <remarks>
/// AgentContext.SendMessageAsync only completes when the whole turn is finished. When a tool needs approval the turn
/// pauses (status AwaitingInput) until the user approves or rejects, so these tests start the send, wait for the pause,
/// act as the user, and then await the send.
/// </remarks>
public class ChatFlowTests
{
    private const string AdjustPrompt = "Reduce A-102 payout by $250.50 because of a duplicate trade";

    [Fact]
    public async Task Only_the_write_tool_needs_approval_and_it_comes_from_the_read_only_annotation()
    {
        await using var p = await Pipeline.CreateAsync();

        Assert.Equal(["adjust_payout", "get_advisor_summary", "get_statement"], p.Tools.Select(t => t.Name).Order().ToArray());
        Assert.Equal(["adjust_payout"], p.Tools.OfType<ApprovalRequiredAIFunction>().Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task Read_only_question_streams_an_answer_and_shows_the_tool_calls_without_asking()
    {
        await using var p = await Pipeline.CreateAsync();

        await p.Chat.SendMessageAsync("Summarize advisor A-102 for October");
        Assert.Equal(ConversationStatus.Idle, p.Chat.Status);

        var calls = p.Blocks.OfType<FunctionInvocationContentBlock>().ToList();
        Assert.Contains(calls, c => c.ToolName == "get_advisor_summary" && c.HasResult);
        Assert.Contains(calls, c => c.ToolName == "get_statement" && c.HasResult);
        Assert.Null(p.Approval);
        Assert.Contains("Imani Brooks", p.AssistantText);
        Assert.Contains("2026-10", p.AssistantText);
        Assert.Equal(0m, await p.OpenAdjustmentsAsync("A-102"));
    }

    [Fact]
    public async Task Write_request_pauses_for_approval_and_changes_nothing_until_approved()
    {
        await using var p = await Pipeline.CreateAsync();

        var send = p.Chat.SendMessageAsync(AdjustPrompt);
        await p.WaitForAsync(ConversationStatus.AwaitingInput);

        var approval = p.Approval;
        Assert.NotNull(approval);
        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.Equal("adjust_payout", approval.ToolName);
        var args = approval.Arguments!;
        Assert.Equal("A-102", args["advisorId"]?.ToString());
        Assert.Equal(-250.50m, Convert.ToDecimal(args["amount"], CultureInfo.InvariantCulture));
        Assert.Equal(0m, await p.OpenAdjustmentsAsync("A-102"));   // nothing has run yet

        approval.Approve();
        await send;

        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Equal(-250.50m, await p.OpenAdjustmentsAsync("A-102"));
        Assert.Contains("ADJ-0001", p.AssistantText);
    }

    [Fact]
    public async Task Follow_up_change_request_reuses_the_advisor_from_the_earlier_question()
    {
        await using var p = await Pipeline.CreateAsync();

        await p.Chat.SendMessageAsync("Summarize advisor A-105");
        var send = p.Chat.SendMessageAsync("Can we add $500 in the adjustment");
        await p.WaitForAsync(ConversationStatus.AwaitingInput);

        var approval = p.Approval!;
        Assert.Equal("adjust_payout", approval.ToolName);
        Assert.Equal("A-105", approval.Arguments!["advisorId"]?.ToString());
        Assert.Equal(500m, Convert.ToDecimal(approval.Arguments["amount"], CultureInfo.InvariantCulture));
        approval.Reject("The user declined this change.");
        await send;
    }

    [Fact]
    public async Task Rejecting_leaves_data_unchanged_and_tells_the_model_it_was_declined()
    {
        await using var p = await Pipeline.CreateAsync();

        var send = p.Chat.SendMessageAsync(AdjustPrompt);
        await p.WaitForAsync(ConversationStatus.AwaitingInput);

        var approval = p.Approval!;
        approval.Reject("The user declined this change.");
        await send;

        Assert.Equal(ApprovalStatus.Rejected, approval.Status);
        Assert.Equal(0m, await p.OpenAdjustmentsAsync("A-102"));
        Assert.Contains("declined", p.AssistantText);

        // What the model received on its final call: a tool result saying the call was rejected.
        var final = p.Model.Received[^1];
        var toolResults = string.Join(" ", final.SelectMany(m => m.Contents).OfType<FunctionResultContent>()
            .Select(r => ScriptedChatClient.ResultText(r).Text));
        Assert.Matches(new Regex("reject|declin|denied", RegexOptions.IgnoreCase), toolResults);
    }

    [Fact]
    public async Task Approved_call_that_the_server_refuses_is_reported_and_changes_nothing()
    {
        await using var p = await Pipeline.CreateAsync();

        var send = p.Chat.SendMessageAsync("Increase A-102 payout by $9000 because of a big bonus");
        await p.WaitForAsync(ConversationStatus.AwaitingInput);
        p.Approval!.Approve();
        await send;

        Assert.Equal(0m, await p.OpenAdjustmentsAsync("A-102"));
        Assert.Contains("limit", p.AssistantText);
    }
}
