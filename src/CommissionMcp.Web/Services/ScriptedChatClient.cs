using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;

namespace CommissionMcp.Web.Services;

/// <summary>
/// An offline stand-in for a language model, used when no API key is configured and in tests. It does no reasoning:
/// it reads the question with a few patterns, asks for the matching MCP tool calls, and writes an answer from the tool
/// results. It exercises the real pipeline (tool calls, results, approval, rejection, streaming) without a model.
/// </summary>
public sealed partial class ScriptedChatClient(TimeSpan? tokenDelay = null) : IChatClient
{
    private readonly TimeSpan _delay = tokenDelay ?? TimeSpan.FromMilliseconds(25);

    /// <summary>Every message list this client was called with. Lets tests check what the "model" was told.</summary>
    public List<IReadOnlyList<ChatMessage>> Received { get; } = [];

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        => await GetStreamingResponseAsync(messages, options, cancellationToken).ToChatResponseAsync(cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var list = messages.ToList();
        Received.Add(list);

        var lastUser = list.FindLastIndex(m => m.Role == ChatRole.User);
        var userText = lastUser >= 0 ? list[lastUser].Text : "";
        var afterUser = lastUser >= 0 ? list.Skip(lastUser + 1).ToList() : list;
        var contents = afterUser.SelectMany(m => m.Contents).ToList();

        var results = contents.OfType<FunctionResultContent>().ToList();
        if (results.Count > 0)
        {
            var names = contents.OfType<FunctionCallContent>().ToDictionary(c => c.CallId, c => c.Name);
            await foreach (var u in Stream(Answer(results, names), cancellationToken)) yield return u;
            yield break;
        }

        var earlier = list.Take(Math.Max(lastUser, 0)).Where(m => m.Role == ChatRole.User).Select(m => m.Text).ToList();
        var (intro, calls) = Plan(WithContext(userText, earlier));
        await foreach (var u in Stream(intro, cancellationToken)) yield return u;
        foreach (var call in calls)
            yield return new ChatResponseUpdate { Role = ChatRole.Assistant, Contents = [call] };
    }

    // ---- planning: which tools to call for this question ----

    /// <summary>
    /// A real model remembers the conversation; this one borrows just enough. A change request with no advisor id uses the
    /// last id mentioned, and a bare answer ("A-105") completes the previous change request that was missing it.
    /// </summary>
    private static string WithContext(string text, List<string> earlier)
    {
        if (earlier.Count == 0) return text;
        var previous = earlier[^1];
        var hasId = AdvisorId().IsMatch(text);
        if (!AdjustWords().IsMatch(text))
            return hasId && AdjustWords().IsMatch(previous) && !AdvisorId().IsMatch(previous) ? previous + " " + text : text;
        if (hasId) return text;
        var lastId = earlier.Select(e => AdvisorId().Match(e)).LastOrDefault(m => m.Success);
        return lastId is null ? text : text + " " + lastId.Value;
    }

    private static (string Intro, List<FunctionCallContent> Calls) Plan(string text)
    {
        var advisor = AdvisorId().Match(text);
        if (!advisor.Success)
            return ("I can look up an advisor's summary or statement, or adjust a payout. Please include an advisor id such as A-102, for example: \"Summarize advisor A-102 for October\".", []);

        var id = advisor.Value.ToUpperInvariant();
        var month = FindMonth(text);

        if (AdjustWords().IsMatch(text))
        {
            var amount = FindAmount(text);
            if (amount is null)
                return ($"How much should the payout for {id} change by? For example: \"Reduce {id} payout by $250 because of a duplicate trade\".", []);

            var reason = ReasonText().Match(text) is { Success: true } m ? CleanReason(m.Groups[1].Value) : "";
            if (reason.Length < 5) reason = "Requested in chat";
            return ($"I'll adjust the open-month payout for {id} by {Money(amount.Value)}. This changes data, so it needs your approval.",
                [Call("adjust_payout", new() { ["advisorId"] = id, ["amount"] = amount.Value, ["reason"] = reason })]);
        }

        var calls = new List<FunctionCallContent> { Call("get_advisor_summary", new() { ["advisorId"] = id }) };
        if (month is not null) calls.Add(Call("get_statement", new() { ["advisorId"] = id, ["month"] = month }));
        return ($"Let me look up {id}{(month is null ? "" : $" for {month}")}.", calls);
    }

