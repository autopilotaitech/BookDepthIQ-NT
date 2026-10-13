namespace BookDepthIQ.Core;

/// <summary>How a prop account's fail floor moves.</summary>
public enum DrawdownKind
{
    /// <summary>Floor = start − drawdown, never moves.</summary>
    Static,
    /// <summary>Floor trails the highest equity seen, open P&L included, in real time.</summary>
    TrailingIntraday,
    /// <summary>Floor trails the highest end-of-day balance; moves only at the session close.</summary>
    TrailingEod,
}

/// <summary>The user's prop rules: start balance, max drawdown, how it trails, where it stops
/// trailing (0 = never), and an optional daily loss limit. Every number is the user's.</summary>
public sealed record DrawdownRules(double Start, double MaxDrawdown, DrawdownKind Kind, double LockAt = 0, double DailyLoss = 0);

/// <summary>Tracks the fail floor from the equity stream. Pure; the caller persists Peak.</summary>
public sealed class DrawdownTracker
{
    public DrawdownRules Rules { get; }
    /// <summary>Highest equity (intraday) or end-of-day balance (EOD) seen.</summary>
    public double Peak { get; private set; }
    public double DayStart { get; private set; }

    public DrawdownTracker(DrawdownRules rules, double? peak = null, double? dayStart = null)
    {
        Rules = rules;
        Peak = peak ?? rules.Start;
        DayStart = dayStart ?? rules.Start;
    }

    /// <summary>Equity now (balance + open P&L). Intraday trailing moves the peak with it.</summary>
    public void OnEquity(double equity)
    {
        if (Rules.Kind == DrawdownKind.TrailingIntraday && equity > Peak) Peak = equity;
    }

    /// <summary>Session close: EOD trailing moves the peak to the closing balance; the day resets.</summary>
    public void OnSessionClose(double balance)
    {
        if (Rules.Kind == DrawdownKind.TrailingEod && balance > Peak) Peak = balance;
        DayStart = balance;
    }

    /// <summary>The equity at which the account fails.</summary>
    public double Floor
    {
        get
        {
            if (Rules.Kind == DrawdownKind.Static) return Rules.Start - Rules.MaxDrawdown;
            var f = Peak - Rules.MaxDrawdown;
            return Rules.LockAt > 0 ? Math.Min(f, Rules.LockAt) : f;
        }
    }

    /// <summary>$ that may still be lost now: the smaller of the floor room and today's room.</summary>
    public double Room(double equity)
    {
        var room = equity - Floor;
        if (Rules.DailyLoss > 0) room = Math.Min(room, equity - (DayStart - Rules.DailyLoss));
        return Math.Max(0, room);
    }
}
