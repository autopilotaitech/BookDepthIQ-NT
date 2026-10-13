namespace BookDepthIQ.Core;

// Dual SuperTrend — a line-for-line port of BookDepthIQ's trend engine
// (BookDepthIQ crates/hmd-analytics/src/trend.rs, via BookDepthIQ-Vest src/panel/supertrend.ts).
// Candles are built from trades: a candle completes when the first trade of a later time bucket
// arrives. ATR = EMA of true range (α = 2/(N+1)); bands = typical (H+L+C)/3 ± multiplier × ATR; the
// line ratchets and flips by the switch rule. Keep in step with trend.rs — change both or neither.

public enum TrendDir { Neutral, Up, Down }
public enum SwitchCond { Close, Wick, Confirmed, Volume, Delta, DeltaBook }
public enum CombinedState { StrongLong, WeakLong, Chop, WeakShort, StrongShort }

public sealed record TrendConfig
{
    public bool Enabled { get; init; } = true;
    public int IntervalSec { get; init; } = 60;
    public int NumCandles { get; init; } = 10;
    public double Multiplier { get; init; } = 3.6;
    public SwitchCond Switch { get; init; } = SwitchCond.Delta;
    public int Confirm { get; init; } = 3;
    public double VolMult { get; init; } = 1.5;
    public double DeltaMin { get; init; }

    /// <summary>BookDepthIQ's live defaults: both flip on price + delta.</summary>
    public static readonly TrendConfig DefaultT1 = new() { IntervalSec = 60, NumCandles = 10, Multiplier = 3.6, Switch = SwitchCond.Delta, Confirm = 3 };
    public static readonly TrendConfig DefaultT2 = new() { IntervalSec = 15, NumCandles = 10, Multiplier = 3.0, Switch = SwitchCond.Delta, Confirm = 1 };
}

public sealed record TrendSnapshot(TrendDir Dir, long? LineTicks, bool Switched);

public sealed class TrendEngine
{
    private sealed class Candle
    {
        public long Bucket; public double High, Low, Close; public double? PrevClose; public double Vol, Delta;
    }

    public TrendConfig Cfg { get; }
    private Candle? cur;
    private double? prevClose;
    private double? atr;
    private TrendDir dir = TrendDir.Neutral;
    private double? line;
    private int crossStreak;
    private bool lastSwitched;
    private double? volEma;
    private double bookImbalance;
    public double LastVol { get; private set; }
    public double LastDelta { get; private set; }
    private double lastClose;

    public TrendEngine(TrendConfig cfg) => Cfg = cfg;

    public void SetBookImbalance(double v) => bookImbalance = v;

    /// <summary>(close − line) / (3 × ATR), clamped to [−1, 1]; 0 without a line or ATR.</summary>
    public double LineDistanceNorm() => line is double l && atr is double a && a > 0 ? Math.Clamp((lastClose - l) / (3 * a), -1, 1) : 0;

    /// <summary>Feed one trade. Returns the completed candle's snapshot when this trade starts a new bucket.</summary>
    public TrendSnapshot? OnTrade(long tsMs, double priceTicks, double size, bool isBuy)
    {
        if (!Cfg.Enabled || Cfg.IntervalSec <= 0) return null;
        var bucket = (long)Math.Floor(tsMs / (Cfg.IntervalSec * 1000.0));
        var signed = isBuy ? size : -size;
        var c = cur;
        if (c is null)
        {
            cur = new Candle { Bucket = bucket, High = priceTicks, Low = priceTicks, Close = priceTicks, PrevClose = prevClose, Vol = size, Delta = signed };
            return null;
        }
        if (c.Bucket == bucket)
        {
            c.High = Math.Max(c.High, priceTicks);
            c.Low = Math.Min(c.Low, priceTicks);
            c.Close = priceTicks;
            c.Vol += size;
            c.Delta += signed;
            return null;
        }
        var snap = Finalize(c);
        prevClose = c.Close;
        cur = new Candle { Bucket = bucket, High = priceTicks, Low = priceTicks, Close = priceTicks, PrevClose = prevClose, Vol = size, Delta = signed };
        return snap;
    }

    private TrendSnapshot Finalize(Candle c)
    {
        LastVol = c.Vol;
        LastDelta = c.Delta;
        lastClose = c.Close;
        var n = Math.Max(1, Cfg.NumCandles);
        var alpha = 2.0 / (n + 1);
        var prevVolEma = volEma;
        volEma = prevVolEma is null ? c.Vol : prevVolEma + alpha * (c.Vol - prevVolEma);

        var typical = (c.High + c.Low + c.Close) / 3;
        var tr = c.PrevClose is double pc ? Math.Max(c.High - c.Low, Math.Max(Math.Abs(c.High - pc), Math.Abs(c.Low - pc))) : c.High - c.Low;
        atr = atr is null ? tr : atr + alpha * (tr - atr);
        var a = atr.Value;
        var upper = typical + Cfg.Multiplier * a;
        var lower = typical - Cfg.Multiplier * a;
        var close = c.Close;

        if (line is null)
        {
            dir = close >= typical ? TrendDir.Up : TrendDir.Down;
            line = dir == TrendDir.Up ? lower : upper;
            lastSwitched = false;
            return Snap();
        }
        var prevLine = line.Value;
        var switched = false;
        if (dir is TrendDir.Up or TrendDir.Down)
        {
            var up = dir == TrendDir.Up;
            var newLine = up ? Math.Max(prevLine, lower) : Math.Min(prevLine, upper);
            var breached = up
                ? Cfg.Switch == SwitchCond.Wick ? c.Low < newLine : close < newLine
                : Cfg.Switch == SwitchCond.Wick ? c.High > newLine : close > newLine;
            if (breached)
            {
                var flipTo = up ? TrendDir.Down : TrendDir.Up;
                var flipLine = up ? upper : lower;
                var flowOk = FlowOk(!up, c, prevVolEma);
                var sw = Cfg.Switch;
                if (sw == SwitchCond.Confirmed)
                {
                    crossStreak++;
                    if (crossStreak >= Math.Max(1, Cfg.Confirm)) { dir = flipTo; line = flipLine; switched = true; crossStreak = 0; }
                    else line = newLine;
                }
                else if (sw is SwitchCond.Volume or SwitchCond.Delta or SwitchCond.DeltaBook)
                {
                    if (flowOk) { dir = flipTo; line = flipLine; switched = true; crossStreak = 0; }
                    else { crossStreak = 0; line = newLine; }
                }
                else
                {
                    crossStreak++;
                    if (crossStreak >= 1) { dir = flipTo; line = flipLine; switched = true; crossStreak = 0; }
                    else line = newLine;
                }
            }
            else
            {
                crossStreak = 0;
                line = newLine;
            }
        }
        else
        {
            dir = close >= typical ? TrendDir.Up : TrendDir.Down;
            line = dir == TrendDir.Up ? lower : upper;
        }
        lastSwitched = switched;
        return Snap();
    }

