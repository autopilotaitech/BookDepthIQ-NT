using BookDepthIQ.Core;
using Xunit;

// Ports of the BookDepthIQ-Vest tests for the modules ported to C# (same cases, same numbers).

public class SuperTrendTests
{
    private static TrendConfig Cfg(int sec, int n, double mult, SwitchCond sw) => new() { IntervalSec = sec, NumCandles = n, Multiplier = mult, Switch = sw, Confirm = 2, VolMult = 1.5 };
    private static long S(int i) => i * 1000L;

    [Fact]
    public void Rising_market_goes_up_and_the_line_never_drops()
    {
        var e = new TrendEngine(Cfg(1, 3, 2.0, SwitchCond.Close));
        long? last = null;
        for (var i = 0; i < 30; i++)
        {
            var snap = e.OnTrade(S(i), 1000 + i * 5, 1, true);
            if (snap is { Dir: TrendDir.Up, LineTicks: long l })
            {
                if (last is long p) Assert.True(l >= p);
                last = l;
            }
        }
        Assert.Equal(TrendDir.Up, e.Snapshot().Dir);
    }

    [Fact]
    public void Cold_start_is_neutral()
    {
        var s = new TrendEngine(Cfg(1, 10, 2, SwitchCond.Close)).Snapshot();
        Assert.Equal(TrendDir.Neutral, s.Dir);
        Assert.Null(s.LineTicks);
    }

    [Fact]
    public void Close_cross_flips_down()
    {
        var e = new TrendEngine(Cfg(1, 3, 1, SwitchCond.Close));
        for (var i = 0; i < 20; i++) e.OnTrade(S(i), 1000 + i * 10, 1, true);
        Assert.Equal(TrendDir.Up, e.Snapshot().Dir);
        for (var i = 20; i < 40; i++) e.OnTrade(S(i), 1200 - (i - 19) * 40, 1, true);
        Assert.Equal(TrendDir.Down, e.Snapshot().Dir);
    }

    [Fact]
    public void Confirmed_needs_n_crosses()
    {
        var e = new TrendEngine(Cfg(1, 3, 1, SwitchCond.Confirmed));
        for (var i = 0; i < 20; i++) e.OnTrade(S(i), 1000 + i * 10, 1, true);
        e.OnTrade(S(20), 900, 1, true);
        e.OnTrade(S(21), 1000, 1, true);
        Assert.Equal(TrendDir.Up, e.Snapshot().Dir);
    }

    [Fact]
    public void Delta_switch_flips_only_when_delta_agrees()
    {
        TrendEngine Up()
        {
            var e = new TrendEngine(Cfg(1, 3, 1, SwitchCond.Delta));
            for (var i = 0; i < 20; i++) e.OnTrade(S(i), 1000 + i * 10, 1, true);
            return e;
        }
        var a = Up();
        a.OnTrade(S(20), 700, 1, true);
        a.OnTrade(S(21), 700, 1, true);
        Assert.Equal(TrendDir.Up, a.Snapshot().Dir);
        var b = Up();
        b.OnTrade(S(20), 700, 1, false);
        var snap = b.OnTrade(S(21), 700, 1, false)!;
        Assert.Equal(TrendDir.Down, snap.Dir);
        Assert.True(snap.Switched);
    }

    [Fact]
    public void Combined_state_and_switch_once()
    {
        Assert.Equal(CombinedState.StrongLong, DualTrend.Combined(TrendDir.Up, TrendDir.Up));
        Assert.Equal(CombinedState.WeakLong, DualTrend.Combined(TrendDir.Up, TrendDir.Down));
        Assert.Equal(CombinedState.WeakShort, DualTrend.Combined(TrendDir.Down, TrendDir.Up));
        Assert.Equal(CombinedState.Chop, DualTrend.Combined(TrendDir.Neutral, TrendDir.Up));
        var d = new DualTrend(Cfg(1, 3, 1, SwitchCond.Close), Cfg(100, 3, 1, SwitchCond.Close));
        for (var i = 0; i < 20; i++) d.OnTrade(S(i), 1000 + i * 10, 1, true);
        var flags = new List<bool>();
        for (var i = 20; i < 40; i++) if (d.OnTrade(S(i), 1200 - (i - 19) * 40, 1, true) is { } snap) flags.Add(snap.T1.Switched);
        Assert.Single(flags, f => f);
        Assert.Equal(TrendDir.Down, d.Snapshot().T1.Dir);
    }

