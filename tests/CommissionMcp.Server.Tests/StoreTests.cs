using CommissionMcp.Server.Data;

namespace CommissionMcp.Server.Tests;

public class StoreTests
{
    [Fact]
    public void Data_is_deterministic_between_instances()
    {
        var a = new CommissionStore().GetStatement("A-102", "2026-10");
        var b = new CommissionStore().GetStatement("A-102", "2026-10");
        Assert.Equal(a.GrossCommission, b.GrossCommission);
        Assert.Equal(a.Lines.Select(l => (l.Date, l.Commission)), b.Lines.Select(l => (l.Date, l.Commission)));
    }

    [Fact]
    public void Statement_has_lines_and_net_equals_gross_when_no_adjustments()
    {
        var s = new CommissionStore().GetStatement("a-102", "2026-10");   // id is case-insensitive
        Assert.InRange(s.Lines.Count, 4, 6);
        Assert.False(s.IsOpen);
        Assert.Equal(s.GrossCommission, s.NetPayout);
        Assert.Equal(s.Lines.Sum(l => l.Commission), s.GrossCommission);
    }

    [Fact]
    public void Summary_year_to_date_is_sum_of_closed_months()
    {
        var store = new CommissionStore();
        var ytd = new[] { "2026-07", "2026-08", "2026-09", "2026-10" }.Sum(m => store.GetStatement("A-102", m).GrossCommission);
        Assert.Equal(ytd, store.GetSummary("A-102").YearToDateCommission);
    }

    [Fact]
    public void Adjust_payout_changes_open_month_only()
    {
        var store = new CommissionStore();
        var before = store.GetSummary("A-102");
        var adj = store.AdjustPayout("A-102", -250.50m, "Correction for duplicate trade");
        var after = store.GetSummary("A-102");

        Assert.Equal("2026-11", adj.Month);
        Assert.Equal(before.OpenMonthNetPayout - 250.50m, after.OpenMonthNetPayout);
        Assert.Equal(before.LastClosedNetPayout, after.LastClosedNetPayout);
        Assert.Equal(before.YearToDateCommission, after.YearToDateCommission);
        Assert.Equal(-250.50m, store.GetStatement("A-102", "2026-11").AdjustmentsTotal);
    }

    [Fact]
    public void Adjustments_are_per_advisor()
    {
        var store = new CommissionStore();
        store.AdjustPayout("A-102", 100m, "Bonus true-up");
        Assert.Equal(0m, store.GetSummary("A-103").OpenMonthAdjustments);
    }

    [Theory]
    [InlineData("A-999", 10, "valid reason")]      // unknown advisor
    [InlineData("A-102", 0, "valid reason")]       // zero
    [InlineData("A-102", 5000.01, "valid reason")] // over limit
    [InlineData("A-102", 10.123, "valid reason")]  // too many decimals
    [InlineData("A-102", 10, "no")]                // reason too short
    [InlineData("A-102", 10, "   ")]               // blank reason
    public void Adjust_payout_rejects_bad_input_and_changes_nothing(string id, double amount, string reason)
    {
        var store = new CommissionStore();
        Assert.Throws<CommissionException>(() => store.AdjustPayout(id, (decimal)amount, reason));
        Assert.Equal(0m, store.GetSummary("A-102").OpenMonthAdjustments);
    }

    [Theory]
    [InlineData("2026-13")]
    [InlineData("October")]
    [InlineData("2025-10")]   // valid format, no statement
    public void Statement_rejects_bad_or_unavailable_months(string month)
        => Assert.Throws<CommissionException>(() => new CommissionStore().GetStatement("A-102", month));

    [Fact]
    public void Limit_boundary_is_allowed()
    {
        var store = new CommissionStore();
        store.AdjustPayout("A-101", 5000m, "Boundary check");
        store.AdjustPayout("A-101", -5000m, "Boundary check");
        Assert.Equal(0m, store.GetSummary("A-101").OpenMonthAdjustments);
    }
}
