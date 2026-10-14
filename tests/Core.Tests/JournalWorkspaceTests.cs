using BookDepthIQ.Core;
using Xunit;

public class JournalWorkspaceTests
{
    [Fact]
    public void Export_quotes_notes_with_commas_quotes_and_newlines()
    {
        var csv = Journal.Csv(new[] { new JournalTrade { Notes = "review, \"patient\"\nentry", Setup = "a,b" } });
        Assert.Contains("\"a,b\"", csv);
        Assert.Contains("\"review, \"\"patient\"\"\nentry\"", csv);
    }

    [Fact]
    public void Environment_and_calendar_date_are_scoped_to_chicago()
    {
        Assert.Equal("SIM", JournalAnalytics.Environment("Sim101"));
        Assert.Equal("PLAYBACK", JournalAnalytics.Environment("Playback101"));
        Assert.Equal("LIVE", JournalAnalytics.Environment("APEX-123"));
        var utc = new DateTimeOffset(2026,10,15,2,0,0,TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal(new DateOnly(2026,10,14), JournalAnalytics.Day(utc));
    }

    [Fact]
    public void Analytics_uses_closed_net_returns_and_chronological_drawdown()
    {
        var trades = new[] { new JournalTrade { Close = 3, Pnl = 50, Exit = 1 },
            new JournalTrade { Close = 1, Pnl = 100, Exit = 1 }, new JournalTrade { Close = 2, Pnl = -40, Exit = 1 },
            new JournalTrade { Close = 0, Pnl = 999, Exit = null } };
        var a = JournalAnalytics.Compute(trades);
        Assert.Equal(110, a.Net);
        Assert.Equal(40, a.MaxDrawdown);
        Assert.Equal(3.75, a.ProfitFactor);
        Assert.Equal(3, a.Count);
        Assert.Equal(new double[] { 0, 100, 60, 110 }, a.Equity);
    }

    private static JournalTrade? Fill(TripBuilder b, string id, string account, bool buy, int qty, double price) =>
        b.OnExec(new(id, "MNQ 12-26", "", buy, qty, price, qty, 1000), account,
            (_, _) => new("pullback", "Ticks", null, null), _ => 2, _ => .25);

    [Fact]
    public void Sim_accounts_and_execution_ids_are_isolated()
    {
        var b = new TripBuilder();
        Fill(b, "1", "Sim101", true, 1, 100);
        Fill(b, "1", "Sim102", false, 1, 200);
        var a = Fill(b, "2", "Sim101", false, 1, 102)!;
        var c = Fill(b, "2", "Sim102", true, 1, 199)!;
        Assert.Equal(2, a.Pnl);
        Assert.Equal(0, c.Pnl);
        Assert.Equal("Sim102", c.Account);
    }

    [Fact]
    public void Partial_exit_then_scale_in_has_correct_weighted_entry_and_net_profit()
    {
        var b = new TripBuilder();
        Fill(b, "1", "Sim101", true, 2, 100);
        Fill(b, "2", "Sim101", false, 1, 102);
        Fill(b, "3", "Sim101", true, 1, 101);
        var t = Fill(b, "4", "Sim101", false, 2, 103)!;
        Assert.Equal(301.0 / 3, t.Entry, 8);
        Assert.Equal(8.0 / 3 + 100, t.Exit!.Value, 8);
        Assert.Equal(8, t.Pnl, 8); // gross $14, six $1 fill fees
    }
}
