namespace BookDepthIQ.Core;

// Liquidity over time, per price: a heatmap of resting size (last 5 minutes in 10 s bins), and
// pull/stack — size that left the book WITHOUT trading (pulled) or arrived (stacked) — over the last
// minute. ABSORB marks a price one side kept hitting — more than was showing — while the level held
// and its size stayed. Pure. Port of BookDepthIQ-Vest src/panel/liquidity.ts. Prices in ticks.

public sealed record FlowRead(long Net, long Pulled, long Stacked, long Traded, string? Absorb, bool Signal);

public sealed class LiquidityTracker
{
    public const long BinMs = 10_000;
    public const int Bins = 30; // 5 minutes
    public const long FlowWindowMs = 60_000;
    /// <summary>Outermost book levels per side left out of pull/stack (they flicker in and out of view).</summary>
    public const int EdgeLevels = 3;

    private sealed record Hit(long At, long Price, long Qty, char Side, long Shown);
    private sealed record Flow(long At, long Pull, long Stack, long Traded);

    private Dictionary<long, long> cur = new();
    /// <summary>Finished 10 s bins: per price, time-averaged resting size, signed (+ bid, − offer), oldest first.</summary>
    private readonly List<Dictionary<long, double>> bins = new();
    private long? binStart;
    private Dictionary<long, long> curSigned = new();
    private readonly Dictionary<long, double> acc = new();
    private long? lastBookAt;
    private readonly Dictionary<long, long> tradedSince = new();
    private readonly Dictionary<long, List<Flow>> flows = new();
    private HashSet<long> edge = new();
    private List<Hit> hits = new();
    private long now;

    /// <summary>Largest resting size seen in the heat window (for colour scaling).</summary>
    public double MaxSize { get; private set; }

    public void OnTrade(long priceTicks, long qty, char side, long at)
    {
        tradedSince[priceTicks] = (tradedSince.TryGetValue(priceTicks, out var v) ? v : 0) + qty;
        now = Math.Max(now, at);
        if (side is 'b' or 's') hits.Add(new Hit(at, priceTicks, qty, side, cur.TryGetValue(priceTicks, out var sh) ? sh : 0));
    }

    public void OnBook(Book book, long at)
    {
        now = Math.Max(now, at);
        var next = new Dictionary<long, long>();
        for (var i = 0; i < book.Bids.Ticks.Length; i++) next[book.Bids.Ticks[i]] = book.Bids.Sizes[i];
        for (var i = 0; i < book.Asks.Ticks.Length; i++) next[book.Asks.Ticks[i]] = book.Asks.Sizes[i];
        // Only prices inside the range BOTH snapshots cover count, and not the flickering edge levels.
        static (long lo, long hi) Range(Dictionary<long, long> m)
        {
            long lo = long.MaxValue, hi = long.MinValue;
            foreach (var (p, v) in m) if (v > 0) { lo = Math.Min(lo, p); hi = Math.Max(hi, p); }
            return (lo, hi);
        }
        var a = Range(cur);
        var b = Range(next);
        var lo = Math.Max(a.lo, b.lo);
        var hi = Math.Min(a.hi, b.hi);
        var newEdge = book.Bids.Ticks.TakeLast(EdgeLevels).Concat(book.Asks.Ticks.TakeLast(EdgeLevels)).ToHashSet();
        var skip = new HashSet<long>(newEdge);
        skip.UnionWith(edge);
        var prices = cur.Count > 0 ? cur.Keys.Union(next.Keys).Where(p => p >= lo && p <= hi && !skip.Contains(p)).ToList() : new List<long>();
        edge = newEdge;
        foreach (var p in prices)
        {
            var before = cur.TryGetValue(p, out var x) ? x : 0;
            var after = next.TryGetValue(p, out var y) ? y : 0;
            var traded = tradedSince.TryGetValue(p, out var z) ? z : 0;
            var drop = before - after;
            var pull = drop > 0 ? Math.Max(0, drop - traded) : 0;
            var stack = after > before ? after - before : 0;
            if (pull > 0 || stack > 0 || traded > 0)
            {
                if (!flows.TryGetValue(p, out var list)) flows[p] = list = new List<Flow>();
                list.Add(new Flow(at, pull, stack, traded));
            }
        }
        tradedSince.Clear();

        // Heat: credit the book that was showing since the last update to the open bin, time-weighted.
        var bs = binStart ?? at;
        var last = lastBookAt ?? at;
        if (at - bs > (Bins + 1) * BinMs)
        {
            bs = at - (Bins + 1) * BinMs;
            last = Math.Max(last, bs);
        }
        void Credit(long ms)
        {
            if (ms <= 0) return;
            foreach (var (p, v) in curSigned) acc[p] = (acc.TryGetValue(p, out var c) ? c : 0) + (double)v * ms;
        }
        while (at - bs >= BinMs)
        {
            var end = bs + BinMs;
            Credit(end - last);
            last = end;
            var avg = new Dictionary<long, double>();
            foreach (var (p, v) in acc) if (v != 0) avg[p] = v / BinMs;
            bins.Add(avg);
            if (bins.Count > Bins) bins.RemoveAt(0);
            acc.Clear();
            bs = end;
        }
        Credit(at - last);
        binStart = bs;
        lastBookAt = at;
        cur = next;
        curSigned = new Dictionary<long, long>();
        for (var i = 0; i < book.Bids.Ticks.Length; i++) curSigned[book.Bids.Ticks[i]] = book.Bids.Sizes[i];
        for (var i = 0; i < book.Asks.Ticks.Length; i++) curSigned[book.Asks.Ticks[i]] = -book.Asks.Sizes[i];
        MaxSize = 0;
        foreach (var bin in bins) foreach (var v in bin.Values) MaxSize = Math.Max(MaxSize, v);
        foreach (var v in cur.Values) MaxSize = Math.Max(MaxSize, v);
        if (prices.Count > 0) Prune(at);
    }

