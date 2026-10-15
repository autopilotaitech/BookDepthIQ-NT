namespace BookDepthIQ.Core;

// Position management that acts WITHOUT a click: B/E after TP1, trailing stop (R or SuperTrend),
// stop recovery, close before the CME weekend halt, close at the daily loss limit. Each is OFF until
// the user turns it on, only ever moves or re-places a STOP or closes a position (never opens one),
// and logs what it did. Owner's OK to port them all: 2026-10-13 ("port all of it"). Pure.
// Port of BookDepthIQ-Vest src/live/manage.ts and targets.ts (BeWatch).

public sealed record TrendLine(long Ticks, TrendDir Dir);

public static class Manage
{
    /// <summary>Trailing stop: once price has gone startR × risk your way, the stop follows the best price
    /// at one risk behind it, only ever your way. The new stop in ticks, or null to stay.</summary>
    public static long? TrailTarget(bool isLong, long entryTicks, long riskTicks, long bestTicks, double startR, long? stopTicks)
    {
        if (riskTicks <= 0 || !(startR > 0)) return null;
        var dir = isLong ? 1 : -1;
        if ((bestTicks - entryTicks) * dir < startR * riskTicks) return null;
        var want = isLong ? bestTicks - riskTicks : bestTicks + riskTicks;
        if (stopTicks is long s && (want - s) * dir < 1) return null;
        return want;
    }

    /// <summary>SuperTrend trail: the stop sits ON the chosen line while it is on the trade's side, only
    /// ever your way. A flipped line is ignored.</summary>
    public static long? LineTrailTarget(bool isLong, TrendLine? line, long? stopTicks)
    {
        if (line is null || line.Dir != (isLong ? TrendDir.Up : TrendDir.Down)) return null;
        if (stopTicks is long s && (line.Ticks - s) * (isLong ? 1 : -1) < 1) return null;
        return line.Ticks;
    }

    /// <summary>When to close before the CME weekend halt: leadMin before Friday's close (default 16:00
    /// Chicago for CME Globex equity/metals/energy). Null when it is not Friday in Chicago or the close passed.</summary>
    public static long? WeekendCloseAt(long nowMs, string closeHhmm = "16:00", int leadMin = 5, string tz = "America/Chicago")
    {
        if (Session.ParseHhmm(closeHhmm) is not int closeMin) return null;
        var zone = Session.Zone(tz);
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(nowMs), zone);
        if (local.DayOfWeek != DayOfWeek.Friday) return null;
        var closeLocal = new DateTime(local.Year, local.Month, local.Day, closeMin / 60, closeMin % 60, 0, DateTimeKind.Unspecified);
        var closeUtc = TimeZoneInfo.ConvertTimeToUtc(closeLocal, zone);
        var at = new DateTimeOffset(closeUtc).ToUnixTimeMilliseconds() - leadMin * 60_000L;
        return nowMs <= at + leadMin * 60_000L ? at : null;
    }
}

/// <summary>One trailing stop per instrument: the risk captured at entry and the best price since, or
/// the SuperTrend line it follows (1 = ST1, 2 = ST2).</summary>
public sealed class TrailBook
{
    private sealed class W { public long RiskTicks; public double StartR; public int Line; public long? Best; public bool? Long; }
    private readonly Dictionary<string, W> w = new();

    public void Arm(string inst, long riskTicks, double startR, int line = 0)
    {
        if (line is 1 or 2) w[inst] = new W { RiskTicks = riskTicks, Line = line };
        else if (riskTicks > 0 && startR > 0) w[inst] = new W { RiskTicks = riskTicks, StartR = startR };
    }

    public (double StartR, int Line)? Config(string inst) => w.TryGetValue(inst, out var s) ? (s.StartR, s.Line) : null;
    public bool Clear(string? inst = null) { if (inst is null) { var n = w.Count; w.Clear(); return n > 0; } return w.Remove(inst); }
    public bool Has(string inst) => w.ContainsKey(inst);
    public int Count => w.Count;
    public void ResetPosition(string inst)
    {
        if(w.TryGetValue(inst,out var state)) { state.Best=null; state.Long=null; }
    }

