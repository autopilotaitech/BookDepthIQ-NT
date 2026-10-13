using System.Text.Json;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

public sealed record AccountInfo(string Name, string Conn);
public sealed record WorkingOrder(string Id, string Inst, string Action, string Type, int Qty, int Filled, double Limit, double Stop, string State, string Oco, string Name)
{
    public bool IsBuy => Action is "Buy" or "BuyToCover";
    /// <summary>The price the order rests at on the ladder.</summary>
    public double Price => Type is "StopMarket" ? Stop : Limit;
    public bool IsStop => Type is "StopMarket" or "StopLimit";
}
public sealed record LogEntry(DateTime Time, char Mark, string Text);

/// <summary>Everything the DOM shows, fed by bridge messages. All access on the UI thread.</summary>
public sealed class DomState
{
    public bool Connected { get; set; }
    public List<AccountInfo> Accounts { get; } = new();
    public string Instrument { get; set; } = "";
    public double TickSize { get; set; } = 0.25;
    public double PointValue { get; set; } = 20;
    public double TickValue => TickSize * PointValue;
    public Book Book { get; set; } = Book.Empty;
    public long? LastTick { get; set; }
    public List<Trade> Tape { get; } = new();
    /// <summary>Volume traded at each price (ticks) this session, split by aggressor.</summary>
    public Dictionary<long, (long Buy, long Sell)> Volume { get; } = new();
    public long LastMsgMs { get; set; }

    public string Account { get; set; } = "Sim101";
    public double Cash, Realized, Unrealized, NetLiq, TrailDd, DailyLimit, Commission;
    public int PosQty { get; set; }
    public double PosAvg { get; set; }
    public List<WorkingOrder> Orders { get; } = new();

    public List<LogEntry> Log { get; } = new();
    public string? LastResult { get; set; }
    public bool LastOk { get; set; } = true;

    public event Action? Changed;
    public void Raise() => Changed?.Invoke();

    public void Note(char mark, string text)
    {
        Log.Add(new LogEntry(DateTime.Now, mark, text));
        if (Log.Count > 300) Log.RemoveRange(0, Log.Count - 300);
    }

    /// <summary>Applies one bridge message. Unknown types are ignored.</summary>
    public void Apply(JsonElement m)
    {
        LastMsgMs = Environment.TickCount64;
        switch (S(m, "t"))
        {
            case "hello":
                Connected = true;
                Accounts.Clear();
                if (m.TryGetProperty("accounts", out var al))
                    foreach (var a in al.EnumerateArray()) Accounts.Add(new AccountInfo(S(a, "name"), S(a, "conn")));
                break;
            case "inst":
                Instrument = S(m, "name");
                TickSize = D(m, "tick", TickSize);
                PointValue = D(m, "pv", PointValue);
                Book = Book.Empty;
                Tape.Clear();
                Volume.Clear();
                LastTick = null;
                break;
            case "book":
                Book = Book.FromLevels(Levels(m, "bids"), Levels(m, "asks"), TickSize, (long)D(m, "ms", 0));
                break;
            case "trade":
            {
                var t = Book.ToTicks(D(m, "p", 0), TickSize);
                var v = (long)D(m, "v", 0);
                var side = S(m, "side") is { Length: > 0 } sd ? sd[0] : '?';
                LastTick = t;
                Tape.Insert(0, new Trade(t, v, side, (long)D(m, "ms", 0)));
                if (Tape.Count > 200) Tape.RemoveRange(200, Tape.Count - 200);
                Volume.TryGetValue(t, out var cur);
                Volume[t] = side == 'b' ? (cur.Buy + v, cur.Sell) : side == 's' ? (cur.Buy, cur.Sell + v) : (cur.Buy + v / 2, cur.Sell + v - v / 2);
                break;
            }
            case "account":
                if (S(m, "name") != Account) break;
                Cash = D(m, "cash", 0); Realized = D(m, "realized", 0); Unrealized = D(m, "unrealized", 0);
                NetLiq = D(m, "netliq", 0); TrailDd = D(m, "trailDd", 0); DailyLimit = D(m, "dailyLimit", 0); Commission = D(m, "commission", 0);
                break;
            case "pos":
                if (S(m, "acct") == Account && S(m, "inst") == Instrument) { PosQty = (int)D(m, "qty", 0); PosAvg = D(m, "avg", 0); }
                break;
            case "orders":
                if (S(m, "acct") != Account) break;
                Orders.Clear();
                foreach (var o in m.GetProperty("list").EnumerateArray())
                    Orders.Add(new WorkingOrder(S(o, "id"), S(o, "inst"), S(o, "action"), S(o, "type"), (int)D(o, "qty", 0), (int)D(o, "filled", 0), D(o, "limit", 0), D(o, "stop", 0), S(o, "state"), S(o, "oco"), S(o, "name")));
                break;
            case "ack":
            {
                var ok = m.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                LastOk = ok;
                LastResult = S(m, "msg");
                Note(ok ? '✓' : '✗', LastResult);
                break;
            }
            case "log":
                Note('·', S(m, "msg"));
                break;
        }
    }

    public IEnumerable<WorkingOrder> OrdersOnScreen => Orders.Where(o => o.Inst == Instrument);

    /// <summary>Open P&L in $ at the price you would exit at now.</summary>
    public double OpenPnl
    {
        get
        {
            if (PosQty == 0) return 0;
            var exit = PosQty > 0 ? Book.BestBid : Book.BestAsk;
            if (exit is null) return Unrealized;
            return (exit.Value * TickSize - PosAvg) * PosQty * PointValue;
        }
    }

    private static string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : e.TryGetProperty(k, out v) ? v.ToString() : "";
    private static double D(JsonElement e, string k, double dflt) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : dflt;
    private static IEnumerable<double[]> Levels(JsonElement e, string k) =>
        e.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(r => r.EnumerateArray().Select(x => x.GetDouble()).ToArray()).ToList()
            : Enumerable.Empty<double[]>();
}
