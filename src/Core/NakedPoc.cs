namespace BookDepthIQ.Core;

/// <summary>A naked POC: the point of control of a finished period that price has not traded at since.</summary>
public sealed record NakedPocLevel(long Ticks, long PeriodStartMs, double Volume);

/// <summary>
/// Naked points of control. Each period (N minutes, wall-clock aligned) gets its own volume POC — the most
/// traded price, as on one footprint/volumetric bar. When the period closes that POC becomes a naked POC
/// line; it stays naked until a later print trades at that price, then it is gone. Feed prints in time
/// order (history first, then live). Ties for the POC go to the price nearest the period's volume-weighted
/// average. Pure.
/// </summary>
public sealed class NakedPocTracker
{
    private readonly long periodMs;
    private readonly int keep;
    private long curPeriod = long.MinValue;
    private readonly Dictionary<long, double> curVol = new();
    private readonly List<NakedPocLevel> naked = new();

    public NakedPocTracker(int periodMinutes = 15, int keep = 30)
    {
        periodMs = Math.Max(1, periodMinutes) * 60_000L;
        this.keep = Math.Max(1, keep);
    }

    public IReadOnlyList<NakedPocLevel> Levels => naked;
    public long PeriodMs => periodMs;

    public void OnPrint(long ms, long tick, double size)
    {
        if (size <= 0) return;
        var period = ms / periodMs;
        if (period != curPeriod)
        {
            if (curPeriod != long.MinValue && curVol.Count > 0) Close(curPeriod * periodMs);
            curPeriod = period;
            curVol.Clear();
        }
        // A print at a naked POC from an EARLIER period fills it.
        naked.RemoveAll(l => l.Ticks == tick && l.PeriodStartMs + periodMs <= ms);
        curVol[tick] = (curVol.TryGetValue(tick, out var v) ? v : 0) + size;
    }

    private void Close(long periodStart)
    {
        double tot = 0, pv = 0;
        foreach (var (t, v) in curVol) { tot += v; pv += t * v; }
        var vwap = tot > 0 ? pv / tot : 0;
        var max = curVol.Values.Max();
        var poc = curVol.Where(kv => kv.Value == max).OrderBy(kv => Math.Abs(kv.Key - vwap)).ThenBy(kv => kv.Key).First();
        naked.Add(new NakedPocLevel(poc.Key, periodStart, poc.Value));
        if (naked.Count > keep) naked.RemoveRange(0, naked.Count - keep);
    }

    public void Reset() { curPeriod = long.MinValue; curVol.Clear(); naked.Clear(); }
}
