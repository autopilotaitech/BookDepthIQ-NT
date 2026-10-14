using System.Text.Json;

namespace BookDepthIQ.Core;

// Dealer gamma (GEX) levels from CBOE's free delayed option chains. Port of BookDepthIQ
// crates/hmd-agent/src/gex.rs (ALGO_VERSION 4) and its design notes
// (docs/superpowers/specs/2026-08-17-gex-levels-design.md). Pure, except the store's optional cache file.
//
//   ES, MES  -> _SPX, converted by SPREAD (futures = cash + carry)
//   NQ, MNQ  -> QQQ,  converted by RATIO  (futures = cash x ~41)
//   YM, MYM  -> DIA,  RATIO      GC, MGC -> GLD, RATIO      CL, MCL -> USO, RATIO
//
// Levels are computed in CASH space and moved into futures prices at the edge, with a median-smoothed
// spread/ratio measured against the live futures price. No conversion yet = draw NOTHING: a level at the
// wrong price is worse than no level. GEX is read-only decoration; it never touches the order path.

public enum GexConversion { Spread, Ratio }

public sealed record GexSource(string Symbol, GexConversion Conversion)
{
    public string Url => $"https://cdn.cboe.com/api/global/delayed_quotes/options/{Symbol}.json";
    public string Name => Symbol.TrimStart('_');

    /// <summary>The chain behind a futures root, or null (nothing is fetched for an unknown product).</summary>
    public static GexSource? ForRoot(string root) => root.ToUpperInvariant() switch
    {
        "ES" or "MES" => new("_SPX", GexConversion.Spread),
        "NQ" or "MNQ" => new("QQQ", GexConversion.Ratio),
        "YM" or "MYM" => new("DIA", GexConversion.Ratio),
        "GC" or "MGC" => new("GLD", GexConversion.Ratio),
        "CL" or "MCL" => new("USO", GexConversion.Ratio),
        _ => null,
    };
}

/// <summary>One contract, only the fields GEX needs. Expiry YYMMDD; IV a decimal; delta signed.</summary>
public readonly record struct GexContract(double Strike, int Expiry, bool IsCall, double Gamma, double Oi, double Iv, double Delta);

/// <summary>A computed level set, cash prices. Levels: strongest |GEX| first, (price, gex $).</summary>
public sealed record GexLevelSet(double? Flip, double? CallWall, double? PutWall, double? Magnet, IReadOnlyList<(double Price, double Gex)> Levels, double NetGex)
{
    public static readonly GexLevelSet Empty = new(null, null, null, null, Array.Empty<(double, double)>(), 0);
    public bool HasSignal => Flip != null || CallWall != null || PutWall != null || Levels.Count > 0;
}

public sealed class GexSnapshot
{
    public string Timestamp { get; init; } = "";
    public string Symbol { get; init; } = "";
    public double Spot { get; init; }
    public GexLevelSet All { get; init; } = GexLevelSet.Empty;
    /// <summary>The first NEAR_EXPIRIES expiries after today: the default view.</summary>
    public GexLevelSet Near { get; init; } = GexLevelSet.Empty;
    public GexLevelSet ZeroDte { get; init; } = GexLevelSet.Empty;
    public int? ZeroDteExpiry { get; init; }
    public double? RangeLow { get; init; }
    public double? RangeHigh { get; init; }
    public bool PositiveGamma { get; init; }
    public int Contracts { get; init; }
    public int AlgoVersion { get; init; }
    /// <summary>Vintage fingerprint: total open interest. OI is published once a day and frozen within
    /// a session, so a change here is exactly the daily positioning update.</summary>
    public long TotalOi { get; init; }
    public bool Plausible => All.HasSignal || Near.HasSignal || ZeroDte.HasSignal;
}

public static class Gex
{
    /// <summary>Bump when the maths or selection changes (same as BookDepthIQ's ALGO_VERSION).</summary>
    public const int AlgoVersion = 4;
    /// <summary>The NEAR set spans the four nearest expirations (esgex.net / SpotGamma convention).</summary>
    public const int NearExpiries = 4;
    /// <summary>Numbered levels further than this many expected day-moves from spot are not drawn.</summary>
    public const double MaxLevelDistanceDays = 2.0;
    private const double ScanRate = 0.04;

