using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BookDepthIQ.Core;

// Initial Balance (IB) — a port of BookDepthIQ's ib_store.rs + ib_history.rs (v1.0.146/147, branch
// claude/ninjatrader-ib-indicator-port). The IB is the high/low of the first N minutes after a session
// open, computed from every trade in the half-open window [open, open + N min). All 12 ranges
// ({RTH, Globex} × {1, 3, 5, 15, 30, 60} min) are tracked at once, so the session and length settings
// are an instant choice of what to draw. After a range forms: break / back-inside events (crossing
// against the previous trade, so a gap through a level still counts), delta since the latest break,
// and the window's POC / 70% value area. Keep in step with ib_store.rs.
//
// One difference from BookDepthIQ: the DOM feeds NinjaTrader's tick history first, so an IB that
// formed before the DOM opened is rebuilt from real prints instead of reported missed.

public enum IbSession { Rth, Globex }
public enum IbState { Idle, Missed, Building, Inside, Above, Below }
public enum IbEventKind { BrokeHigh, BrokeLow, BackInside }

public sealed record IbEvent(IbEventKind Kind, long Ms, long Ticks);

/// <summary>A range that closed on this trade. Partial ranges (tracking began after the open) are never recorded.</summary>
public sealed record IbFormed(IbSession Session, int LenMin, long AnchorMs, long High, long Low, bool Partial);

public sealed record IbRangeView(
    IbSession Session, int LenMin, long? WindowStart, long? WindowEnd, IbState State, bool Formed, bool Partial, bool Restored,
    long? High, long? Low, long? Poc, long? Val, long? Vah, IReadOnlyList<IbEvent> Events, long? DeltaSinceBreak);

public static class Ib
{
    public static readonly int[] Lengths = { 1, 3, 5, 15, 30, 60 };
    public static readonly double[] ExtensionMults = { 1.5, 2, 3 };
    public const int MaxEvents = 64;
    public const double ValueAreaPct = 0.70;
    public const string Tz = "America/Chicago";

    /// <summary>The RTH open (Chicago HH:MM[:SS]) for an instrument, as BookDepthIQ's per-family start
    /// fields: equity index futures use the OR bell (08:30), CL 08:00, metals 07:20, treasuries 07:20.</summary>
    public static string RthStart(string root, string orBell)
    {
        var r = (root ?? "").ToUpperInvariant();
        return r switch
        {
            "CL" or "MCL" or "QM" or "NG" or "RB" or "HO" => "08:00",
            "GC" or "MGC" or "SI" or "SIL" or "HG" or "MHG" or "PL" => "07:20",
            "ZB" or "ZN" or "ZF" or "ZT" or "UB" or "TN" => "07:20",
            _ => orBell,
        };
    }

    private static (int H, int M, int S)? Hms(string s)
    {
        var m = Regex.Match((s ?? "").Trim(), @"^(\d{1,2}):(\d{2})(?::(\d{2}))?$");
        if (!m.Success) return null;
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
    }

