namespace BookDepthIQ.Core;

// What an entry really costs from the live book, and the pre-trade checks. Pure.
// Port of BookDepthIQ-Vest src/panel/cost.ts and checks.ts, in CME terms: contracts, ticks, tick
// value and a fee per contract per side.

public sealed record Fill(double AvgTicks, long BestTicks, long WorstTicks, double SlipTicks, int Levels, bool Complete);

public sealed record RoundTrip(Fill Entry, Fill Exit, double CrossTicks, double FeesUsd, double TotalUsd, double BreakEvenTicks);

public enum Gate { Cheap, Normal, Wide }

public enum CheckLevel { Ok, Warn, Bad }

public sealed record TradeCheck(CheckLevel Level, string Text);

public sealed record CheckInput
{
    public int Contracts { get; init; }
    /// <summary>Average size of your last trades (journal); null with too few.</summary>
    public double? NormalContracts { get; init; }
    public double SizeMult { get; init; } = 2;
    /// <summary>Distance of a limit/stop from the mid in ticks (null for a market order).</summary>
    public long? DistTicks { get; init; }
    /// <summary>Distance at or above this many ticks warns (0 = off).</summary>
    public long FarTicks { get; init; }
    public Gate? Gate { get; init; }
    public long? SpreadTicks { get; init; }
    public int SlTicks { get; init; }
    public bool BracketsOn { get; init; }
    /// <summary>Ticks against the entry before the account fails at this size.</summary>
    public double? FailTicks { get; init; }
    /// <summary>Your own max contracts (0 = off).</summary>
    public int MaxContracts { get; init; }
}

public static class Cost
{
    /// <summary>Walk one side of the book (best first) for qty contracts.</summary>
    public static Fill? WalkBook(BookSide side, long qty)
    {
        if (qty <= 0 || side.Ticks.Length == 0) return null;
        var left = qty;
        double notional = 0;
        var levels = 0;
        var worst = side.Ticks[0];
        for (var i = 0; i < side.Ticks.Length && left > 0; i++)
        {
            var take = Math.Min(left, side.Sizes[i]);
            if (take <= 0) continue;
            notional += take * (double)side.Ticks[i];
            left -= take;
            levels++;
            worst = side.Ticks[i];
        }
        var filled = qty - Math.Max(0, left);
        if (filled <= 0) return null;
        var avg = notional / filled;
        return new Fill(avg, side.Ticks[0], worst, Math.Abs(avg - side.Ticks[0]), levels, left <= 0);
    }

    /// <summary>Round trip for an entry of qty: enter by walking the far side, exit by walking the near
    /// side, plus the fee per side each way. Break-even = ticks price must move your way to get to zero.</summary>
    public static RoundTrip? Trip(Book book, bool buy, long qty, double tickValue, double feePerSide)
    {
        var entry = WalkBook(buy ? book.Asks : book.Bids, qty);
        var exit = WalkBook(buy ? book.Bids : book.Asks, qty);
        if (entry is null || exit is null || tickValue <= 0) return null;
        var cross = Math.Abs(entry.AvgTicks - exit.AvgTicks);
        var fees = 2 * qty * Math.Max(0, feePerSide);
        var total = cross * qty * tickValue + fees;
        return new RoundTrip(entry, exit, cross, fees, total, total / (qty * tickValue));
    }

    /// <summary>Spread against this market's own recent spreads: ≤ median cheap, ≤ p90 normal, else wide.</summary>
    public static Gate? SpreadGate(long spread, long? p50, long? p90) =>
        p50 is null || p90 is null ? null : spread <= p50 ? Core.Gate.Cheap : spread <= p90 ? Core.Gate.Normal : Core.Gate.Wide;

    /// <summary>TP distance as a multiple of the all-in break-even move.</summary>
    public static double? EdgeRatio(double tpTicks, double? breakEvenTicks) => tpTicks > 0 && breakEvenTicks > 0 ? tpTicks / breakEvenTicks : null;

    /// <summary>Fat-finger checks: warnings only, they never block (owner, 2026-10-13).</summary>
    public static List<TradeCheck> Checks(CheckInput i)
    {
        var o = new List<TradeCheck>();
        if (i.NormalContracts is double n && n > 0)
        {
            var x = i.Contracts / n;
            o.Add(x >= i.SizeMult ? new TradeCheck(CheckLevel.Warn, $"size {x:0.0}× your normal ({n:0.#} ct)") : new TradeCheck(CheckLevel.Ok, "size normal"));
        }
        if (i.Gate == Core.Gate.Wide) o.Add(new TradeCheck(CheckLevel.Warn, $"spread {i.SpreadTicks?.ToString() ?? "?"} ticks — wide right now"));
        else if (i.Gate is Gate g) o.Add(new TradeCheck(CheckLevel.Ok, $"spread {i.SpreadTicks?.ToString() ?? "?"}t {g.ToString().ToLowerInvariant()}"));
        if (i.DistTicks is long d && i.FarTicks > 0 && Math.Abs(d) >= i.FarTicks) o.Add(new TradeCheck(CheckLevel.Warn, $"{Math.Abs(d)} ticks from the market — is the price right?"));
        if (i.BracketsOn && i.SlTicks > 0 && i.SpreadTicks is long sp && i.SlTicks <= sp) o.Add(new TradeCheck(CheckLevel.Bad, $"stop {i.SlTicks}t — inside the spread, fills at once"));
        if (i.BracketsOn && i.SlTicks > 0 && i.FailTicks is double f && i.SlTicks >= f) o.Add(new TradeCheck(CheckLevel.Bad, $"stop {i.SlTicks}t is past the fail line ({f:0}t) — the account fails first"));
        if (i.MaxContracts > 0 && i.Contracts > i.MaxContracts) o.Add(new TradeCheck(CheckLevel.Warn, $"{i.Contracts} ct — over your {i.MaxContracts} ct limit"));
        return o;
    }

    public static bool HasWarning(IEnumerable<TradeCheck> checks) => checks.Any(c => c.Level != CheckLevel.Ok);

    /// <summary>Market feed: live under 2 s since the last message, slow under 6 s, else stale.</summary>
    public static string FeedHealth(long? ageMs, bool connected) => !connected || ageMs is null ? "stale" : ageMs < 2000 ? "live" : ageMs < 6000 ? "slow" : "stale";

    /// <summary>Median and 95th percentile (null when empty).</summary>
    public static (long P50, long P95)? Percentiles(IEnumerable<long> ms)
    {
        var s = ms.OrderBy(x => x).ToArray();
        if (s.Length == 0) return null;
        long At(double q) => s[Math.Min(s.Length - 1, (int)Math.Floor(q * (s.Length - 1) + 0.5))];
        return (At(0.5), At(0.95));
    }
}