    /// <summary>OSI symbol: ROOT + YYMMDD + C|P + strike×1000 (8 digits). Root not filtered (SPX+SPXW).</summary>
    public static (int Expiry, bool IsCall, double Strike)? ParseOsi(string sym)
    {
        if (sym.Length < 16) return null;
        var tail = sym[^15..];
        var root = sym[..^15];
        if (root.Length == 0 || !root.All(ch => char.IsAsciiLetter(ch) || ch == '^')) return null;
        if (!int.TryParse(tail[..6], out var exp)) return null;
        bool call;
        if (tail[6] == 'C') call = true;
        else if (tail[6] == 'P') call = false;
        else return null;
        if (!long.TryParse(tail[7..], out var milli)) return null;
        return (exp, call, milli / 1000.0);
    }

    /// <summary>$ gamma per 1% move, signed +call / −put (the feed's gamma is unsigned; the sign is ours,
    /// the standard public assumption that customers sell calls and buy puts).</summary>
    public static double ContractGex(in GexContract c, double spot)
    {
        var mag = c.Gamma * c.Oi * 100 * spot * spot * 0.01;
        return c.IsCall ? mag : -mag;
    }

    public static double BsGamma(double spot, double strike, double t, double iv, double rate)
    {
        if (!(spot > 0 && strike > 0 && t > 0 && iv > 0)) return 0;
        var sq = Math.Sqrt(t);
        var d1 = (Math.Log(spot / strike) + (rate + 0.5 * iv * iv) * t) / (iv * sq);
        return Math.Exp(-0.5 * d1 * d1) / Math.Sqrt(2 * Math.PI) / (spot * iv * sq);
    }

    private static DateTime SafeDate(int d)
    {
        var y = Math.Clamp(2000 + d / 10000, 2000, 2099);
        var m = Math.Clamp(d / 100 % 100, 1, 12);
        return new DateTime(y, m, Math.Clamp(d % 100, 1, DateTime.DaysInMonth(y, m)), 0, 0, 0, DateTimeKind.Utc);
    }

    /// <summary>Years to expiry; never zero (a 0DTE contract has hours of life: half a day).</summary>
    public static double YearsToExpiry(int today, int expiry) => Math.Max(0.5, Math.Max(0, (SafeDate(expiry) - SafeDate(today)).TotalDays)) / 365.0;

    /// <summary>Deep-ITM and degenerate-IV contracts carry OI with no real hedging behind them.</summary>
    public static bool ScanEligible(in GexContract c) => c.Oi > 0 && c.Iv > 0.01 && c.Iv < 2.0 && Math.Abs(c.Delta) < 0.95;

    /// <summary>The zero-gamma spot (standard "gamma flip"): re-price every contract's gamma at candidate
    /// spots ±3% and interpolate where total signed exposure crosses zero. Null when it never crosses.</summary>
    public static double? ZeroGammaSpot(IReadOnlyList<GexContract> contracts, double spot, int today)
    {
        if (!(spot > 0)) return null;
        const int steps = 60;
        const double span = 0.03;
        var eligible = contracts.Where(c => ScanEligible(c)).ToArray();
        if (eligible.Length == 0) return null;
        var t = eligible.Select(c => YearsToExpiry(today, c.Expiry)).ToArray();
        double Total(double s)
        {
            double sum = 0;
            for (var i = 0; i < eligible.Length; i++)
            {
                var c = eligible[i];
                var mag = BsGamma(s, c.Strike, t[i], c.Iv, ScanRate) * c.Oi * 100 * s * s * 0.01;
                sum += c.IsCall ? mag : -mag;
            }
            return sum;
        }
        double lo = spot * (1 - span), hi = spot * (1 + span), step = (hi - lo) / steps;
        double ps = lo, pv = Total(lo);
        for (var i = 1; i <= steps; i++)
        {
            var s = lo + step * i;
            var v = Total(s);
            if ((pv < 0 && v >= 0) || (pv > 0 && v <= 0))
            {
                var d = v - pv;
                return Math.Abs(d) > double.Epsilon ? ps + (s - ps) * (-pv / d) : s;
            }
            ps = s;
            pv = v;
        }
        return null;
    }

    /// <summary>Per-strike profile, keyed in milli-points. Net, call side (+) and put side (+ magnitude).</summary>
    public sealed class Profile
    {
        public SortedDictionary<long, double> ByStrike { get; } = new();
        public SortedDictionary<long, double> Calls { get; } = new();
        public SortedDictionary<long, double> Puts { get; } = new();
        public double Net { get; set; }
    }

    private static long Key(double strike) => (long)Math.Round(strike * 1000);
    private static double Unkey(long k) => k / 1000.0;
    private static void Add(SortedDictionary<long, double> m, long k, double v) => m[k] = (m.TryGetValue(k, out var x) ? x : 0) + v;

