using System.Globalization;

namespace BookDepthIQ.Core;

// Price structure: one-minute bars (swing stops, the range), market state, the SuperTrend scalp read,
// and the trade journal. Pure. Port of BookDepthIQ-Vest src/panel/structure.ts. Prices in ticks.

public sealed class Bar { public long Hi, Lo, Close; }

public sealed record Regime(double Er, double MoveAtr);

public enum MarketState { Warming, TrendUp, TrendDown, Chop }

public sealed record StateView(MarketState State, string? Extended, string Why, double? Adx=null)
{
    public string AdxLabel => Adx is double score
        ? (State switch {MarketState.TrendUp=>"TREND ▲",MarketState.TrendDown=>"TREND ▼",_=>"CHOP"})+
            " · ADX "+score.ToString("0.0",CultureInfo.InvariantCulture)+(Extended!=null?" · EXTENDED":"")
        : "ADX —";
}

public enum ScalpZone { Go, Near, Stretched }

public sealed record ScalpRead(bool Long, ScalpZone Zone, double DistAtr, double Pull);

/// <summary>One-minute bars from the trade feed, the last keepMin minutes (trades in time order).</summary>
public sealed class MinuteBars
{
    public const int RegimeBars = 15;
    public const double RegimeEr = 0.3, RegimeAtr = 3;
    private readonly SortedDictionary<long, Bar> bars = new();
    private readonly int keepMin;
    public MinuteBars(int keepMin = 240) => this.keepMin = keepMin;

    public void Add(long timeMs, long priceTicks)
    {
        var m = timeMs / 60_000;
        if (bars.TryGetValue(m, out var b))
        {
            b.Hi = Math.Max(b.Hi, priceTicks);
            b.Lo = Math.Min(b.Lo, priceTicks);
            b.Close = priceTicks;
            return;
        }
        bars[m] = new Bar { Hi = priceTicks, Lo = priceTicks, Close = priceTicks };
        foreach (var k in bars.Keys.Where(k => k < m - keepMin).ToList()) bars.Remove(k);
    }

    public void Clear() => bars.Clear();

    /// <summary>High and low over the last minutes up to now (this minute included).</summary>
    public (long Hi, long Lo)? Range(int minutes, long nowMs)
    {
        var now = nowMs / 60_000;
        long hi = long.MinValue, lo = long.MaxValue;
        foreach (var (k, b) in bars)
        {
            if (k <= now - minutes || k > now) continue;
            hi = Math.Max(hi, b.Hi);
            lo = Math.Min(lo, b.Lo);
        }
        return hi == long.MinValue ? null : (hi, lo);
    }

    /// <summary>Efficiency (net ÷ path, −1..1) and net move in 14-bar Wilder ATR over the last n bars.
    /// Null until there are n + 1 bars.</summary>
    public Regime? RegimeAt(long nowMs, int n = RegimeBars)
    {
        var now = nowMs / 60_000;
        var bs = bars.Where(kv => kv.Key <= now).Select(kv => kv.Value).ToList();
        if (bs.Count < n + 1) return null;
        double atr = 0;
        for (var i = 0; i < bs.Count; i++)
        {
            var b = bs[i];
            double tr = i > 0 ? Math.Max(b.Hi - b.Lo, Math.Max(Math.Abs(b.Hi - bs[i - 1].Close), Math.Abs(b.Lo - bs[i - 1].Close))) : b.Hi - b.Lo;
            atr = i < 14 ? (atr * i + tr) / (i + 1) : atr + (tr - atr) / 14;
        }
        var last = bs.Count - 1;
        double path = 0;
        for (var k = last - n + 1; k <= last; k++) path += Math.Abs(bs[k].Close - bs[k - 1].Close);
        double net = bs[last].Close - bs[last - n].Close;
        return new Regime(path > 0 ? net / path : 0, atr > 0 ? net / atr : 0);
    }
}

public static class Structure
{
    public const double ScalpNearAtr = 2;
    public const int ScalpPullbackBars = 5;

    /// <summary>Swing stop distance in ticks from refTicks: bufTicks under the swing low for a long,
    /// over the swing high for a short. At least one tick.</summary>
    public static int? SwingStopTicks(bool isLong, long refTicks, (long Hi, long Lo)? range, int bufTicks)
    {
        if (range is not var (hi, lo)) return null;
        var level = isLong ? lo - bufTicks : hi + bufTicks;
        return (int)Math.Max(1, (refTicks - level) * (isLong ? 1 : -1));
    }