    /// <summary>A trade printed while a position is open: the stop it should have now, if it should move.</summary>
    public long? OnTrade(string inst, bool isLong, long entryTicks, long priceTicks, long? stopTicks, TrendLine? st1, TrendLine? st2)
    {
        if (!w.TryGetValue(inst, out var s)) return null;
        if (s.Line != 0) return Manage.LineTrailTarget(isLong, s.Line == 1 ? st1 : st2, stopTicks);
        if (s.Long != isLong) { s.Long = isLong; s.Best = null; }
        s.Best = s.Best is long b ? (isLong ? Math.Max(b, priceTicks) : Math.Min(b, priceTicks)) : priceTicks;
        return Manage.TrailTarget(isLong, entryTicks, s.RiskTicks, s.Best.Value, s.StartR, stopTicks);
    }
}

/// <summary>B/E after TP1 requires identified TP1 executions, never changes in order/position counts.</summary>
public sealed class BeWatch
{
    private sealed class W { public string Inst = ""; public long OffsetTicks; public int TargetQty, Filled; public bool Seen; public bool? Long; public HashSet<string> Executions = new(); }
    private W? w;

    public void Arm(string inst, long offsetTicks, int targetQty = 1, bool? isLong = null) =>
        w = new W { Inst = inst, OffsetTicks = offsetTicks, TargetQty = Math.Max(1,targetQty), Long=isLong };
    public void OnExecution(Exec e)
    {
        if (w is null || e.Inst != w.Inst || e.Name != "BDIQ TP1" || e.Qty <= 0 || (w.Long.HasValue && e.Buy == w.Long.Value)) return;
        if (e.Id.Length > 0 && !w.Executions.Add(e.Id)) return;
        w.Filled += e.Qty;
    }
    public bool Clear(string? inst = null) { if (w is null || (inst is not null && w.Inst != inst)) return false; w = null; return true; }
    public string? Inst => w?.Inst;
    public long OffsetTicks => w?.OffsetTicks ?? 0;

    /// <summary>"fire" once when TP1 has filled; "gone" when the position closed; else "idle".</summary>
    public string Check(int absQty, int targetOrders, bool entryPending = false)
    {
        if (w is null) return "idle";
        if (absQty == 0)
        {
            if(entryPending) return "idle";
            if (!w.Seen) return "idle";
            w = null;
            return "gone";
        }
        w.Seen = true;
        if (w.Filled >= w.TargetQty) { w = null; return "fire"; }
        return "idle";
    }
}

/// <summary>Stop recovery: remembers the last stop seen on each position. When a position that HAD a stop
/// shows none on reads at least confirmMs apart, it says which stop to put back. Positions never seen
/// with a stop are left alone (brackets attach a moment after the fill).</summary>
public sealed class StopGuard
{
    private sealed class S { public double Stop; public long? MissingSince; public long? LastTry; }
    private readonly Dictionary<string, S> last = new();
    private readonly long confirmMs, retryMs;
    public StopGuard(long confirmMs = 3000, long retryMs = 10_000) { this.confirmMs = confirmMs; this.retryMs = retryMs; }

    /// <summary>positions: each open position key with its stop price (null = none).</summary>
    public List<(string Key, double Stop)> Check(IEnumerable<(string Key, double? Stop)> positions, long now)
    {
        var o = new List<(string, double)>();
        var list = positions.ToList();
        var open = list.Select(p => p.Key).ToHashSet();
        foreach (var k in last.Keys.ToList()) if (!open.Contains(k)) last.Remove(k);
        foreach (var (key, stop) in list)
        {
            if (stop is double sp) { last[key] = new S { Stop = sp }; continue; }
            if (!last.TryGetValue(key, out var s)) continue;
            s.MissingSince ??= now;
            if (now - s.MissingSince < confirmMs) continue;
            if (s.LastTry is long t && now - t < retryMs) continue;
            s.LastTry = now;
            o.Add((key, s.Stop));
        }
        return o;
    }

    public void Clear() => last.Clear();
}