    /// <summary>Profile over a set of expiries; null/empty = the whole chain.</summary>
    public static Profile BuildProfile(IEnumerable<GexContract> contracts, double spot, ICollection<int>? expiries = null)
    {
        var p = new Profile();
        foreach (var c in contracts)
        {
            if (expiries is { Count: > 0 } && !expiries.Contains(c.Expiry)) continue;
            if (c.Oi == 0 || c.Gamma == 0) continue;
            var g = ContractGex(c, spot);
            var k = Key(c.Strike);
            Add(p.ByStrike, k, g);
            if (c.IsCall) Add(p.Calls, k, g); else Add(p.Puts, k, -g);
            p.Net += g;
        }
        return p;
    }

    /// <summary>Fallback flip: where the cumulative per-strike profile crosses zero, interpolated.</summary>
    public static double? CumulativeFlip(Profile p)
    {
        double cum = 0;
        double? prevStrike = null;
        foreach (var (k, v) in p.ByStrike)
        {
            var prev = cum;
            cum += v;
            var strike = Unkey(k);
            if ((prev < 0 && cum >= 0) || (prev > 0 && cum <= 0))
            {
                var span = cum - prev;
                return prevStrike is double ps && Math.Abs(span) > double.Epsilon ? ps + (strike - ps) * Math.Clamp(-prev / span, 0, 1) : strike;
            }
            prevStrike = strike;
        }
        return null;
    }

    /// <summary>Call wall: heaviest call gamma above spot. Put wall: heaviest put gamma below.</summary>
    public static (double? Call, double? Put) Walls(Profile p, double spot)
    {
        double? call = null, put = null;
        double bc = 0, bp = 0;
        foreach (var (k, v) in p.Calls) if (Unkey(k) > spot && v > 0 && v > bc) { bc = v; call = Unkey(k); }
        foreach (var (k, v) in p.Puts) if (Unkey(k) < spot && v > 0 && v > bp) { bp = v; put = Unkey(k); }
        return (call, put);
    }

    /// <summary>The magnet: strike with the largest combined |call| + |put| gamma (the ATM pin).</summary>
    public static double? Magnet(Profile p)
    {
        var m = new Dictionary<long, double>();
        foreach (var (k, v) in p.Calls) m[k] = (m.TryGetValue(k, out var x) ? x : 0) + Math.Abs(v);
        foreach (var (k, v) in p.Puts) m[k] = (m.TryGetValue(k, out var x) ? x : 0) + Math.Abs(v);
        return m.Count == 0 ? null : Unkey(m.MaxBy(kv => kv.Value).Key);
    }

    /// <summary>The n strongest strikes by |GEX|, skipping any already drawn as a wall or the magnet.</summary>
    public static List<(double Price, double Gex)> TopLevels(Profile p, int n, params double?[] exclude)
    {
        var skip = exclude.Where(x => x != null).Select(x => Key(x!.Value)).ToHashSet();
        return p.ByStrike.Where(kv => !skip.Contains(kv.Key)).Select(kv => (Unkey(kv.Key), kv.Value))
            .OrderByDescending(x => Math.Abs(x.Value)).Take(n).ToList();
    }

    /// <summary>One-day expected move band from iv30 — a PERCENTAGE in this feed.</summary>
    public static (double Low, double High)? ExpectedRange(double spot, double iv30Percent)
    {
        if (!(double.IsFinite(iv30Percent) && iv30Percent > 0 && spot > 0)) return null;
        var day = spot * (iv30Percent / 100) * Math.Sqrt(1.0 / 252);
        return (spot - day, spot + day);
    }

    private static GexLevelSet LevelSet(Profile p, double spot, int topN, double? dayMove, double? zeroGamma)
    {
        var (cw, pw) = Walls(p, spot);
        var mag = Magnet(p);
        var levels = TopLevels(p, topN * 4, cw, pw, mag)
            .Where(l => dayMove is not double d || d <= 0 || Math.Abs(l.Price - spot) <= d * MaxLevelDistanceDays)
            .Take(topN).ToList();
        return new GexLevelSet(zeroGamma ?? CumulativeFlip(p), cw, pw, mag, levels, p.Net);
    }

    /// <summary>YYMMDD from a payload timestamp like "2026-08-17 16:28:16"; else the earliest expiry.</summary>
    public static int PayloadDate(string timestamp, IReadOnlyList<GexContract> contracts)
    {
        var d = timestamp.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Split('-');
        if (d is { Length: 3 } && int.TryParse(d[0], out var y) && int.TryParse(d[1], out var m) && int.TryParse(d[2], out var day))
            return y % 100 * 10000 + m * 100 + day;
        return contracts.Count > 0 ? contracts.Min(c => c.Expiry) : 0;
    }