    /// <summary>"because of a duplicate trade" -> "A duplicate trade": drop the lead-in words and capitalise.</summary>
    private static string CleanReason(string raw)
    {
        var s = LeadIn().Replace(raw.Trim().TrimEnd('.'), "");
        return s.Length == 0 ? "" : char.ToUpperInvariant(s[0]) + s[1..];
    }

    private static FunctionCallContent Call(string name, Dictionary<string, object?> args) =>
        new("call_" + Guid.NewGuid().ToString("N")[..8], name, args);

    private static string? FindMonth(string text)
    {
        var ym = YearMonth().Match(text);
        if (ym.Success) return $"{ym.Groups[1].Value}-{ym.Groups[2].Value}";
        var names = CultureInfo.InvariantCulture.DateTimeFormat.MonthNames;
        for (var i = 0; i < 12; i++)
            if (text.Contains(names[i], StringComparison.OrdinalIgnoreCase)) return $"2026-{i + 1:00}";
        return null;
    }

    private static decimal? FindAmount(string text)
    {
        // Remove ids, year-months and month names first so their digits are not mistaken for the amount.
        var cleaned = YearMonth().Replace(AdvisorId().Replace(text, " "), " ");
        var m = Amount().Match(cleaned);
        if (!m.Success || !decimal.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return null;
        return Reducing().IsMatch(text) ? -value : value;
    }

    // ---- answering: turn tool results into a reply ----

    private static string Answer(List<FunctionResultContent> results, Dictionary<string, string> names)
    {
        var parts = new List<string>();
        foreach (var r in results)
        {
            var name = names.GetValueOrDefault(r.CallId) ?? "tool";
            var (text, isError) = ResultText(r);

            if (Rejected().IsMatch(text))
            {
                parts.Add($"I did not apply the change: the {name} request was declined, so nothing was modified.");
                continue;
            }
            if (isError)
            {
                parts.Add($"The {name} tool reported a problem: {text}");
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(text);
                parts.Add(name switch
                {
                    "get_advisor_summary" => DescribeSummary(doc.RootElement),
                    "get_statement" => DescribeStatement(doc.RootElement),
                    "adjust_payout" => DescribeAdjustment(doc.RootElement),
                    _ => text,
                });
            }
            catch (JsonException) { parts.Add(text); }
        }
        return string.Join("\n\n", parts);
    }

    private static string DescribeSummary(JsonElement e)
    {
        var a = e.GetProperty("advisor");
        return $"{a.GetProperty("name").GetString()} ({a.GetProperty("id").GetString()}, {a.GetProperty("tier").GetString()} tier, {a.GetProperty("office").GetString()}). " +
               $"Year-to-date commission is {Money(e.GetProperty("yearToDateCommission").GetDecimal())}. " +
               $"The last closed month, {e.GetProperty("lastClosedMonth").GetString()}, paid out {Money(e.GetProperty("lastClosedNetPayout").GetDecimal())}. " +
               $"For the open month {e.GetProperty("openMonth").GetString()}, adjustments total {Money(e.GetProperty("openMonthAdjustments").GetDecimal())} " +
               $"and the net payout stands at {Money(e.GetProperty("openMonthNetPayout").GetDecimal())}.";
    }

    private static string DescribeStatement(JsonElement e)
    {
        var lines = e.GetProperty("lines");
        var top = lines.EnumerateArray().OrderByDescending(l => l.GetProperty("commission").GetDecimal()).First();
        return $"The {e.GetProperty("month").GetString()} statement has {lines.GetArrayLength()} line items totalling {Money(e.GetProperty("grossCommission").GetDecimal())} in commission. " +
               $"The largest is {top.GetProperty("product").GetString()} on {top.GetProperty("date").GetString()} at {Money(top.GetProperty("commission").GetDecimal())}. " +
               $"After adjustments of {Money(e.GetProperty("adjustmentsTotal").GetDecimal())}, the net payout is {Money(e.GetProperty("netPayout").GetDecimal())}.";
    }

    private static string DescribeAdjustment(JsonElement e) =>
        $"Done. Adjustment {e.GetProperty("id").GetString()} of {Money(e.GetProperty("amount").GetDecimal())} was applied to {e.GetProperty("advisorId").GetString()} " +
        $"for {e.GetProperty("month").GetString()}. Reason recorded: \"{e.GetProperty("reason").GetString()}\".";

    /// <summary>Tool results arrive as plain text, JSON text, or an MCP result object with a content array. Normalise to text plus an error flag.</summary>
    public static (string Text, bool IsError) ResultText(FunctionResultContent r)
    {
        switch (r.Result)
        {
            case null: return ("", r.Exception is not null);
            case string s: return (s, r.Exception is not null);
            // A successful MCP tool call comes back from the MCP client as TextContent (or a list of AIContent).
            case TextContent tc: return (tc.Text, false);
            case IEnumerable<AIContent> items: return (string.Concat(items.OfType<TextContent>().Select(t => t.Text)), false);
            case JsonElement je: return FromElement(je);
            default: return FromElement(JsonSerializer.SerializeToElement(r.Result));
        }

        static (string, bool) FromElement(JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.String) return (je.GetString() ?? "", false);
            if (je.ValueKind == JsonValueKind.Object && je.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                var text = string.Concat(content.EnumerateArray()
                    .Where(c => c.TryGetProperty("text", out _)).Select(c => c.GetProperty("text").GetString()));
                var isError = je.TryGetProperty("isError", out var err) && err.ValueKind == JsonValueKind.True;
                return (text, isError);
            }
            return (je.GetRawText(), false);
        }
    }

