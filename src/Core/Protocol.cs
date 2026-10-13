using System.Text.Json;
using System.Text.Json.Nodes;

namespace BookDepthIQ.Core;

// The app ↔ NT8 bridge protocol: one JSON object per line over TCP 127.0.0.1:Protocol.Port.
// The bridge (nt8/AddOns/BookDepthIQBridge.cs) is the server; the DOM app connects to it.
// docs/PROTOCOL.md lists every message. Keep the two ends in step.

public static class Protocol
{
    public const int Port = 36971;
    public const int Version = 1;

    // ── app → bridge ──
    public static string Subscribe(string instrument) => Line(new() { ["t"] = "sub", ["inst"] = instrument });
    public static string SelectAccount(string account) => Line(new() { ["t"] = "acct", ["name"] = account });

    /// <summary>An entry from a click. type: market | limit | stop. tp/sl in ticks (0 = none);
    /// the bridge places them as an OCO pair when the entry fills.</summary>
    public static string Order(string cid, string account, string instrument, string side, string type, int qty, double? price, int tpTicks, int slTicks) =>
        Line(new()
        {
            ["t"] = "order", ["cid"] = cid, ["acct"] = account, ["inst"] = instrument, ["side"] = side, ["type"] = type,
            ["qty"] = qty, ["price"] = price, ["tp"] = tpTicks, ["sl"] = slTicks,
        });

    public static string Cancel(string cid, string account, string orderId) => Line(new() { ["t"] = "cancel", ["cid"] = cid, ["acct"] = account, ["id"] = orderId });
    public static string Move(string cid, string account, string orderId, double price) => Line(new() { ["t"] = "move", ["cid"] = cid, ["acct"] = account, ["id"] = orderId, ["price"] = price });
    public static string Flatten(string cid, string account, string instrument) => Line(new() { ["t"] = "flatten", ["cid"] = cid, ["acct"] = account, ["inst"] = instrument });
    public static string CancelAll(string cid, string account, string instrument) => Line(new() { ["t"] = "cancelAll", ["cid"] = cid, ["acct"] = account, ["inst"] = instrument });

    private static string Line(JsonObject o) => o.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
}
