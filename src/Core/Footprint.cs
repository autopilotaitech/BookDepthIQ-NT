namespace BookDepthIQ.Core;

// Footprint, liquidity marks and the session volume profile. Pure.
// Port of BookDepthIQ-Vest src/panel/footprint.ts and profile.ts. Rows are price buckets in ticks,
// g ticks apart. "Bought" lifted the ask at that price, "sold" hit the bid.

public sealed record Imbalances(HashSet<long> Buy, HashSet<long> Sell, HashSet<long> BuyStack, HashSet<long> SellStack)
{
    public static readonly Imbalances Empty = new(new(), new(), new(), new());
}

public sealed record Profile(long? Poc, long? Vah, long? Val, double MaxVol, double MaxAbsDelta, double TotalVol)
{
    public static readonly Profile Empty = new(null, null, null, 0, 0, 0);
}

public static class Footprint
{
    /// <summary>
    /// Diagonal imbalances: a buyer lifting the ask at P vs a seller hitting the bid one row lower.
    /// minQty: a row must trade at least this much; the empty side counts as minQty. A lit cell stays
    /// lit until its ratio drops below ratio × release (no blinking).
    /// </summary>
    public static Imbalances Find(IReadOnlyDictionary<long, double> bought, IReadOnlyDictionary<long, double> sold, int g,
        double ratio = 3, double minQty = 0, int stackMin = 3, Imbalances? prev = null, double release = 0.75)
    {
        var buy = new HashSet<long>();
        var sell = new HashSet<long>();
        double Need(bool lit) => lit ? ratio * release : ratio;
        foreach (var (t, b) in bought)
        {
            var r = Need(prev?.Buy.Contains(t) == true);
            var other = sold.TryGetValue(t - g, out var s) ? s : 0;
            if (b > 0 && b >= minQty && b >= r * Math.Max(Math.Max(other, minQty), 1e-12)) buy.Add(t);
        }
        foreach (var (t, s) in sold)
        {
            var r = Need(prev?.Sell.Contains(t) == true);
            var other = bought.TryGetValue(t + g, out var b) ? b : 0;
            if (s > 0 && s >= minQty && s >= r * Math.Max(Math.Max(other, minQty), 1e-12)) sell.Add(t);
        }
        return new Imbalances(buy, sell, Runs(buy, g, stackMin), Runs(sell, g, stackMin));
    }

    /// <summary>Members of runs of at least min consecutive rows (g apart).</summary>
    public static HashSet<long> Runs(HashSet<long> rows, int g, int min)
    {
        var o = new HashSet<long>();
        var sorted = rows.OrderBy(x => x).ToArray();
        var start = 0;
        for (var i = 1; i <= sorted.Length; i++)
        {
            if (i < sorted.Length && sorted[i] - sorted[i - 1] == g) continue;
            if (i - start >= min) for (var k = start; k < i; k++) o.Add(sorted[k]);
            start = i;
        }
        return o;
    }

    /// <summary>Levels resting at least mult × the average non-zero size on that side.</summary>
    public static HashSet<long> Walls(IReadOnlyDictionary<long, long> sizes, double mult = 3)
    {
        var vals = sizes.Values.Where(v => v > 0).ToList();
        var o = new HashSet<long>();
        if (vals.Count < 3) return o;
        var avg = vals.Average();
        foreach (var (t, v) in sizes) if (v >= mult * avg) o.Add(t);
        return o;
    }

    /// <summary>Share of visible resting size on the bid, 0–1 (null when the book is empty).</summary>
    public static double? BidShare(IEnumerable<long> bids, IEnumerable<long> asks)
    {
        double b = bids.Sum(), a = asks.Sum();
        return b + a > 0 ? b / (b + a) : null;
    }

    /// <summary>
    /// Session profile: POC (most-traded row) and a value area holding vaFraction of all volume, grown
    /// outward from the POC toward the heavier neighbour; and the largest |delta|.
    /// </summary>
    public static Profile BuildProfile(IReadOnlyDictionary<long, double> vol, IReadOnlyDictionary<long, double> delta, double vaFraction = 0.7)
    {
        var rows = vol.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).ToArray();
        var maxAbsDelta = delta.Values.Select(Math.Abs).DefaultIfEmpty(0).Max();
        if (rows.Length == 0) return Profile.Empty with { MaxAbsDelta = maxAbsDelta };
        var poc = 0;
        double total = 0;
        for (var i = 0; i < rows.Length; i++)
        {
            total += rows[i].Value;
            if (rows[i].Value > rows[poc].Value) poc = i;
        }
        var target = total * Math.Clamp(vaFraction, 0, 1);
        int lo = poc, hi = poc;
        var area = rows[poc].Value;
        while (area < target - 1e-12 && (lo > 0 || hi < rows.Length - 1))
        {
            var down = lo > 0 ? rows[lo - 1].Value : -1;
            var up = hi < rows.Length - 1 ? rows[hi + 1].Value : -1;
            if (down >= up) area += rows[--lo].Value;
            else area += rows[++hi].Value;
        }
        return new Profile(rows[poc].Key, rows[hi].Key, rows[lo].Key, rows[poc].Value, maxAbsDelta, total);
    }
}