    [Fact]
    public void Confidence_weights_match_trend_rs()
    {
        Assert.Equal(90, DualTrend.ConfidenceScore(1, 1, 1, 1, 0));
        Assert.Equal(25, DualTrend.ConfidenceScore(1, 0, 0, 0, 1));
        Assert.Equal(-90, DualTrend.ConfidenceScore(-1, -1, -1, -1, 0));
        Assert.Equal(60, TrendConfig.DefaultT1.IntervalSec);
        Assert.Equal(15, TrendConfig.DefaultT2.IntervalSec);
    }
}

public class PaxOrTests
{
    private static long T(int h, int m, int s = 0) => new DateTimeOffset(2026, 10, 8, h, m, s, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private static readonly PaxOrCfg Cfg = new();

    [Fact]
    public void Roots_and_spacing()
    {
        Assert.Equal("NQ", PaxOr.OrRoot("NQ 12-26"));
        Assert.Equal("ES", PaxOr.OrRoot("MES 12-26"));
        Assert.Equal("", PaxOr.OrRoot("GC 12-26"));
        Assert.Equal(260, PaxOr.LevelFactorTicks("NQ", Cfg, 0.25));
        Assert.Equal(60, PaxOr.LevelFactorTicks("ES", Cfg, 0.25));
    }

    [Fact]
    public void Session_and_weekend_carry()
    {
        var s = PaxOr.SessionAt(T(15, 0), Cfg)!;
        Assert.Equal(T(13, 30), s.Start);
        Assert.Equal(30_000, s.OrbEnd - s.Start);
        Assert.Equal(T(13, 30) + 86_400_000, s.End);
        var fri = new DateTimeOffset(2026, 10, 9, 13, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        var sat = PaxOr.SessionAt(new DateTimeOffset(2026, 10, 10, 18, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), Cfg)!;
        Assert.Equal(fri, sat.Start);
        Assert.Equal(new DateTimeOffset(2026, 10, 12, 13, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), sat.End);
    }

    [Fact]
    public void Or_and_ext_ladder()
    {
        var or = new PaxOr(PaxOr.SessionAt(T(15, 0), Cfg)!, 260, false);
        or.Add(T(13, 29, 59), 999_999);
        or.Add(T(13, 30, 5), 124_000);
        or.Add(T(13, 30, 20), 124_040);
        or.Add(T(13, 30, 30), 130_000);
        Assert.Equal(new[] { ("OR H", 124_040L), ("OR L", 124_000L), ("OR EXT1", 124_300L), ("OR EXT1", 123_740L), ("OR EXT2", 124_560L) },
            or.Levels().Select(l => (l.Label, l.Ticks)).ToArray());
        or.Add(T(14, 0), 124_560);
        Assert.Equal(5, or.Levels().Count);
        or.Add(T(14, 1), 123_739);
        Assert.Equal(("OR EXT2", 123_480L), (or.Levels()[^1].Label, or.Levels()[^1].Ticks));
    }
}

public class LiquidityFootprintTests
{
    private static Book Deep(Dictionary<long, long>? o = null) =>
        new(new BookSide(new long[] { 100, 99, 98, 97, 96, 95 }, new long[] { 100, 99, 98, 97, 96, 95 }.Select(t => o != null && o.TryGetValue(t, out var v) ? v : 10).ToArray()),
            new BookSide(new long[] { 101, 102, 103, 104, 105, 106 }, new long[] { 101, 102, 103, 104, 105, 106 }.Select(t => o != null && o.TryGetValue(t, out var v) ? v : 10).ToArray()), 0);

    [Fact]
    public void Pull_and_stack()
    {
        var l = new LiquidityTracker();
        l.OnBook(Deep(), 0);
        l.OnTrade(101, 3, '?', 0);
        l.OnBook(Deep(new() { [100] = 14, [101] = 2 }), 1000);
        var f = l.FlowAt(101);
        Assert.Equal((5, 3, -5), (f.Pulled, f.Traded, f.Net));
        Assert.Equal(4, l.FlowAt(100).Net);
    }

    [Fact]
    public void Absorb_bid()
    {
        var l = new LiquidityTracker();
        l.OnBook(Deep(), 0);
        for (var k = 1; k <= 4; k++)
        {
            l.OnTrade(100, 4, 's', k * 1000);
            l.OnBook(Deep(), k * 1000 + 500);
        }
        Assert.Equal("bid", l.Absorb(100, 1, 5));
    }

    [Fact]
    public void Heat_is_time_averaged()
    {
        var l = new LiquidityTracker();
        Book B(long bid, long ask) => new(bid > 0 ? new BookSide(new long[] { 100 }, new[] { bid }) : BookSide.Empty, ask > 0 ? new BookSide(new long[] { 100 }, new[] { ask }) : BookSide.Empty, 0);
        l.OnBook(B(10, 0), 0);
        l.OnBook(B(0, 4), LiquidityTracker.BinMs / 2);
        l.OnBook(B(0, 4), LiquidityTracker.BinMs);
        l.OnBook(B(0, 4), 2 * LiquidityTracker.BinMs);
        Assert.Equal(new double[] { 3, -4, -4 }, l.Heat(100));
    }

    [Fact]
    public void Diagonal_imbalances_and_runs()
    {
        var bought = new Dictionary<long, double> { [10] = 30, [11] = 30, [12] = 30 };
        var sold = new Dictionary<long, double> { [9] = 5, [10] = 5, [11] = 5 };
        var im = Footprint.Find(bought, sold, 1, 3, 0, 3);
        Assert.Equal(new long[] { 10, 11, 12 }, im.Buy.OrderBy(x => x));
        Assert.Equal(3, im.BuyStack.Count);
        var p = Footprint.BuildProfile(new Dictionary<long, double> { [1] = 1, [2] = 5, [3] = 2, [4] = 1 }, new Dictionary<long, double>(), 0.7);
        Assert.Equal(2, p.Poc);
        Assert.Equal((3L, 2L), (p.Vah!.Value, p.Val!.Value));
    }
}

public class TargetsManageStructureTests
{
    [Fact]
    public void Split_targets_whole_contracts()
    {
        var s = Targets.SplitTargets(5, new[] { new Target(16, 50), new Target(32, 30), new Target(48, 20) });
        Assert.True(s.Ok);
        Assert.Equal(new[] { 2, 1, 2 }, s.Legs.Select(l => l.Qty));
        Assert.False(Targets.SplitTargets(1, new[] { new Target(16, 50), new Target(32, 50) }).Ok);
        Assert.False(Targets.SplitTargets(4, new[] { new Target(32, 50), new Target(16, 50) }).Ok);
    }

    [Fact]
    public void Scale_orders_add_up()
    {
        var s = Targets.ScaleOrders(10, 100, 90, 4, 2, SkewDir.End);
        Assert.True(s.Ok);
        Assert.Equal(10, s.Legs.Sum(l => l.Qty));
        Assert.Equal(new long[] { 100, 97, 93, 90 }, s.Legs.Select(l => l.Ticks));
    }

    [Fact]
    public void Trail_moves_only_your_way()
    {
        Assert.Null(Manage.TrailTarget(true, 100, 10, 105, 1, 90)); // not 1R yet
        Assert.Equal(100, Manage.TrailTarget(true, 100, 10, 110, 1, 90));
        Assert.Null(Manage.TrailTarget(true, 100, 10, 110, 1, 100)); // already there
        Assert.Equal(95, Manage.LineTrailTarget(true, new TrendLine(95, TrendDir.Up), 90));
        Assert.Null(Manage.LineTrailTarget(true, new TrendLine(95, TrendDir.Down), 90));
    }

    [Fact]
    public void Be_watch_fires_once_on_tp1()
    {
        var w = new BeWatch();
        w.Arm("NQ", 1);
        Assert.Equal("idle", w.Check(0, 0)); // limit entry not filled yet
        Assert.Equal("idle", w.Check(3, 2));
        Assert.Equal("idle", w.Check(2, 2)); // manual partial close keeps both targets
        Assert.Equal("fire", w.Check(1, 1));
        Assert.Equal("idle", w.Check(1, 1));
    }

    [Fact]
    public void Stop_guard_puts_back_a_vanished_stop()
    {
        var g = new StopGuard(3000, 10_000);
        Assert.Empty(g.Check(new[] { ("NQ", (double?)20990) }, 0));
        Assert.Empty(g.Check(new[] { ("NQ", (double?)null) }, 1000));
        Assert.Equal(new[] { ("NQ", 20990.0) }, g.Check(new[] { ("NQ", (double?)null) }, 4500));
        Assert.Empty(g.Check(new[] { ("NQ", (double?)null) }, 5000)); // retry later
    }

    [Fact]
    public void Market_state_and_scalp_read()
    {
        var bars = new MinuteBars();
        for (var m = 0; m < 20; m++) bars.Add(m * 60_000L, 1000 + m * 10);
        var r = bars.RegimeAt(19 * 60_000L)!;
        Assert.True(r.Er > 0.9);
        Assert.Equal(MarketState.TrendUp, Structure.Market(r, null, TrendDir.Up, null, null, null, null, null).State);
        Assert.Equal(ScalpZone.Go, Structure.Scalp(TrendDir.Up, 1010, 1000, 10, new Regime(-0.4, 0))!.Zone);
        Assert.Equal(ScalpZone.Stretched, Structure.Scalp(TrendDir.Up, 1030, 1000, 10, new Regime(0.4, 0))!.Zone);
        Assert.Equal(11, Structure.SwingStopTicks(true, 1010, (1020, 1000), 1));
    }

    [Fact]
    public void Protect_day_limits()
    {
        var st = new DayState("2026-10-13", 50000, 3);
        Assert.False(Protect.Check(new DayLimits(500), st, 49400, 0, 50).Ok);
        Assert.False(Protect.Check(new DayLimits(MaxTrades: 3), st, 50000, 0, 50).Ok);
        Assert.False(Protect.Check(new DayLimits(500), st with { Trades = 0 }, 49800, 0, 400).Ok); // only $300 left
        Assert.True(Protect.Check(new DayLimits(500), st with { Trades = 0 }, 49800, 0, 200).Ok);
        Assert.Equal("2026-10-14", Protect.TradingDay(new DateTimeOffset(2026, 10, 13, 23, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds())); // 18:00 CDT
    }

    [Fact]
    public void Cost_walk_and_break_even()
    {
        var book = new Book(new BookSide(new long[] { 100, 99 }, new long[] { 2, 5 }), new BookSide(new long[] { 101, 102 }, new long[] { 1, 5 }), 0);
        var t = Cost.Trip(book, true, 2, 5, 2.0)!;
        Assert.Equal(101.5, t.Entry.AvgTicks);
        Assert.Equal(1.5, t.CrossTicks);
        Assert.Equal(1.5 * 2 * 5 + 8, t.TotalUsd, 6);
        Assert.Equal(Gate.Wide, Cost.SpreadGate(5, 1, 2));
        Assert.Contains(Cost.Checks(new CheckInput { Contracts = 6, MaxContracts = 5 }), c => c.Level == CheckLevel.Warn);
    }
}
