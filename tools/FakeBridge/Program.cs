// A stand-in for the NT8 bridge, for working on the DOM without NinjaTrader: a random-walk NQ book
// and prints on 127.0.0.1:36971. Order commands are answered "FAKE — nothing sent"; it never talks
// to NinjaTrader or any broker.
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

var listener = new TcpListener(IPAddress.Loopback, 36971);
listener.Start();
Console.WriteLine("fake bridge on 127.0.0.1:36971 (no orders are ever sent)");
while (true)
{
    var c = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => Serve(c));
}

static async Task Serve(TcpClient c)
{
    var rnd = new Random(7);
    var tick = 0.25;
    var mid = 84000L; // 21000.00
    var stream = c.GetStream();
    var w = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
    var gate = new object();
    void Send(JsonObject o) { lock (gate) w.WriteLine(o.ToJsonString()); }
    Send(new() { ["t"] = "hello", ["v"] = 1, ["accounts"] = new JsonArray(new JsonObject { ["name"] = "Sim101", ["conn"] = "Connected" }) });
    _ = Task.Run(async () =>
    {
        var r = new StreamReader(stream);
        string? line;
        while ((line = await r.ReadLineAsync()) != null)
        {
            var m = JsonNode.Parse(line)!;
            var t = (string?)m["t"];
            if (t == "sub") Send(new() { ["t"] = "inst", ["name"] = (string?)m["inst"], ["tick"] = tick, ["pv"] = 20.0, ["desc"] = "fake NQ" });
            else if (t == "acct") Send(new() { ["t"] = "account", ["name"] = (string?)m["name"], ["cash"] = 50000.0, ["realized"] = 0.0, ["unrealized"] = 0.0, ["netliq"] = 50000.0, ["trailDd"] = 0.0, ["dailyLimit"] = 0.0, ["commission"] = 0.0 });
            else if (m["cid"] is not null) Send(new() { ["t"] = "ack", ["cid"] = (string?)m["cid"], ["ok"] = false, ["msg"] = $"FAKE bridge — {t} not sent anywhere" });
        }
    });
    try
    {
        while (c.Connected)
        {
            mid += rnd.Next(-2, 3);
            var bids = new JsonArray(); var asks = new JsonArray();
            for (var i = 0; i < 40; i++)
            {
                bids.Add(new JsonArray((mid - i) * tick, (double)rnd.Next(3, 60)));
                asks.Add(new JsonArray((mid + 1 + i) * tick, (double)rnd.Next(3, 60)));
            }
            Send(new() { ["t"] = "book", ["bids"] = bids, ["asks"] = asks, ["ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            var buy = rnd.Next(2) == 0;
            Send(new() { ["t"] = "trade", ["p"] = (buy ? mid + 1 : mid) * tick, ["v"] = (double)rnd.Next(1, 12), ["side"] = buy ? "b" : "s", ["ms"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            await Task.Delay(120);
        }
    }
    catch { }
}