    public static GexSnapshot Compute(string symbol, string timestamp, double spot, double iv30, IReadOnlyList<GexContract> contracts, int topN = 7)
    {
        var today = PayloadDate(timestamp, contracts);
        int? zero = contracts.Count > 0 && contracts.Min(c => c.Expiry) == today ? today : null;
        var near = contracts.Select(c => c.Expiry).Distinct().OrderBy(e => e).Where(e => e != zero).Take(NearExpiries).ToHashSet();
        var range = ExpectedRange(spot, iv30);
        double? dayMove = range is var (_, h) ? h - spot : null;
        var nearC = contracts.Where(c => near.Contains(c.Expiry)).ToList();
        var zeroC = zero is int z ? contracts.Where(c => c.Expiry == z).ToList() : new List<GexContract>();
        var all = LevelSet(BuildProfile(contracts, spot), spot, topN, dayMove, ZeroGammaSpot(contracts, spot, today));
        return new GexSnapshot
        {
            Timestamp = timestamp, Symbol = symbol, Spot = spot,
            All = all,
            Near = LevelSet(BuildProfile(nearC, spot), spot, topN, dayMove, ZeroGammaSpot(nearC, spot, today)),
            ZeroDte = zero is null ? GexLevelSet.Empty : LevelSet(BuildProfile(zeroC, spot), spot, topN, dayMove, ZeroGammaSpot(zeroC, spot, today)),
            ZeroDteExpiry = zero,
            RangeLow = range?.Low, RangeHigh = range?.High,
            PositiveGamma = all.NetGex >= 0,
            Contracts = contracts.Count, AlgoVersion = AlgoVersion,
            TotalOi = (long)contracts.Sum(c => c.Oi),
        };
    }

    /// <summary>Parse CBOE's payload. Null for anything malformed (never a half-parsed chain).</summary>
    public static (string Timestamp, double Spot, double Iv30, List<GexContract> Contracts)? ParsePayload(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var ts = root.GetProperty("timestamp").GetString() ?? "";
            var data = root.GetProperty("data");
            var spot = data.GetProperty("current_price").GetDouble();
            var iv30 = data.TryGetProperty("iv30", out var iv) && iv.ValueKind == JsonValueKind.Number ? iv.GetDouble() : 0;
            var list = new List<GexContract>();
            foreach (var o in data.GetProperty("options").EnumerateArray())
            {
                if (!o.TryGetProperty("option", out var sym) || sym.ValueKind != JsonValueKind.String) continue;
                if (ParseOsi(sym.GetString()!) is not var (exp, call, strike)) continue;
                double N(string name) => o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
                list.Add(new GexContract(strike, exp, call, N("gamma"), N("open_interest"), N("iv"), N("delta")));
            }
            return list.Count == 0 ? null : (ts, spot, iv30, list);
        }
        catch { return null; }
    }

    /// <summary>Cash level → futures price on the instrument tick (spread adds, ratio multiplies).</summary>
    public static double ToFutures(double cash, double basis, GexConversion mode, double tick)
    {
        var raw = mode == GexConversion.Spread ? cash + basis : cash * basis;
        return tick > 0 ? Math.Round(raw / tick) * tick : raw;
    }
}

/// <summary>Futures-to-cash basis: the MEDIAN of many paired samples (the payload's spot is delayed; a
/// single subtraction would absorb however far price moved meanwhile and make every level drift).
/// Null until there are enough samples: draw nothing.</summary>
public sealed class GexBasis
{
    private readonly Queue<double> samples = new();
    private readonly int cap, min;
    public GexConversion Mode { get; }

    public GexBasis(GexConversion mode, int cap = 1200, int minSamples = 30)
    {
        Mode = mode;
        this.cap = cap;
        min = minSamples;
    }

    public int Count => samples.Count;

    public void Sample(double futures, double cash)
    {
        if (!(double.IsFinite(futures) && double.IsFinite(cash) && futures > 0 && cash > 0)) return;
        if (samples.Count == cap) samples.Dequeue();
        samples.Enqueue(Mode == GexConversion.Spread ? futures - cash : futures / cash);
    }

    public double? Value
    {
        get
        {
            if (samples.Count < min) return null;
            var v = samples.OrderBy(x => x).ToArray();
            var n = v.Length;
            return n % 2 == 1 ? v[n / 2] : (v[n / 2 - 1] + v[n / 2]) / 2;
        }
    }