    private void Prune(long at)
    {
        if (hits.Count > 0 && at - hits[0].At > FlowWindowMs) hits = hits.Where(h => at - h.At <= FlowWindowMs).ToList();
        foreach (var p in flows.Keys.ToList())
        {
            var keep = flows[p].Where(f => at - f.At <= FlowWindowMs).ToList();
            if (keep.Count > 0) flows[p] = keep;
            else flows.Remove(p);
        }
    }

    /// <summary>Resting size on the row price..price+group−1: one time-averaged value per 10 s bin, oldest
    /// first, then the book now. Signed: + bid, − offer (the side it was on THEN).</summary>
    public double[] Heat(long price, int group = 1)
    {
        double At(IReadOnlyDictionary<long, double> m)
        {
            double s = 0;
            for (var k = 0; k < group; k++) s += m.TryGetValue(price + k, out var v) ? v : 0;
            return s;
        }
        var o = new double[bins.Count + 1];
        for (var i = 0; i < bins.Count; i++) o[i] = At(bins[i]);
        double nowSum = 0;
        for (var k = 0; k < group; k++) nowSum += curSigned.TryGetValue(price + k, out var v) ? v : 0;
        o[^1] = nowSum;
        return o;
    }

    /// <summary>
    /// Absorption at the row over the last minute: "bid" when sellers kept hitting it and buyers soaked
    /// it up, "ask" the other way round. All of: one side's aggressive volume ≥ absorbMin and ≥ 2× the
    /// other's; it is ≥ 1.5× the size showing when the hitting began; the level held (no trade through
    /// it since); at least half that size still rests there now.
    /// </summary>
    public string? Absorb(long price, int group = 1, long absorbMin = 5)
    {
        var top = price + group - 1;
        var recent = hits.Where(h => now - h.At <= FlowWindowMs).ToList();
        var here = recent.Where(h => h.Price >= price && h.Price <= top).ToList();
        if (here.Count == 0) return null;
        var sells = here.Where(h => h.Side == 's').Sum(h => h.Qty);
        var buys = here.Where(h => h.Side == 'b').Sum(h => h.Qty);
        var side = sells >= absorbMin && sells >= 2 * buys ? "bid" : buys >= absorbMin && buys >= 2 * sells ? "ask" : null;
        if (side is null) return null;
        var hitSide = side == "bid" ? 's' : 'b';
        var first = here.First(h => h.Side == hitSide);
        var traded = side == "bid" ? sells : buys;
        long shown = 0;
        for (var p = price; p <= top; p++) shown = Math.Max(shown, here.FirstOrDefault(h => h.Side == hitSide && h.Price == p)?.Shown ?? 0);
        if (traded < 1.5 * shown) return null;
        if (recent.Any(h => h.At >= first.At && (side == "bid" ? h.Price < price : h.Price > top))) return null;
        long rest = 0;
        for (var p = price; p <= top; p++) rest += cur.TryGetValue(p, out var v) ? v : 0;
        if (rest <= 0 || rest < 0.5 * shown) return null;
        return side;
    }

    /// <summary>Over the last minute on the row: stacked − pulled, traded, absorption. Signal is false
    /// when the net is under 20% of the churn (re-quoting moves size without anyone pulling or adding).</summary>
    public FlowRead FlowAt(long price, int group = 1, long absorbMin = 5)
    {
        long pulled = 0, stacked = 0, traded = 0;
        for (var k = 0; k < group; k++)
            if (flows.TryGetValue(price + k, out var list))
                foreach (var f in list) { pulled += f.Pull; stacked += f.Stack; traded += f.Traded; }
        var net = stacked - pulled;
        return new FlowRead(net, pulled, stacked, traded, Absorb(price, group, absorbMin), Math.Abs(net) > 0 && Math.Abs(net) >= 0.2 * (pulled + stacked));
    }

    public void Reset()
    {
        cur.Clear(); bins.Clear(); binStart = null; lastBookAt = null; curSigned.Clear(); acc.Clear();
        edge.Clear(); tradedSince.Clear(); flows.Clear(); hits.Clear(); now = 0; MaxSize = 0;
    }
}