    private static string Money(decimal v) => (v < 0 ? "-" : "") + "$" + Math.Abs(v).ToString("N2", CultureInfo.InvariantCulture);

    private async IAsyncEnumerable<ChatResponseUpdate> Stream(string text, [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (var word in text.Split(' '))
        {
            if (_delay > TimeSpan.Zero) await Task.Delay(_delay, ct);
            yield return new ChatResponseUpdate(ChatRole.Assistant, word + " ");
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }

    [GeneratedRegex(@"\bA-\d{3}\b", RegexOptions.IgnoreCase)] private static partial Regex AdvisorId();
    [GeneratedRegex(@"\b(20\d{2})-(0[1-9]|1[0-2])\b")] private static partial Regex YearMonth();
    [GeneratedRegex(@"\b(adjust|increase|decrease|reduce|deduct|bonus|credit|reimburse|add)\b", RegexOptions.IgnoreCase)] private static partial Regex AdjustWords();
    [GeneratedRegex(@"\b(reduce|decrease|deduct|minus|less|remove|cut)\b", RegexOptions.IgnoreCase)] private static partial Regex Reducing();
    [GeneratedRegex(@"\$?\s?(\d[\d,]*(?:\.\d{1,2})?)")] private static partial Regex Amount();
    [GeneratedRegex(@"(?:because|reason:?)\s+(.+)$", RegexOptions.IgnoreCase)] private static partial Regex ReasonText();
    [GeneratedRegex(@"reject|declin|denied|not approved", RegexOptions.IgnoreCase)] private static partial Regex Rejected();
    [GeneratedRegex(@"^(of|that|the|for|to)\s+", RegexOptions.IgnoreCase)] private static partial Regex LeadIn();
}
