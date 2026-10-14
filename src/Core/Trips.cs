namespace BookDepthIQ.Core;

/// <summary>One NinjaTrader execution (fees = commission + exchange fee).</summary>
public sealed record Exec(string Id, string Inst, string Name, bool Buy, int Qty, double Price, double Fees, long Ms);

/// <summary>Context captured when a trip opens: the setup tag, stop mode, the price expected at the click,
/// and the stop then working (with its $ risk).</summary>
public sealed record TripContext(string Setup, string StopMode, double? Expected, double? Stop);

/// <summary>
/// Builds journal round trips from executions: a trip opens on the first fill from flat, adds on fills the
/// same way, closes when the position is back to zero. P&L = (avg exit − avg entry) × qty × point value,
/// minus every fee on the trip. A fill past flat closes the trip and opens the other way. Pure.
/// </summary>
public sealed class TripBuilder
{
    private sealed class Open
    {
        public bool Long; public int Qty; public int MaxQty; public int EntryQty; public double EntryNotional; public double ExitNotional; public int ExitQty;
        public double Fees; public long OpenMs; public TripContext Ctx = new("", "", null, null);
    }

    private readonly Dictionary<string, Open> open = new();
    private readonly HashSet<string> seen = new();

    /// <summary>Feeds one execution; returns the trip it closed, if any. context(inst, long) is asked when a
    /// trip opens; pointValue(inst) and tickSize(inst) price it.</summary>
    public JournalTrade? OnExec(Exec e, string account, Func<string, bool, TripContext> context, Func<string, double> pointValue, Func<string, double> tickSize)
    {
        if (e.Id.Length > 0 && !seen.Add(account + "\0" + e.Inst + "\0" + e.Id)) return null;
        if (e.Qty <= 0) return null;
        var key = account + "\0" + e.Inst;
        if (!open.TryGetValue(key, out var t) || t.Qty == 0)
        {
            t = new Open { Long = e.Buy, OpenMs = e.Ms, Ctx = context(e.Inst, e.Buy) };
            open[key] = t;
        }
        if (e.Buy == t.Long)
        {
            t.Fees += e.Fees;
            t.Qty += e.Qty;
            t.MaxQty = Math.Max(t.MaxQty, t.Qty);
            t.EntryNotional += e.Qty * e.Price;
            t.EntryQty += e.Qty;
            return null;
        }
        var closeQty = Math.Min(e.Qty, t.Qty);
        // Fees split pro rata when the fill also opens the other way.
        t.Fees += e.Fees * closeQty / e.Qty;
        t.Qty -= closeQty;
        t.ExitQty += closeQty;
        t.ExitNotional += closeQty * e.Price;
        if (t.Qty > 0) return null;
        open.Remove(key);
        var pv = pointValue(e.Inst);
        var ts = tickSize(e.Inst);
        var avgEntry = t.EntryNotional / Math.Max(1, t.EntryQty);
        var avgExit = t.ExitNotional / Math.Max(1, t.ExitQty);
        var gross = (avgExit - avgEntry) * t.ExitQty * pv * (t.Long ? 1 : -1);
        var trade = new JournalTrade
        {
            Open = t.OpenMs, Close = e.Ms, Inst = e.Inst, Account = account, Long = t.Long, Qty = t.ExitQty, Entry = avgEntry, Exit = avgExit,
            Stop = t.Ctx.Stop, RiskUsd = t.Ctx.Stop is double st ? Math.Abs(avgEntry - st) * t.MaxQty * pv : null,
            Pnl = gross - t.Fees, FeesUsd = t.Fees, Setup = t.Ctx.Setup, StopMode = t.Ctx.StopMode, Expected = t.Ctx.Expected,
            SlipTicks = t.Ctx.Expected is double ex && ts > 0 ? Math.Round((avgEntry - ex) / ts * (t.Long ? 1 : -1), 2) : null,
        };
        var rest = e.Qty - closeQty;
        if (rest > 0) OnExec(e with { Id = e.Id.Length > 0 ? e.Id + "+r" : "", Qty = rest, Fees = e.Fees * rest / e.Qty }, account, context, pointValue, tickSize);
        return trade;
    }

    /// <summary>Records the working stop on an open trip once it is known (brackets attach after the fill).</summary>
    public void SetStop(string inst, double stop, string account = "Sim101")
    {
        if (open.TryGetValue(account + "\0" + inst, out var t) && t.Ctx.Stop is null) t.Ctx = t.Ctx with { Stop = stop };
    }
}
