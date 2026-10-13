using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

/// <summary>What the ticket would send now: stop distance (ticks / ST1 ATR / swing), targets (ticks, or R
/// in ATR and swing modes), contracts from the size rule, and the risk numbers. One place for the ticket,
/// the preview on the ladder and the risk line.</summary>
public sealed class Ticket
{
    public int StopTicks { get; private set; }
    public string? StopWhy { get; private set; }
    public List<Target> Targets { get; } = new();
    public RiskOut Risk { get; private set; } = CmeRisk.Calc(new RiskInput());
    public Split Split { get; private set; } = new(true, new());
    public double Room { get; private set; }
    public int Contracts => Risk.Contracts;

    public static Ticket For(Settings c, DomState s, DrawdownTracker dd, bool isLong, long? refTicks = null)
    {
        var t = new Ticket();
        var atr = s.Trend.T1.AtrTicks;
        switch (c.StopMode)
        {
            case StopMode.Atr:
                if (atr is double a && a > 0) t.StopTicks = Math.Max(1, (int)Math.Round(a * c.AtrMult));
                else t.StopWhy = "ATR stop: ST1 needs a few candles first";
                break;
            case StopMode.Swing:
                var r = refTicks ?? (isLong ? s.Book.BestAsk : s.Book.BestBid) ?? s.LastTick;
                var st = r is long rr ? Structure.SwingStopTicks(isLong, rr, s.Bars.Range(c.SwingMinutes, DomState.NowMs()), c.SwingBufTicks) : null;
                if (st is int sw) t.StopTicks = sw;
                else t.StopWhy = $"swing stop: needs {c.SwingMinutes} min of prints";
                break;
            default:
                t.StopTicks = Math.Max(0, c.SlTicks);
                break;
        }
        var split = Core.Targets.ParseSplit(c.TpSplit);
        for (var i = 0; i < 3; i++)
        {
            var ticks = c.StopMode == StopMode.Ticks ? (i < c.TpTicks.Length ? c.TpTicks[i] : 0) : (int)Math.Round((i < c.TpR.Length ? c.TpR[i] : 0) * t.StopTicks);
            t.Targets.Add(new Target(ticks, i < split.Length ? split[i] : 0));
        }
        t.Room = dd.Room(s.Equity);
        var first = t.Targets.FirstOrDefault(x => x.Ticks > 0);
        t.Risk = CmeRisk.Calc(new RiskInput
        {
            TickValue = s.TickValue, RoomUsd = t.Room, StopTicks = t.StopTicks, TargetTicks = first?.Ticks ?? 0, FeePerSide = c.FeePerSide,
            Mode = c.SizeMode, Input = c.SizeInput, MaxContracts = c.MaxContracts,
        });
        t.Split = Core.Targets.SplitTargets(t.Risk.Contracts, t.Targets);
        return t;
    }

    /// <summary>The slices sent with an entry: each target with its contracts, or one stop-only slice.</summary>
    public IReadOnlyList<Piece> Slices(bool bracketOn)
    {
        if (!bracketOn || Contracts <= 0) return Array.Empty<Piece>();
        if (Split.Ok && Split.Legs.Count > 0) return Split.Legs;
        return StopTicks > 0 ? new[] { new Piece(0, Contracts) } : Array.Empty<Piece>();
    }
}
