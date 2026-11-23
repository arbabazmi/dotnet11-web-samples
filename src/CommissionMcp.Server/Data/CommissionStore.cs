using System.Globalization;

namespace CommissionMcp.Server.Data;

/// <summary>Raised for expected, user-correctable problems (unknown advisor, bad amount). Tools turn these into tool errors.</summary>
public sealed class CommissionException(string message) : Exception(message);

/// <summary>
/// In-memory store with synthetic data only: five fictional advisors, closed months 2026-07 to 2026-10, and an open
/// payout month (2026-11) that adjustments apply to. Nothing persists beyond the process.
/// </summary>
public sealed class CommissionStore
{
    public const string OpenMonth = "2026-11";
    public const decimal MaxAdjustment = 5_000m;
    private static readonly string[] ClosedMonths = ["2026-07", "2026-08", "2026-09", "2026-10"];

    private static readonly Advisor[] AdvisorList =
    [
        new("A-101", "Rowan Hale",   "Gold",   "Maple Ridge (test)"),
        new("A-102", "Imani Brooks", "Silver", "Harbor Point (test)"),
        new("A-103", "Teodor Vance", "Bronze", "Maple Ridge (test)"),
        new("A-104", "Priya Okafor", "Gold",   "Stone Bay (test)"),
        new("A-105", "Lucas Marin",  "Silver", "Harbor Point (test)"),
    ];

    private static readonly (string Product, string Description, decimal Rate)[] Products =
    [
        ("Mutual Fund Trail", "Quarterly trail on mutual fund assets", 0.0100m),
        ("Annuity",           "Annuity placement commission",          0.0500m),
        ("Advisory Fee",      "Advisory fee share",                    0.0125m),
        ("Insurance",         "Insurance premium commission",          0.0300m),
    ];

    private readonly Dictionary<(string Advisor, string Month), List<StatementLine>> _lines = new();
    private readonly List<Adjustment> _adjustments = [];
    private readonly object _gate = new();
    private int _nextAdjustment = 1;

    public CommissionStore()
    {
        for (var a = 0; a < AdvisorList.Length; a++)
        {
            foreach (var month in ClosedMonths.Append(OpenMonth))
                _lines[(AdvisorList[a].Id, month)] = BuildLines(a, month);
        }
    }

    public IReadOnlyList<Advisor> Advisors => AdvisorList;

    public Advisor GetAdvisor(string advisorId) =>
        AdvisorList.FirstOrDefault(a => a.Id.Equals(advisorId?.Trim(), StringComparison.OrdinalIgnoreCase))
        ?? throw new CommissionException($"Advisor '{advisorId}' was not found. Valid ids: {string.Join(", ", AdvisorList.Select(a => a.Id))}.");

    public Statement GetStatement(string advisorId, string month)
    {
        var advisor = GetAdvisor(advisorId);
        month = NormalizeMonth(month);
        lock (_gate) return BuildStatement(advisor, month);
    }

    public AdvisorSummary GetSummary(string advisorId)
    {
        var advisor = GetAdvisor(advisorId);
        lock (_gate)
        {
            var closed = ClosedMonths.Select(m => BuildStatement(advisor, m)).ToList();
            var open = BuildStatement(advisor, OpenMonth);
            var last = closed[^1];
            return new AdvisorSummary(advisor, OpenMonth, closed.Sum(s => s.GrossCommission),
                last.Month, last.NetPayout, open.AdjustmentsTotal, open.NetPayout);
        }
    }

    /// <summary>Adds an adjustment to the open payout month. The only operation that changes data.</summary>
    public Adjustment AdjustPayout(string advisorId, decimal amount, string reason)
    {
        var advisor = GetAdvisor(advisorId);
        if (amount == 0) throw new CommissionException("Amount must not be zero.");
        if (Math.Abs(amount) > MaxAdjustment)
            throw new CommissionException($"Amount {amount:0.00} exceeds the per-adjustment limit of {MaxAdjustment:0.00}.");
        if (decimal.Round(amount, 2) != amount) throw new CommissionException("Amount can have at most two decimal places.");
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new CommissionException("A reason of at least 5 characters is required.");

        lock (_gate)
        {
            var adj = new Adjustment($"ADJ-{_nextAdjustment++:0000}", advisor.Id, OpenMonth, amount, reason.Trim(), DateTimeOffset.UtcNow);
            _adjustments.Add(adj);
            return adj;
        }
    }

    private static string NormalizeMonth(string month)
    {
        if (!DateTime.TryParseExact(month?.Trim(), "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            throw new CommissionException($"Month '{month}' is not valid. Use the format yyyy-MM, for example 2026-10.");
        var normalized = d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
        if (!ClosedMonths.Contains(normalized) && normalized != OpenMonth)
            throw new CommissionException($"No statement for {normalized}. Available: {string.Join(", ", ClosedMonths)} and {OpenMonth} (open).");
        return normalized;
    }

    private Statement BuildStatement(Advisor advisor, string month)
    {
        var lines = _lines[(advisor.Id, month)];
        var adjustments = _adjustments.Where(a => a.AdvisorId == advisor.Id && a.Month == month).ToList();
        var gross = lines.Sum(l => l.Commission);
        var adjTotal = adjustments.Sum(a => a.Amount);
        return new Statement(advisor.Id, month, month == OpenMonth, lines, gross, adjustments, adjTotal, gross + adjTotal);
    }

    // Deterministic pseudo-random lines (own LCG so values never change between runs or runtime versions).
    private static List<StatementLine> BuildLines(int advisorIndex, string month)
    {
        uint state = (uint)(advisorIndex * 7919 + int.Parse(month.Replace("-", ""), CultureInfo.InvariantCulture) * 31 + 17);
        uint Next() { state = unchecked(state * 1664525u + 1013904223u); return state >> 8; }

        var count = 4 + (int)(Next() % 3);   // 4 to 6 lines
        var list = new List<StatementLine>();
        for (var i = 0; i < count; i++)
        {
            var (product, description, rate) = Products[(int)(Next() % (uint)Products.Length)];
            var gross = 5_000m + (Next() % 551) * 100m;   // 5,000 to 60,000 in steps of 100
            var day = 1 + (int)(Next() % 27);
            var date = $"{month}-{day:00}";
            list.Add(new StatementLine(date, description, product, gross, rate, decimal.Round(gross * rate, 2)));
        }
        return list.OrderBy(l => l.Date, StringComparer.Ordinal).ToList();
    }
}