    private static long LocalToMs(DateTime localDate, int h, int m, int s, TimeZoneInfo z)
    {
        var local = DateTime.SpecifyKind(localDate.Date.AddHours(h).AddMinutes(m).AddSeconds(s), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, z)).ToUnixTimeMilliseconds();
    }

    /// <summary>The most recent RTH open at or before ms: a weekday at the start time, Chicago.</summary>
    public static long? RthAnchor(long ms, string start)
    {
        if (Hms(start) is not var (h, m, s)) return null;
        var z = Session.Zone(Tz);
        var day = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), z).Date;
        for (var i = 0; i < 8; i++, day = day.AddDays(-1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var a = LocalToMs(day, h, m, s, z);
            if (a <= ms) return a;
        }
        return null;
    }

    /// <summary>The most recent Globex open at or before ms: 17:00 Chicago, Sunday through Thursday (the
    /// Friday 17:00 and Saturday bells don't exist, so a Sunday trade never arms on Saturday's).</summary>
    public static long? GlobexAnchor(long ms)
    {
        var z = Session.Zone(Tz);
        var day = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), z).Date;
        for (var i = 0; i < 8; i++, day = day.AddDays(-1))
        {
            if (day.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday) continue;
            var a = LocalToMs(day, 17, 0, 0, z);
            if (a <= ms) return a;
        }
        return null;
    }

    /// <summary>Chicago date of an open, yyyy-MM-dd (history key).</summary>
    public static string SessionDate(long anchorMs) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(anchorMs), Session.Zone(Tz)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Chicago HH:mm of an open, so a day anchored at another time never mixes into the stats.</summary>
    public static string OpenHhmm(long anchorMs) =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(anchorMs), Session.Zone(Tz)).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>POC and the value area covering pct of volume, expanded from the POC two rows at a time
    /// toward the heavier pair (tie: up) — hmd-analytics volume_profile.rs.</summary>
    public static (long Poc, long Val, long Vah)? ValueArea(IReadOnlyDictionary<long, long> vol, double pct)
    {
        if (vol.Count == 0) return null;
        var rows = vol.OrderBy(kv => kv.Key).ToArray();
        long total = 0;
        foreach (var r in rows) total += r.Value;
        if (total <= 0) return null;
        var pocIdx = 0;
        for (var i = 1; i < rows.Length; i++) if (rows[i].Value >= rows[pocIdx].Value) pocIdx = i; // tie -> highest tick
        var target = (long)Math.Ceiling(total * pct);
        var acc = rows[pocIdx].Value;
        int lo = pocIdx, hi = pocIdx, last = rows.Length - 1;
        while (acc < target && (lo > 0 || hi < last))
        {
            var above = (hi < last ? rows[hi + 1].Value : 0) + (hi + 1 < last ? rows[hi + 2].Value : 0);
            var below = (lo >= 1 ? rows[lo - 1].Value : 0) + (lo >= 2 ? rows[lo - 2].Value : 0);
            if (hi < last && (above >= below || lo == 0))
                for (var k = 0; k < Math.Min(2, last - hi); k++) acc += rows[++hi].Value;
            else
                for (var k = 0; k < Math.Min(2, lo); k++) acc += rows[--lo].Value;
        }
        return (rows[pocIdx].Key, rows[lo].Key, rows[hi].Key);
    }
}

public sealed class IbTracker
{
    private enum Zone { Inside, Above, Below }

    private sealed class Range
    {
        public long? High, Low;
        public bool Formed, Restored;
        /// <summary>Where the last post-formation trade printed; null = none yet, which counts as inside.</summary>
        public Zone? Zone;
        public readonly List<IbEvent> Events = new();
        public long? DeltaSinceBreak;
        public readonly Dictionary<long, long> Vol = new();

        public void AfterFormed(long ms, long px, long signed)
        {
            if (High is not long h || Low is not long l) return;
            var zone = px > h ? IbTracker.Zone.Above : px < l ? IbTracker.Zone.Below : IbTracker.Zone.Inside;
            var prev = Zone ?? IbTracker.Zone.Inside;
            Zone = zone;
            if (zone == prev)
            {
                if (DeltaSinceBreak is long d) DeltaSinceBreak = d + signed;
                return;
            }
            var kind = zone switch { IbTracker.Zone.Above => IbEventKind.BrokeHigh, IbTracker.Zone.Below => IbEventKind.BrokeLow, _ => IbEventKind.BackInside };
            if (zone == IbTracker.Zone.Inside) { if (DeltaSinceBreak is long d) DeltaSinceBreak = d + signed; }
            else DeltaSinceBreak = signed;
            if (Events.Count == Ib.MaxEvents) Events.RemoveAt(0);
            Events.Add(new IbEvent(kind, ms, px));
        }
    }

    private sealed class SessionRanges
    {
        public long? Anchor;
        /// <summary>When tracking really began: the anchor when the open was crossed, later when the first
        /// print seen was after it.</summary>
        public long? JoinedAt;
        public Range[] Ranges = NewRanges();

        private static Range[] NewRanges() => Ib.Lengths.Select(_ => new Range()).ToArray();

        public long? EndOf(int i) => Anchor is long a ? a + Ib.Lengths[i] * 60_000L : null;
        public bool Missed(int i) => EndOf(i) is long end && JoinedAt is long j && !Ranges[i].Restored && end <= j;
        public bool Partial(int i) => Anchor is long a && JoinedAt is long j && j > a && !Missed(i) && !Ranges[i].Restored;

