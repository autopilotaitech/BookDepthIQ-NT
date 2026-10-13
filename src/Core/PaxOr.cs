using System.Text.RegularExpressions;

namespace BookDepthIQ.Core;

// RTH opening range — a port of BookDepthIQ's PAXOR (computePaxOr, RTH mode) via
// BookDepthIQ-Vest src/panel/paxor.ts. The OR is the high/low of trades in [08:30:00, 08:30:30)
// Chicago; it stays until the next weekday bell. EXT rungs start levelPoints beyond OR H / OR L
// (NQ 65, ES 15) and grow one more step each time price strictly breaks the outermost rung.
// NQ and ES families only. Keep in step with paxor.ts in BookDepthIQ.

public sealed record PaxOrCfg
{
    public bool Enabled { get; init; } = true;
    public string Timezone { get; init; } = "America/Chicago";
    public string StartTime { get; init; } = "08:30:00";
    public int DurationSec { get; init; } = 30;
    public string EndTime { get; init; } = "17:00:00";
    public bool ShowMid { get; init; }
    public double EsLevelPoints { get; init; } = 15;
    public double NqLevelPoints { get; init; } = 65;
}

public enum OrKind { High, Low, Mid, Upper, Lower }

public sealed record OrLevel(OrKind Kind, long Ticks, int Index)
{
    public string Label => Kind switch
    {
        OrKind.Mid => "OR MID",
        OrKind.Upper or OrKind.Lower => $"OR EXT{Index}",
        OrKind.High => "OR H",
        _ => "OR L",
    };
}

public sealed record OrSession(long Start, long OrbEnd, long End);

public sealed class PaxOr
{
    /// <summary>"NQ" / "ES" for the NQ and ES families (incl. micros), else "" (OR not shown).</summary>
    public static string OrRoot(string? instrument)
    {
        var u = (instrument ?? "").ToUpperInvariant();
        var m = Regex.Match(u, @"\b(MES|MNQ|ES|NQ)\b");
        if (!m.Success) m = Regex.Match(u, @"^(MES|MNQ|ES|NQ)");
        if (!m.Success) return "";
        return m.Groups[1].Value is "NQ" or "MNQ" ? "NQ" : "ES";
    }

    public static long LevelFactorTicks(string root, PaxOrCfg cfg, double tickSize)
    {
        var pts = root == "NQ" ? cfg.NqLevelPoints : root == "ES" ? cfg.EsLevelPoints : 0;
        return pts > 0 && tickSize > 0 ? (long)Math.Round(pts / tickSize) : 0;
    }

    private static string Hhmm(string s) { var m = Regex.Match(s.Trim(), @"^(\d{1,2}):(\d{2})"); return m.Success ? $"{m.Groups[1].Value}:{m.Groups[2].Value}" : s; }
    private static int Secs(string s) { var m = Regex.Match(s.Trim(), @"^(\d{1,2}):(\d{2})(?::(\d{2}))?$"); return m.Success && m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0; }
    private static DayOfWeek WeekdayAt(long ms, string tz) => TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), BookDepthIQ.Core.Session.Zone(tz)).DayOfWeek;
    private static bool Weekend(DayOfWeek d) => d is DayOfWeek.Saturday or DayOfWeek.Sunday;

    /// <summary>The OR that applies at now: the most recent weekday bell at or before now, its window, and
    /// when the next weekday bell replaces it.</summary>
    public static OrSession? SessionAt(long now, PaxOrCfg cfg)
    {
        if (BookDepthIQ.Core.Session.Window(now, Hhmm(cfg.StartTime), Hhmm(cfg.EndTime), cfg.Timezone) is not var (from, _)) return null;
        const long day = 86_400_000;
        var start = from + Secs(cfg.StartTime) * 1000L;
        if (start > now) start -= day;
        for (var i = 0; i < 3 && Weekend(WeekdayAt(start, cfg.Timezone)); i++) start -= day;
        var next = start + day;
        for (var i = 0; i < 3 && Weekend(WeekdayAt(next, cfg.Timezone)); i++) next += day;
        return new OrSession(start, start + Math.Max(1, cfg.DurationSec) * 1000L, next);
    }

    public OrSession Session { get; }
    private readonly long factorTicks;
    private readonly bool showMid;
    private long high = long.MinValue, low = long.MaxValue;
    private long? upper, lower;
    private readonly List<long> uppers = new(), lowers = new();

    public PaxOr(OrSession session, long factorTicks, bool showMid)
    {
        Session = session;
        this.factorTicks = factorTicks;
        this.showMid = showMid;
    }

    public bool Formed => high != long.MinValue && low != long.MaxValue;

    public void Add(long timeMs, long priceTicks)
    {
        if (priceTicks <= 0 || timeMs < Session.Start || timeMs > Session.End) return;
        if (timeMs < Session.OrbEnd)
        {
            high = Math.Max(high, priceTicks);
            low = Math.Min(low, priceTicks);
            return;
        }
        if (!Formed || factorTicks <= 0) return;
        upper ??= high + factorTicks;
        lower ??= low - factorTicks;
        if (priceTicks > upper) { upper += factorTicks; uppers.Add(upper.Value); }
        if (priceTicks < lower) { lower -= factorTicks; lowers.Add(lower.Value); }
    }

    /// <summary>OR H, OR L, [MID], the first rung each side, then EXT2, EXT3 …</summary>
    public List<OrLevel> Levels()
    {
        if (!Formed) return new();
        var o = new List<OrLevel> { new(OrKind.High, high, 0), new(OrKind.Low, low, 0) };
        if (showMid) o.Add(new(OrKind.Mid, (long)Math.Round(low + (high - low) * 0.5), 0));
        if (factorTicks > 0)
        {
            o.Add(new(OrKind.Upper, high + factorTicks, 1));
            o.Add(new(OrKind.Lower, low - factorTicks, 1));
            for (var i = 0; i < uppers.Count; i++) o.Add(new(OrKind.Upper, uppers[i], i + 2));
            for (var i = 0; i < lowers.Count; i++) o.Add(new(OrKind.Lower, lowers[i], i + 2));
        }
        return o;
    }

    public bool Forming(long now) => now >= Session.Start && now < Session.OrbEnd;
}
