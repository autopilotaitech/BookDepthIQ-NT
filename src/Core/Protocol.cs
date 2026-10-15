using System.Text.Json;
using System.Text.Json.Nodes;

namespace BookDepthIQ.Core;

// The app ↔ NT8 bridge protocol: one JSON object per line over TCP 127.0.0.1:Protocol.Port.
// The bridge (nt8/AddOns/BookDepthIQBridge.cs) is the server; the DOM app connects to it.
// docs/PROTOCOL.md lists every message. Keep the two ends in step.

public static class Protocol
{
    /// <summary>The bridge's port. BDIQ_PORT overrides it for development (the fake bridge), so a running
    /// NinjaTrader is never touched by a test.</summary>
    public static readonly int Port = int.TryParse(Environment.GetEnvironmentVariable("BDIQ_PORT"), out var p) && p > 0 ? p : 36971;
    public const int Version = 2;

    // ── app → bridge ──
    public static string Subscribe(string instrument) => Line(new() { ["t"] = "sub", ["inst"] = instrument });
    public static string SelectAccount(string account) => Line(new() { ["t"] = "acct", ["name"] = account });
    public static string RefreshAccounts() => Line(new() { ["t"] = "accounts" });

    /// <summary>An entry from a click. type: market | limit | stop. tp/sl in ticks (0 = none); the bridge
    /// places them as an OCO pair when the entry fills.</summary>
    public static string Order(string cid, string account, string instrument, string side, string type, int qty, double? price, int tpTicks, int slTicks) =>
        Order(cid, account, instrument, side, type, qty, price, tpTicks > 0 ? new[] { new Piece(tpTicks, qty) } : Array.Empty<Piece>(), slTicks);

    /// <summary>An entry with one or more targets: each (ticks, contracts) slice gets its own TP limit and an
    /// SL stop slTicks away as one OCO pair, on every fill (like NT's ATM). Contracts must add up to qty.</summary>
    public static string Order(string cid, string account, string instrument, string side, string type, int qty, double? price, IReadOnlyList<Piece> targets, int slTicks)
    {
        var t = new JsonArray();
        foreach (var p in targets) t.Add(new JsonArray(p.Ticks, p.Qty));
        return Line(new()
        {
            ["t"] = "order", ["cid"] = cid, ["acct"] = account, ["inst"] = instrument, ["side"] = side, ["type"] = type,
            ["qty"] = qty, ["price"] = price, ["targets"] = t, ["sl"] = slTicks,
        });
    }

    public static string ClosePart(string cid, string account, string instrument, int qty) => Line(new() { ["t"] = "closePart", ["cid"] = cid, ["acct"] = account, ["inst"] = instrument, ["qty"] = qty });
    public static string AddStop(string cid, string account, string instrument, double price, int qty = 0) => Line(new() { ["t"] = "addStop", ["cid"] = cid, ["acct"] = account, ["inst"] = instrument, ["price"] = price, ["qty"] = qty });
    public static string History(string instrument, int hours) => Line(new() { ["t"] = "hist", ["inst"] = instrument, ["hours"] = hours });
    public static string Kill(string cid, string account) => Line(new() { ["t"] = "kill", ["cid"] = cid, ["acct"] = account });

    public static string Cancel(string cid, string account, string orderId) => Line(new() { ["t"] = "cancel", ["cid"] = cid, ["acct"] = account, ["id"] = orderId });
    public static string Move(string cid, string account, string orderId, double price) => Line(new() { ["t"] = "move", ["cid"] = cid, ["acct"] = account, ["id"] = orderId, ["price"] = price });
    public static string Flatten(string cid, string account, string instrument) => Line(new() { ["t"] = "flatten", ["cid"] = cid, ["acct"] = account, ["inst"] = instrument });
    public static string CancelAll(string cid, string account, string instrument) => Line(new() { ["t"] = "cancelAll", ["cid"] = cid, ["acct"] = account, ["inst"] = instrument });

    private static string Line(JsonObject o) => o.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
}