        public bool Arm(long anchor, long joinedAt)
        {
            if (Anchor == anchor) return false;
            Ranges = NewRanges();
            Anchor = anchor;
            JoinedAt = Math.Max(joinedAt, anchor);
            return true;
        }

        public void OnTrade(IbSession session, long ms, long px, long size, long signed, List<IbFormed> outList)
        {
            if (Anchor is not long a || ms < a) return;
            for (var i = 0; i < Ib.Lengths.Length; i++)
            {
                if (Missed(i)) continue;
                var end = a + Ib.Lengths[i] * 60_000L;
                var partial = Partial(i);
                var r = Ranges[i];
                if (!r.Formed)
                {
                    if (ms < end)
                    {
                        r.High = r.High is long h ? Math.Max(h, px) : px;
                        r.Low = r.Low is long l ? Math.Min(l, px) : px;
                        r.Vol[px] = (r.Vol.TryGetValue(px, out var v) ? v : 0) + size;
                        continue;
                    }
                    r.Formed = true;
                    if (r.High is long fh && r.Low is long fl) outList.Add(new IbFormed(session, Ib.Lengths[i], a, fh, fl, partial));
                }
                r.AfterFormed(ms, px, signed);
            }
        }

        public IbRangeView View(IbSession session, int i)
        {
            var r = Ranges[i];
            var missed = Missed(i);
            var state = Anchor is null ? IbState.Idle
                : missed || (r.Formed && r.High is null && r.Low is null) ? IbState.Missed // a window that closed with no trades
                : !r.Formed ? IbState.Building
                : (r.Zone ?? Zone.Inside) switch { Zone.Above => IbState.Above, Zone.Below => IbState.Below, _ => IbState.Inside };
            var va = Ib.ValueArea(r.Vol, Ib.ValueAreaPct);
            return new IbRangeView(session, Ib.Lengths[i], Anchor, EndOf(i), state, r.Formed, Partial(i), r.Restored,
                missed ? null : r.High, missed ? null : r.Low, va?.Poc, va?.Val, va?.Vah, r.Events.ToArray(), r.DeltaSinceBreak);
        }
    }

    private readonly SessionRanges rth = new(), globex = new();
    private readonly string rthStart;
    private long? lastMs;

    public long? LastTicks { get; private set; }
    public long? LastMs => lastMs;

    /// <param name="rthStart">Chicago HH:MM[:SS] of the RTH open (Ib.RthStart).</param>
    public IbTracker(string rthStart) { this.rthStart = rthStart; }

    private SessionRanges Of(IbSession s) => s == IbSession.Rth ? rth : globex;

    /// <summary>Feed one trade, oldest first. side: 'b' = buyer lifted the offer, 's' = seller hit the bid.
    /// Returns the ranges that closed on it and the sessions that armed on it (a new open).</summary>
    public (List<IbFormed> Formed, List<IbSession> Armed) OnTrade(long ms, long ticks, long size, char side)
    {
        var formed = new List<IbFormed>();
        var armed = new List<IbSession>();
        if (size <= 0) return (formed, armed);
        // Arm on the most recent open at or before this trade — never a precomputed next bell. The first
        // print ever seen joins at its own time (late join); a later crossing joins at the open.
        foreach (var (sess, anchor) in new[] { (IbSession.Rth, Ib.RthAnchor(ms, rthStart)), (IbSession.Globex, Ib.GlobexAnchor(ms)) })
            if (anchor is long a && Of(sess).Arm(a, lastMs is long prev && prev < a ? a : ms)) armed.Add(sess);
        var signed = side == 'b' ? size : side == 's' ? -size : 0;
        lastMs = ms;
        LastTicks = ticks;
        rth.OnTrade(IbSession.Rth, ms, ticks, size, signed, formed);
        globex.OnTrade(IbSession.Globex, ms, ticks, size, signed, formed);
        return (formed, armed);
    }

    public IReadOnlyList<int> MissedLengths(IbSession s)
    {
        var sr = Of(s);
        return Enumerable.Range(0, Ib.Lengths.Length).Where(sr.Missed).Select(i => Ib.Lengths[i]).ToList();
    }

