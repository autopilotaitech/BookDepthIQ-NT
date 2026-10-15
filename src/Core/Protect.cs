using System.Globalization;

namespace BookDepthIQ.Core;

// "Protect my day": optional limits on NEW entries, all off by default (0 = off). They never touch
// FLATTEN, CANCEL or any exit. The trading day starts 17:00 Chicago (CME). Pure.
// Port of BookDepthIQ-Vest src/panel/protect.ts, sized in contracts.

public sealed record DayLimits(double LossUsd = 0, int MaxTrades = 0, int CooldownSec = 0);

public sealed record DayState(string Day, double StartEquity, int Trades = 0, long? LastLossAt = null);

public sealed record DayVerdict(bool Ok, string Message, double? RoomUsd = null);

public static class Protect
{
    public static DayState WithRecordedActivity(DayState risk, RecordedDayTotals recorded) =>
        risk.Day == recorded.Day ? risk with { Trades=recorded.Trades, LastLossAt=recorded.LastLossAt } : risk;
    private static readonly TimeZoneInfo Chicago = Session.Zone("America/Chicago");

    /// <summary>Trading-day label: the Chicago date, rolled forward from 17:00.</summary>
    public static string TradingDay(long ms)
    {
        var t = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(ms), Chicago);
        var d = t.Hour >= 17 ? t.Date.AddDays(1) : t.Date;
        return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>May a NEW entry go now? nextRiskUsd = what the next trade loses at its stop (fees in),
    /// null when it has no stop.</summary>
    public static DayVerdict Check(DayLimits lim, DayState st, double equity, long now, double? nextRiskUsd)
    {
        var pnl = equity - st.StartEquity;
        double? room = lim.LossUsd > 0 ? lim.LossUsd + pnl : null;
        if (room <= 0) return new(false, $"daily loss limit reached (${-pnl:N2} down today) — new entries locked until 17:00 Chicago", 0);
        if (lim.MaxTrades > 0 && st.Trades >= lim.MaxTrades) return new(false, $"{st.Trades}/{lim.MaxTrades} trades today — new entries locked until 17:00 Chicago", room);
        if (lim.CooldownSec > 0 && st.LastLossAt is long lastLoss)
        {
            var left = lastLoss + lim.CooldownSec * 1000L - now;
            if (left > 0) return new(false, $"cooldown after a loss: {Math.Ceiling(left / 1000.0)} s left", room);
        }
        if (room is double r)
        {
            if (nextRiskUsd is null) return new(false, $"set a stop: the daily loss limit needs to know this trade's risk (${r:N2} left today)", r);
            if (nextRiskUsd > r + 1e-9) return new(false, $"this trade risks ${nextRiskUsd:N2} but only ${r:N2} is left today — use a smaller size", r);
        }
        return new(true, "ok", room);
    }

    /// <summary>True when the day has hit its loss limit (for the optional close-at-limit).</summary>
    public static bool LimitHit(DayLimits lim, DayState st, double equity) => lim.LossUsd > 0 && equity - st.StartEquity <= -lim.LossUsd;
}

public enum OrderLogStatus { Click, Blocked, Ok, Refused, Error }

public sealed record OrderLogEntry(long T, OrderLogStatus Status, string Text);

/// <summary>Every order click and what became of it. The caller persists Entries.</summary>
public sealed class OrderLog
{
    public const int Max = 300;
    public List<OrderLogEntry> Entries { get; }
    public OrderLog(IEnumerable<OrderLogEntry>? entries = null) => Entries = (entries ?? Enumerable.Empty<OrderLogEntry>()).TakeLast(Max).ToList();

    public OrderLogEntry Add(OrderLogStatus s, string text, long? t = null)
    {
        var e = new OrderLogEntry(t ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), s, text);
        Entries.Add(e);
        if (Entries.Count > Max) Entries.RemoveRange(0, Entries.Count - Max);
        return e;
    }

    public void Clear() => Entries.Clear();

    public static string Mark(OrderLogStatus s) => s switch { OrderLogStatus.Click => "→", OrderLogStatus.Blocked => "⊘", OrderLogStatus.Ok => "✓", OrderLogStatus.Refused => "✗", _ => "!!" };

    /// <summary>Oldest first, one line each, with a header of the context it was copied in.</summary>
    public string Text(IEnumerable<string>? header = null) =>
        string.Join("\n", (header ?? Enumerable.Empty<string>()).Concat(Entries.Select(e =>
            $"{DateTimeOffset.FromUnixTimeMilliseconds(e.T).UtcDateTime:HH:mm:ss.fff}Z {Mark(e.Status)} {e.Text}")));
}
