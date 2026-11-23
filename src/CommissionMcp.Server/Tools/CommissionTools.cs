using System.ComponentModel;
using CommissionMcp.Server.Data;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace CommissionMcp.Server.Tools;

/// <summary>MCP tools over the synthetic commission data. Two read-only tools and one write tool.</summary>
[McpServerToolType]
public sealed class CommissionTools(CommissionStore store)
{
    [McpServerTool(Name = "get_advisor_summary", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Summarises one advisor: profile, year-to-date commission, last closed month and the open payout month (including any adjustments).")]
    public AdvisorSummary GetAdvisorSummary([Description("Advisor id such as A-102")] string advisorId)
        => Run(() => store.GetSummary(advisorId));

    [McpServerTool(Name = "get_statement", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns one advisor's commission statement for a month: line items, gross commission, adjustments and net payout.")]
    public Statement GetStatement(
        [Description("Advisor id such as A-102")] string advisorId,
        [Description("Month in yyyy-MM format, for example 2026-10")] string month)
        => Run(() => store.GetStatement(advisorId, month));

    [McpServerTool(Name = "adjust_payout", ReadOnly = false, Destructive = true, Idempotent = false, OpenWorld = false)]
    [Description("Changes an advisor's payout for the open month by adding an adjustment. Positive amounts increase the payout, negative amounts reduce it. This modifies data and needs a reason.")]
    public Adjustment AdjustPayout(
        [Description("Advisor id such as A-102")] string advisorId,
        [Description("Adjustment amount in dollars, between -5000 and 5000, at most two decimals")] decimal amount,
        [Description("Why the adjustment is being made (at least 5 characters)")] string reason)
        => Run(() => store.AdjustPayout(advisorId, amount, reason));

    // Expected problems become tool errors the model can read and react to, not server faults.
    private static T Run<T>(Func<T> action)
    {
        try { return action(); }
        catch (CommissionException ex) { throw new McpException(ex.Message); }
    }
}
