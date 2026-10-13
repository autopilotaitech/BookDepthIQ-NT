namespace BookDepthIQ.Core;

// Pure maths behind the ladder: grouping, off-screen markers, the spread window.
// Port of BookDepthIQ-Vest src/panel/ladderMath.ts (index-basis tracker dropped: CME has no index gap).

public sealed record Offscreen(string Label, long Ticks, bool Above, long Distance);

public static class LadderMath
{
    /// <summary>Ticks per ladder row. 1 = every tick; larger steps show a wider range on one screen.</summary>
    public static readonly int[] GroupSteps = { 1, 2, 4, 8, 20, 40, 100 };

    /// <summary>Next finer (dir −1) or coarser (+1) group step, clamped to the list.</summary>
    public static int StepGroup(int current, int dir)
    {
        var i = Array.IndexOf(GroupSteps, current);
        var at = i < 0 ? 0 : i;
        return GroupSteps[Math.Clamp(at + dir, 0, GroupSteps.Length - 1)];
    }

    /// <summary>Row a tick belongs to: the bucket's lowest tick (floor, so negatives bucket consistently).</summary>
    public static long BucketOf(long ticks, int group) => (long)Math.Floor((double)ticks / group) * group;

    /// <summary>Sums sizes into group-tick buckets keyed by bucket floor.</summary>
    public static Dictionary<long, long> Aggregate(long[] ticks, long[] sizes, int group)
    {
        var o = new Dictionary<long, long>();
        for (var i = 0; i < ticks.Length; i++)
        {
            var b = BucketOf(ticks[i], group);
            o[b] = (o.TryGetValue(b, out var v) ? v : 0) + sizes[i];
        }
        return o;
    }

    /// <summary>$ P&L of a position of qty contracts (signed) at avgTicks if exited at rowTicks.</summary>
    public static double RowPnlUsd(int qty, double avgTicks, long rowTicks, double tickValue) => (rowTicks - avgTicks) * qty * tickValue;

    /// <summary>Markers (TP, SL, fail, orders) outside [lo, hi], pinned to the ladder edges.</summary>
    public static List<Offscreen> OffscreenMarkers(IEnumerable<(string Label, long Ticks)> markers, long lo, long hi) =>
        markers.Where(m => m.Ticks > hi || m.Ticks < lo)
            .Select(m => new Offscreen(m.Label, m.Ticks, m.Ticks > hi, m.Ticks > hi ? m.Ticks - hi : lo - m.Ticks))
            .ToList();
}

/// <summary>
/// Rolling window of spread samples: "is the spread wide FOR THIS MARKET RIGHT NOW", against its own
/// recent distribution instead of a hard-coded tick count.
/// </summary>
public sealed class SpreadWindow
{
    private readonly Queue<(long At, long Ticks)> samples = new();
    private readonly long windowMs;
    public SpreadWindow(long windowMs = 5 * 60_000) => this.windowMs = windowMs;

    public void Push(long spreadTicks, long now)
    {
        samples.Enqueue((now, spreadTicks));
        while (samples.Count > 0 && samples.Peek().At < now - windowMs) samples.Dequeue();
    }

    public int Size => samples.Count;

    /// <summary>q-quantile of the window, or null with too little history to judge.</summary>
    public long? Quantile(double q, int minSamples = 60)
    {
        if (samples.Count < minSamples) return null;
        var s = samples.Select(x => x.Ticks).OrderBy(x => x).ToArray();
        return s[Math.Min(s.Length - 1, (int)Math.Floor(q * s.Length))];
    }

    /// <summary>True when spread is wider than this market's own recent p90.</summary>
    public bool IsWide(long spread) => Quantile(0.9) is long p90 && spread > p90;
}
