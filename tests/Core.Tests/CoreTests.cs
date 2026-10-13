using System.Text.Json;
using BookDepthIQ.Core;
using Xunit;

public class CmeRiskTests
{
    // NQ: 0.25 tick = $5. MNQ: $0.50.
    private static RiskInput Nq(SizeMode mode, double input, int stop = 16, int tp = 32, double room = 2000, double fee = 2.0, int max = 0) =>
        new() { TickValue = 5, RoomUsd = room, StopTicks = stop, TargetTicks = tp, FeePerSide = fee, Mode = mode, Input = input, MaxContracts = max };

    [Fact]
    public void Contracts_mode_uses_the_number_you_typed()
    {
        var r = CmeRisk.Calc(Nq(SizeMode.Contracts, 2));
        Assert.Equal(2, r.Contracts);
        Assert.Equal(2 * 16 * 5 + 2 * 2 * 2.0, r.Loss, 6); // 160 + 8 fees
        Assert.Equal(2 * 32 * 5 - 8, r.Win, 6);
        Assert.Equal(10, r.DollarsPerTick, 6);
    }

    [Fact]
    public void Risk_dollars_buy_whole_contracts_fees_in()
    {
        // $250 at a 16-tick stop: 80 + 4 fees = $84 per contract → 2 contracts.
        var r = CmeRisk.Calc(Nq(SizeMode.RiskUsd, 250));
        Assert.Equal(2, r.Contracts);
        Assert.False(r.BelowOne);
    }

    [Fact]
    public void Risk_too_small_for_one_contract_says_so()
    {
        var r = CmeRisk.Calc(Nq(SizeMode.RiskUsd, 50));
        Assert.Equal(0, r.Contracts);
        Assert.True(r.BelowOne);
    }

    [Fact]
    public void Room_percent_and_max_contracts_cap()
    {
        var r = CmeRisk.Calc(Nq(SizeMode.RoomPct, 25, room: 2000, max: 3)); // $500 → 5 ct, capped at 3
        Assert.Equal(3, r.Contracts);
        Assert.True(r.Capped);
        Assert.Equal(2000 / 15.0, r.TicksToFail, 6); // 3 ct × $5
    }

    [Fact]
    public void Stop_past_the_fail_line_is_flagged()
    {
        var r = CmeRisk.Calc(Nq(SizeMode.Contracts, 10, stop: 50, room: 2000)); // 10 ct → $50/tick → 40 ticks to fail
        Assert.True(r.StopPastFail);
        Assert.Equal(0, r.LosersLeft);
    }

    [Fact]
    public void Break_even_ticks_from_fees()
    {
        Assert.Equal(0.8, CmeRisk.BreakEvenTicks(5, 2.0), 6); // $4 round trip / $5 a tick
    }
}

public class DrawdownTests
{
    [Fact]
    public void Static_floor_never_moves()
    {
        var d = new DrawdownTracker(new DrawdownRules(50000, 2500, DrawdownKind.Static));
        d.OnEquity(53000);
        Assert.Equal(47500, d.Floor);
        Assert.Equal(5500, d.Room(53000));
    }

    [Fact]
    public void Intraday_trails_open_equity_and_stops_at_the_lock()
    {
        var d = new DrawdownTracker(new DrawdownRules(50000, 2500, DrawdownKind.TrailingIntraday, LockAt: 50100));
        d.OnEquity(51000);
        Assert.Equal(48500, d.Floor);
        d.OnEquity(53000);
        Assert.Equal(50100, d.Floor); // locked
        d.OnEquity(52000); // never moves down
        Assert.Equal(50100, d.Floor);
    }

    [Fact]
    public void Eod_moves_only_at_the_close_and_daily_limit_caps_room()
    {
        var d = new DrawdownTracker(new DrawdownRules(50000, 2500, DrawdownKind.TrailingEod, DailyLoss: 1000));
        d.OnEquity(52000);
        Assert.Equal(47500, d.Floor);
        d.OnSessionClose(51000);
        Assert.Equal(48500, d.Floor);
        Assert.Equal(1000, d.Room(51000)); // daily loss is tighter than the 2500 floor room
    }
}

public class BookAndProtocolTests
{
    [Fact]
    public void Book_from_levels_is_in_ticks_best_first()
    {
        var b = Book.FromLevels(new[] { new[] { 21000.0, 5 }, new[] { 21000.25, 3 } }, new[] { new[] { 21000.75, 4 }, new[] { 21000.5, 2 } }, 0.25, 0);
        Assert.Equal(84001, b.BestBid);
        Assert.Equal(84002, b.BestAsk);
        Assert.Equal(5, b.Bids.SizeAt(84000));
    }

    [Fact]
    public void Order_command_shape()
    {
        var j = JsonDocument.Parse(Protocol.Order("c1", "Sim101", "NQ 12-26", "buy", "limit", 2, 21000.25, 32, 16)).RootElement;
        Assert.Equal("order", j.GetProperty("t").GetString());
        Assert.Equal(2, j.GetProperty("qty").GetInt32());
        Assert.Equal(21000.25, j.GetProperty("price").GetDouble());
        Assert.Equal(16, j.GetProperty("sl").GetInt32());
        var m = JsonDocument.Parse(Protocol.Order("c2", "Sim101", "NQ 12-26", "sell", "market", 1, null, 0, 0)).RootElement;
        Assert.Equal(JsonValueKind.Null, m.GetProperty("price").ValueKind);
    }

    [Fact]
    public void Specs_fallback_by_root()
    {
        Assert.Equal(5.0, CmeSpecs.Find("NQ 12-26")!.TickValue);
        Assert.Equal(20.0, CmeSpecs.Find("NQ")!.PointValue);
        Assert.Equal(1.25, CmeSpecs.Find("MES 12-26")!.TickValue);
    }
}