    public void Reset() => samples.Clear();
}

/// <summary>GEX levels already in futures prices.</summary>
public sealed record GexView(GexLevelSet Set, double? RangeLow, double? RangeHigh, double Basis, GexConversion Mode, GexSnapshot Snapshot);

/// <summary>
/// One chain's state. THE TRANSITION RULE: a new computation replaces the held one only when the
/// vintage (total OI, i.e. the daily positioning update) or the algorithm changed — not because price
/// moved, which would re-rank a fixed book against a moving yardstick and walk the lines around.
/// The conversion is frozen WITH the vintage, so the lines do not creep with a live ratio either.
/// </summary>
public sealed class GexStore
{
    private readonly object gate = new();
    private readonly GexBasis basis;
    private GexSnapshot? frozen;
    private double? frozenBasis;
    private double? latestSpot;
    public GexSource Source { get; }
    /// <summary>Most recent computation (the "recalculated" map), whatever its vintage.</summary>
    public GexSnapshot? Live { get; private set; }
    public GexSnapshot? Frozen { get { lock (gate) return frozen; } }
    public double? FrozenBasis { get { lock (gate) return frozenBasis; } }
    public int BasisSamples { get { lock (gate) return basis.Count; } }

    public GexStore(GexSource source, int minSamples = 30)
    {
        Source = source;
        basis = new GexBasis(source.Conversion, 1200, minSamples);
    }

    /// <summary>The index spot from every successfully parsed poll (accepted or not).</summary>
    public void NoteSpot(double spot)
    {
        if (double.IsFinite(spot) && spot > 0) lock (gate) latestSpot = spot;
    }

    /// <summary>One live futures price, paired with the latest polled spot. No-op before the first poll.</summary>
    public void SampleBasis(double futures)
    {
        lock (gate)
        {
            if (latestSpot is not double sp) return;
            basis.Sample(futures, sp);
            if (frozenBasis == null && frozen != null) frozenBasis = basis.Value;
        }
    }

    /// <summary>Offer a fresh computation. True when the held levels changed.</summary>
    public bool Accept(GexSnapshot snap)
    {
        lock (gate)
        {
            if (snap.Plausible) Live = snap;
            if (frozen != null && frozen.TotalOi == snap.TotalOi && frozen.AlgoVersion == snap.AlgoVersion) return false;
            // CBOE's CDN can serve a payload mid-publish: real OI, but nothing computes. Never freeze that.
            if (!snap.Plausible) return false;
            frozen = snap;
            frozenBasis = basis.Value;
            return true;
        }
    }

    /// <summary>Restore a cached vintage + its conversion (a restart must not move the lines).</summary>
    public bool Restore(GexSnapshot snap, double? savedBasis)
    {
        if (snap.AlgoVersion != Gex.AlgoVersion || snap.Contracts < 500 || snap.TotalOi == 0 || !snap.Plausible) return false;
        lock (gate)
        {
            frozen = snap;
            frozenBasis = savedBasis;
        }
        return true;
    }

    /// <summary>Contract roll / instrument switch: the old basis means nothing for the new contract. The
    /// index spot stays: it doesn't depend on the contract, and CBOE answers 304 (no new spot) until it
    /// publishes again, so clearing it would stall the basis — and the levels — until then.</summary>
    public void ResetBasis()
    {
        lock (gate)
        {
            basis.Reset();
            frozenBasis = null;
        }
    }

    public enum Scope { Near, ZeroDte, All }

    /// <summary>Levels in futures prices, or null with no snapshot or no conversion yet.</summary>
    public GexView? View(double tick, Scope scope, bool live)
    {
        GexSnapshot? snap;
        double? b;
        lock (gate)
        {
            snap = live && Live != null ? Live : frozen;
            b = frozenBasis;
        }
        if (snap == null || b is not double bv) return null;
        var mode = Source.Conversion;
        double? C(double? v) => v is double x ? Gex.ToFutures(x, bv, mode, tick) : null;
        var set = scope switch { Scope.ZeroDte => snap.ZeroDte, Scope.All => snap.All, _ => snap.Near };
        var conv = new GexLevelSet(C(set.Flip), C(set.CallWall), C(set.PutWall), C(set.Magnet),
            set.Levels.Select(l => (Gex.ToFutures(l.Price, bv, mode, tick), l.Gex)).ToList(), set.NetGex);
        return new GexView(conv, C(snap.RangeLow), C(snap.RangeHigh), bv, mode, snap);
    }
}