    /// <summary>TREND when the last 15 minutes went one way efficiently (≥ 0.3) and far (≥ 3 ATR); it holds
    /// until efficiency drops under half that or the move under 1.5 ATR. Otherwise CHOP. EXTENDED when price
    /// is 2σ+ from VWAP or 3+ ATR from ST1. Describes now; does not forecast.</summary>
    public static StateView Market(Regime? r, MarketState? prev, TrendDir st1, double? price, double? vwap, double? vwapSigma, double? st1Line, double? atrTicks)
    {
        if (r is null) return new(MarketState.Warming, null, $"needs {MinuteBars.RegimeBars + 1} minutes of trades");
        var up = r.Er >= MinuteBars.RegimeEr && r.MoveAtr >= MinuteBars.RegimeAtr;
        var down = r.Er <= -MinuteBars.RegimeEr && r.MoveAtr <= -MinuteBars.RegimeAtr;
        var holdUp = prev == MarketState.TrendUp && r.Er >= MinuteBars.RegimeEr / 2 && r.MoveAtr >= MinuteBars.RegimeAtr / 2;
        var holdDown = prev == MarketState.TrendDown && r.Er <= -MinuteBars.RegimeEr / 2 && r.MoveAtr <= -MinuteBars.RegimeAtr / 2;
        var state = up || holdUp ? MarketState.TrendUp : down || holdDown ? MarketState.TrendDown : MarketState.Chop;
        var fmt = $"15m: {(r.MoveAtr >= 0 ? "+" : "")}{r.MoveAtr.ToString("0.0", CultureInfo.InvariantCulture)} ATR, efficiency {Math.Abs(r.Er).ToString("0.00", CultureInfo.InvariantCulture)}";
        var why = state == MarketState.Chop ? $"{fmt} — back and forth" : $"{fmt}{(up || down ? "" : " (holding)")}";
        if (state != MarketState.Chop && st1 != TrendDir.Neutral && st1 != (state == MarketState.TrendUp ? TrendDir.Up : TrendDir.Down)) why += " · ST1 against";
        return WithExtension(new(state,null,why),price,vwap,vwapSigma,st1Line,atrTicks);
    }

    /// <summary>ADX is strength only. DI comparison supplies the direction; no movement remains CHOP
    /// even though NT's zero-DI fallback produces ADX 50. Never a trade signal or entry gate.</summary>
    public static StateView MarketAdx(AdxReading? reading,int minutes,int period,double threshold,
        double? price,double? vwap,double? vwapSigma,double? st1Line,double? atrTicks)
    {
        var basis=$"ADX({period}) · {minutes}m completed bars · threshold {threshold.ToString("0.0",CultureInfo.InvariantCulture)}";
        if(reading is null) return new(MarketState.Warming,null,basis+$" · needs at least {2*period+1} completed bars");
        var trending=reading.Value>=threshold && reading.PlusDi!=reading.MinusDi;
        var state=!trending?MarketState.Chop:reading.PlusDi>reading.MinusDi?MarketState.TrendUp:MarketState.TrendDown;
        var why=basis+$" · value {reading.Value.ToString("0.000",CultureInfo.InvariantCulture)} · +DI {reading.PlusDi.ToString("0.0",CultureInfo.InvariantCulture)} / −DI {reading.MinusDi.ToString("0.0",CultureInfo.InvariantCulture)} · strength, not a forecast";
        return WithExtension(new(state,null,why,reading.Value),price,vwap,vwapSigma,st1Line,atrTicks);
    }

    private static StateView WithExtension(StateView view,double? price,double? vwap,double? vwapSigma,double? st1Line,double? atrTicks)
    {
        string? ext = null;
        var why=view.Why;
        if (price is double p)
        {
            var vz = vwap is double v && vwapSigma is double sg && sg > 0 ? (p - v) / sg : 0;
            var sz = st1Line is double l && atrTicks is double a && a > 0 ? (p - l) / a : 0;
            if (vz >= 2 || sz >= 3) ext = "up";
            else if (vz <= -2 || sz <= -3) ext = "down";
            if (ext != null) why += " · " + (Math.Abs(vz) >= 2 ? $"{vz:0.0}σ from VWAP" : $"{sz:0.0} ATR from ST1");
        }
        return view with {Extended=ext,Why=why};
    }