    public long? AnchorOf(IbSession s) => Of(s).Anchor;

    /// <summary>Seed a range from today's history row when its window closed before tracking began.</summary>
    public void RestoreFormed(IbSession s, int lenMin, long high, long low)
    {
        var i = Array.IndexOf(Ib.Lengths, lenMin);
        if (i < 0) return;
        Of(s).Ranges[i] = new Range { High = Math.Max(high, low), Low = Math.Min(high, low), Formed = true, Restored = true };
    }

    public IbRangeView View(IbSession s, int lenMin)
    {
        var i = Array.IndexOf(Ib.Lengths, lenMin);
        return Of(s).View(s, i < 0 ? 3 : i);
    }
}

// ───────────── history: one row per (date, root, session, length, open time) ─────────────

public sealed record IbHistoryRow(string Date, string Root, IbSession Session, int LenMin, string OpenHhmm, long High, long Low, double TickSize)
{
    public string Key => $"{Date}|{Root}|{Session}|{LenMin}|{OpenHhmm}";
}

public sealed record IbPrev(string Date, long High, long Low);
public sealed record IbStats(int Days, double? MedianRange, double? Percentile, IbPrev? Prev);

/// <summary>Formed IBs, one JSON line each. A re-formed IB (restart) replaces its row, never duplicates.</summary>
public sealed class IbHistory
{
    public const int MinDaysForPercentile = 5;
    private static readonly JsonSerializerOptions Json = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };
    private readonly string? path;
    private readonly Dictionary<string, IbHistoryRow> rows = new();
    private readonly object gate = new();

    public static IbHistory InMemory() => new(null);

    public IbHistory(string? path)
    {
        this.path = path;
        if (path == null || !File.Exists(path)) return;
        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try { if (JsonSerializer.Deserialize<IbHistoryRow>(line, Json) is { } r && r.OpenHhmm is { Length: > 0 }) rows[r.Key] = r; }
                catch { /* a bad line is skipped */ }
            }
        }
        catch { /* unreadable file: start empty */ }
    }

    public int Count { get { lock (gate) return rows.Count; } }

    public void Record(IbHistoryRow row)
    {
        lock (gate)
        {
            if (rows.TryGetValue(row.Key, out var had) && had == row) return;
            rows[row.Key] = row;
            if (path == null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                // Rewritten whole so a replaced row leaves no duplicate behind.
                File.WriteAllLines(path, rows.Values.OrderBy(r => r.Date).ThenBy(r => r.Key).Select(r => JsonSerializer.Serialize(r, Json)));
            }
            catch { /* the stats are still right in memory */ }
        }
    }

    public IbHistoryRow? Get(string date, string root, IbSession s, int lenMin, string openHhmm)
    {
        lock (gate) return rows.TryGetValue($"{date}|{root}|{s}|{lenMin}|{openHhmm}", out var r) ? r : null;
    }

    /// <summary>Median range and today's percentile over PAST days of the same key (percentile from 5 days),
    /// plus the previous day's IB.</summary>
    public IbStats Stats(string root, IbSession s, int lenMin, string openHhmm, string today, long? todayRange)
    {
        List<IbHistoryRow> past;
        lock (gate)
            past = rows.Values.Where(r => r.Root == root && r.Session == s && r.LenMin == lenMin && r.OpenHhmm == openHhmm && string.CompareOrdinal(r.Date, today) < 0)
                .OrderBy(r => r.Date).ToList();
        var ranges = past.Select(r => r.High - r.Low).OrderBy(x => x).ToList();
        double? median = ranges.Count == 0 ? null : ranges.Count % 2 == 1 ? ranges[ranges.Count / 2] : (ranges[ranges.Count / 2 - 1] + ranges[ranges.Count / 2]) / 2.0;
        double? pct = todayRange is long t && past.Count >= MinDaysForPercentile ? ranges.Count(x => x <= t) * 100.0 / past.Count : null;
        var prev = past.LastOrDefault() is { } p ? new IbPrev(p.Date, p.High, p.Low) : null;
        return new IbStats(past.Count, median, pct, prev);
    }
}
