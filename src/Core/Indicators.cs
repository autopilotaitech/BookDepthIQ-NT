using System.Text.RegularExpressions;

namespace BookDepthIQ.Core;

// Session VWAP with σ bands, per-instrument indicator settings and stacked-imbalance zones. Pure.
// Port of BookDepthIQ-Vest src/panel/vwap.ts and indicators.ts.

/// <summary>VWAP = Σ(price × qty) / Σqty; σ = sqrt(Σ(qty × price²) / Σqty − VWAP²); bands = VWAP ± kσ.</summary>
public sealed class Vwap
{
    private double v, pv, pv2;
    public int N { get; private set; }

    public void Add(double price, double qty)
    {
        if (!(qty > 0) || !double.IsFinite(price)) return;
        v += qty; pv += price * qty; pv2 += price * price * qty; N++;
    }

    public void Reset() { v = pv = pv2 = 0; N = 0; }
    public double? Value => v > 0 ? pv / v : null;
    public double? Sigma => Value is double m ? Math.Sqrt(Math.Max(0, pv2 / v - m * m)) : null;

    public List<(double K, double Lo, double Hi)> Bands(IEnumerable<double> mults) =>
        Value is double m && Sigma is double sd ? mults.Where(k => k > 0).Select(k => (k, m - k * sd, m + k * sd)).ToList() : new();
}

public static class Session
{
    /// <summary>"HH:MM" → minutes after midnight, or null.</summary>
    public static int? ParseHhmm(string s)
    {
        var m = Regex.Match((s ?? "").Trim(), @"^(\d{1,2}):(\d{2})$");
        if (!m.Success) return null;
        int h = int.Parse(m.Groups[1].Value), mi = int.Parse(m.Groups[2].Value);
        return h < 24 && mi < 60 ? h * 60 + mi : null;
    }

    public static TimeZoneInfo Zone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time"); }
    }

    /// <summary>The session window at now: from the latest start (wall clock in tz) at or before now to the
    /// next end after it (an end before the start crosses midnight). After the end the window is the
    /// finished session, so VWAP stays frozen until the next start. Epoch ms.</summary>
    public static (long From, long To)? Window(long nowMs, string start, string end, string tz = "America/New_York")
    {
        if (ParseHhmm(start) is not int s || ParseHhmm(end) is not int e) return null;
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nowMs), Zone(tz));
        var min = local.Hour * 60 + local.Minute;
        var sinceStart = ((min - s) % 1440 + 1440) % 1440;
        var from = nowMs / 60_000 * 60_000 - sinceStart * 60_000L;
        var len = ((e - s) % 1440 + 1440) % 1440;
        if (len == 0) len = 1440;
        return (from, from + len * 60_000L);
    }
}

/// <summary>Per-instrument indicator settings (defaults from the Vest panel, CME-tuned where noted).</summary>
public sealed record IndicatorCfg
{
    public bool VwapOn { get; init; } = true;
    public string VwapStart { get; init; } = "09:30";
    public string VwapEnd { get; init; } = "16:00";
    public string VwapTz { get; init; } = "America/New_York";
    public double[] VwapBands { get; init; } = { 1, 2 };
    /// <summary>Volume profile, Δ and footprint count prints from the latest "HH:MM" (VWAP zone); "" = from
    /// when the DOM opened or switched market (owner, 2026-10-13).</summary>
    public string ProfileStart { get; init; } = "";
    /// <summary>Backfill the profile from the start time with NinjaTrader's history. Off = live prints only.</summary>
    public bool ProfileHistory { get; init; }
    public bool FpOn { get; init; } = true;
    /// <summary>Lights a Sold/Bought cell at this diagonal ratio (3 = 3:1).</summary>
    public double FpRatio { get; init; } = 3;
    /// <summary>A row must trade at least this % of the busiest row to count.</summary>
    public double FpMinPct { get; init; } = 5;
    public int FpStack { get; init; } = 3;
    public bool FpLines { get; init; } = true;
    public bool BigOn { get; init; } = true;
    /// <summary>A print this many contracts or more is "big" on the tape (NQ: 10; tune per market).</summary>
    public long BigMin { get; init; } = 10;
    /// <summary>RTH opening range (BookDepthIQ PAXOR), NQ and ES.</summary>
    public bool OrOn { get; init; } = true;
    public bool OrMid { get; init; }

    public static double[] ParseBands(string s) =>
        Regex.Split(s ?? "", @"[,\s]+").Select(x => double.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0)
            .Where(x => x > 0 && double.IsFinite(x)).Take(3).ToArray();

    /// <summary>Stacked-imbalance zones: a support line at each buy run's lowest row, a resistance line at
    /// each sell run's highest row.</summary>
    public static List<(long Ticks, bool Buy, int Rows)> StackZones(HashSet<long> buyStack, HashSet<long> sellStack, int g)
    {
        var zones = new List<(long, bool, int)>();
        void Walk(HashSet<long> set, bool buy)
        {
            var rows = set.OrderBy(x => x).ToArray();
            var start = 0;
            for (var i = 1; i <= rows.Length; i++)
            {
                if (i < rows.Length && rows[i] - rows[i - 1] == g) continue;
                if (i > start) zones.Add((buy ? rows[start] : rows[i - 1], buy, i - start));
                start = i;
            }
        }
        Walk(buyStack, true);
        Walk(sellStack, false);
        return zones;
    }
}