    /// <summary>Where an ST1 scalp stands: GO (near the line and pulling back), NEAR (near, still running:
    /// wait for the dip) or STRETCHED (2+ ATR out: don't chase). Measured on Vest NQ: GO won 63% at 10/10.</summary>
    public static ScalpRead? Scalp(TrendDir st1, double? priceTicks, double? st1Line, double? atrTicks, Regime? r5)
    {
        if (st1 == TrendDir.Neutral || priceTicks is not double p || st1Line is not double l || atrTicks is not double a || !(a > 0) || r5 is null) return null;
        var dir = st1 == TrendDir.Up ? 1 : -1;
        var dist = dir * (p - l) / a;
        var pull = dir * r5.Er;
        var zone = dist >= ScalpNearAtr ? ScalpZone.Stretched : pull < 0 ? ScalpZone.Go : ScalpZone.Near;
        return new ScalpRead(dir > 0, zone, dist, pull);
    }
}

/// <summary>One round trip in the journal. NinjaTrader's executions make every number exact.</summary>
public sealed record JournalTrade
{
    public long Open { get; init; }
    public long Close { get; init; }
    public string Inst { get; init; } = "";
    public string Account { get; init; } = "";
    public bool Long { get; init; }
    public int Qty { get; init; }
    public double Entry { get; init; }
    public double? Exit { get; init; }
    public double? Stop { get; init; }
    /// <summary>$ lost at the stop at entry (fees not included).</summary>
    public double? RiskUsd { get; init; }
    /// <summary>$ result after commissions and fees.</summary>
    public double Pnl { get; init; }
    public double FeesUsd { get; init; }
    public string Setup { get; init; } = "";
    public string Notes { get; init; } = "";
    public string StopMode { get; init; } = "";
    /// <summary>The price you expected when you clicked, and slippage in ticks (+ = worse).</summary>
    public double? Expected { get; init; }
    public double? SlipTicks { get; init; }
    public long? AckMs { get; init; }

    public double? R => RiskUsd is double r && r > 0 ? Pnl / r : null;
}

public sealed record SetupStat(string Setup, int N, double TotalUsd, double? AvgR);

public sealed record JournalStats(int N, int Wins, double WinRate, double TotalUsd, double? AvgR, List<SetupStat> BySetup, double? AvgSlipTicks, double FeesUsd);

public static class Journal
{
    public static JournalStats Stats(IReadOnlyList<JournalTrade> trades)
    {
        double? Avg(IEnumerable<double> xs) { var l = xs.ToList(); return l.Count > 0 ? l.Average() : null; }
        var tags = trades.Select(t => string.IsNullOrEmpty(t.Setup) ? "—" : t.Setup).Distinct().ToList();
        var wins = trades.Count(t => t.Pnl > 0);
        return new JournalStats(
            trades.Count, wins, trades.Count > 0 ? (double)wins / trades.Count : 0, trades.Sum(t => t.Pnl),
            Avg(trades.Where(t => t.R.HasValue).Select(t => t.R!.Value)),
            tags.Select(tag =>
            {
                var ts = trades.Where(t => (string.IsNullOrEmpty(t.Setup) ? "—" : t.Setup) == tag).ToList();
                return new SetupStat(tag, ts.Count, ts.Sum(t => t.Pnl), Avg(ts.Where(t => t.R.HasValue).Select(t => t.R!.Value)));
            }).ToList(),
            Avg(trades.Where(t => t.SlipTicks.HasValue).Select(t => t.SlipTicks!.Value)),
            trades.Sum(t => t.FeesUsd));
    }

    public static string Csv(IEnumerable<JournalTrade> trades)
    {
        string F(double? v, string fmt = "0.00") => v?.ToString(fmt, CultureInfo.InvariantCulture) ?? "";
        string Iso(long ms) => DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        string Quote(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
        var rows = trades.Select(t => string.Join(",", Iso(t.Open), Iso(t.Close), ((t.Close - t.Open) / 60_000.0).ToString("0.0", CultureInfo.InvariantCulture), Quote(t.Inst), Quote(t.Account),
            t.Long ? "long" : "short", t.Qty, F(t.Entry, "0.########"), F(t.Exit, "0.########"), F(t.Stop, "0.########"), F(t.RiskUsd), F(t.Pnl), F(t.R), Quote(t.Setup), Quote(t.StopMode),
            F(t.Expected, "0.########"), F(t.SlipTicks, "0.##"), F(t.FeesUsd), t.AckMs?.ToString(CultureInfo.InvariantCulture) ?? "", Quote(t.Notes)));
        return string.Join("\n", new[] { "open,close,minutes,instrument,account,side,qty,entry,exit,stop,risk_usd,pnl_usd,r,setup,stop_mode,expected,slip_ticks,fees_usd,ack_ms,notes" }.Concat(rows));
    }
}