    private bool FlowOk(bool newDirUp, Candle c, double? prevVolEma)
    {
        var volOk = prevVolEma is double pv && pv > 0 ? c.Vol >= Cfg.VolMult * pv : true;
        var deltaOk = newDirUp ? c.Delta >= Cfg.DeltaMin : c.Delta <= -Cfg.DeltaMin;
        var bookOk = newDirUp ? bookImbalance >= 0 : bookImbalance <= 0;
        return Cfg.Switch switch
        {
            SwitchCond.Volume => volOk,
            SwitchCond.Delta => deltaOk,
            SwitchCond.DeltaBook => deltaOk && bookOk,
            _ => true,
        };
    }

    private TrendSnapshot Snap() => new(dir, line is double l ? (long)Math.Round(l) : null, lastSwitched);

    /// <summary>Current ATR in ticks, once known.</summary>
    public double? AtrTicks => atr;

    /// <summary>Passive read: never reports a flip.</summary>
    public TrendSnapshot Snapshot() => new(dir, line is double l ? (long)Math.Round(l) : null, false);
}

public sealed record DualSnapshot(TrendSnapshot T1, TrendSnapshot T2, CombinedState State, int Confidence);

public sealed class DualTrend
{
    public TrendEngine T1 { get; private set; }
    public TrendEngine T2 { get; private set; }
    private double bookImbalance;

    public DualTrend(TrendConfig? c1 = null, TrendConfig? c2 = null)
    {
        T1 = new TrendEngine(c1 ?? TrendConfig.DefaultT1);
        T2 = new TrendEngine(c2 ?? TrendConfig.DefaultT2);
    }

    /// <summary>Wipe candle/ATR/line state but keep the configs (instrument change).</summary>
    public void Reset()
    {
        T1 = new TrendEngine(T1.Cfg);
        T2 = new TrendEngine(T2.Cfg);
        bookImbalance = 0;
    }

    public void SetBookImbalance(double v)
    {
        bookImbalance = v;
        T1.SetBookImbalance(v);
        T2.SetBookImbalance(v);
    }

    public DualSnapshot? OnTrade(long tsMs, double priceTicks, double size, bool isBuy)
    {
        var s1 = T1.OnTrade(tsMs, priceTicks, size, isBuy);
        var s2 = T2.OnTrade(tsMs, priceTicks, size, isBuy);
        if (s1 is null && s2 is null) return null;
        var snap = Snapshot();
        return snap with
        {
            T1 = s1 is null ? snap.T1 : snap.T1 with { Switched = s1.Switched },
            T2 = s2 is null ? snap.T2 : snap.T2 with { Switched = s2.Switched },
        };
    }

    public static CombinedState Combined(TrendDir t1, TrendDir t2) =>
        t1 == TrendDir.Up ? (t2 == TrendDir.Up ? CombinedState.StrongLong : CombinedState.WeakLong)
        : t1 == TrendDir.Down ? (t2 == TrendDir.Down ? CombinedState.StrongShort : CombinedState.WeakShort)
        : CombinedState.Chop;

    /// <summary>Confidence −100..100 from inputs in the ST1 direction frame, each ~[−1, 1].</summary>
    public static int ConfidenceScore(double dirSlope, double lineDistance, double volDelta, double book, double chop)
    {
        var c = Math.Clamp(0.35 * dirSlope + 0.25 * lineDistance + 0.2 * volDelta + 0.1 * book - 0.1 * chop, -1, 1);
        return (int)Math.Clamp(Math.Round(c * 100, MidpointRounding.AwayFromZero), -100, 100);
    }

    public DualSnapshot Snapshot()
    {
        var a = T1.Snapshot();
        var b = T2.Snapshot();
        var state = Combined(a.Dir, b.Dir);
        if (a.LineTicks is null) return new DualSnapshot(a, b, state, 0);
        var sign = a.Dir == TrendDir.Up ? 1 : a.Dir == TrendDir.Down ? -1 : 0;
        // trend.rs divides by last_vol.max(1): whole CME contracts, as here.
        var volDelta = Math.Tanh(T1.LastDelta / Math.Max(T1.LastVol, 1));
        var chop = a.Dir == TrendDir.Neutral || a.Dir != b.Dir ? 1 : 0;
        return new DualSnapshot(a, b, state, ConfidenceScore(sign, T1.LineDistanceNorm(), volDelta, Math.Clamp(bookImbalance, -1, 1), chop));
    }
}
