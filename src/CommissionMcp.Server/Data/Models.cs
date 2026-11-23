namespace CommissionMcp.Server.Data;

public record Advisor(string Id, string Name, string Tier, string Office);

public record StatementLine(string Date, string Description, string Product, decimal GrossAmount, decimal Rate, decimal Commission);

public record Adjustment(string Id, string AdvisorId, string Month, decimal Amount, string Reason, DateTimeOffset CreatedUtc);

public record Statement(
    string AdvisorId, string Month, bool IsOpen,
    IReadOnlyList<StatementLine> Lines, decimal GrossCommission,
    IReadOnlyList<Adjustment> Adjustments, decimal AdjustmentsTotal, decimal NetPayout);

public record AdvisorSummary(
    Advisor Advisor, string OpenMonth, decimal YearToDateCommission,
    string LastClosedMonth, decimal LastClosedNetPayout,
    decimal OpenMonthAdjustments, decimal OpenMonthNetPayout);
