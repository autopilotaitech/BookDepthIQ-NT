// A stand-in for the NT8 bridge, for working on the DOM without NinjaTrader: a random-walk NQ market
// on 127.0.0.1:36971 with 3 hours of history, live book and prints, and (with --position) a pretend
// long with TP/SL legs so every column and line shows. Order commands are answered "FAKE — nothing
// sent"; it never talks to NinjaTrader or any broker.
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

var withPosition = args.Contains("--position");
// Its own port (36972 unless BDIQ_PORT says otherwise), so it never collides with a running NinjaTrader.
var port = int.TryParse(Environment.GetEnvironmentVariable("BDIQ_PORT"), out var pp) && pp > 0 ? pp : 36972;
var listener = new TcpListener(IPAddress.Loopback, port);
listener.Start();
Console.WriteLine($"fake bridge on 127.0.0.1:{port} (no orders are ever sent) — run the DOM with BDIQ_PORT={port}");
while (true)
{
    var c = await listener.AcceptTcpClientAsync();
    _ = Task.Run(() => Serve(c, withPosition));
}

static async Task Serve(TcpClient c, bool withPosition)
{
    var rnd = new Random(7);
    const double tick = 0.25;
    var stream = c.GetStream();
    var w = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
    var gate = new object();
    void Send(JsonObject o) { lock (gate) w.WriteLine(o.ToJsonString()); }
    long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // History: 3 h of prints, a drifting trend with pullbacks.
    var hist = new List<(long ms, long t, long v, char side)>();
    var start = Now() - 3 * 3600_000L;
    double p = 83800;
    for (var ms = start; ms < Now(); ms += 700 + rnd.Next(800))
    {
        var drift = Math.Sin((ms - start) / 1_800_000.0) * 0.35 + 0.08;
        p += drift + (rnd.NextDouble() - 0.5) * 3.2;
        var side = rnd.NextDouble() < 0.5 + drift / 3 ? 'b' : 's';
        hist.Add((ms, (long)Math.Round(p), rnd.Next(1, 9) + (rnd.Next(40) == 0 ? 25 : 0), side));
    }
    var mid = hist[^1].t;
    var inst = "NQ 12-26";

    Send(new() { ["t"] = "hello", ["v"] = 2, ["accounts"] = new JsonArray(new JsonObject { ["name"] = "Sim101", ["conn"] = "Connected" }, new JsonObject { ["name"] = "APEX-12345", ["conn"] = "Connected" }) });
    _ = Task.Run(async () =>
    {
        var r = new StreamReader(stream);
        string? line;
        while ((line = await r.ReadLineAsync()) != null)
        {
            var m = JsonNode.Parse(line)!;
            var t = (string?)m["t"];
            if (t == "sub") { inst = (string?)m["inst"] ?? inst; Send(new() { ["t"] = "inst", ["name"] = inst, ["tick"] = tick, ["pv"] = 20.0, ["desc"] = "fake NQ" }); }
            else if (t == "acct")
            {
                var acct = (string?)m["name"] ?? "Sim101";
                Send(new() { ["t"] = "account", ["name"] = acct, ["cash"] = 51240.0, ["realized"] = 1240.0, ["unrealized"] = 0.0, ["netliq"] = 0.0, ["trailDd"] = 0.0, ["dailyLimit"] = 0.0, ["commission"] = 0.0 });
                if (withPosition)
                {
                    var avg = (mid - 12) * tick;
                    Send(new() { ["t"] = "pos", ["acct"] = acct, ["inst"] = inst, ["qty"] = 2, ["avg"] = avg });
                    Send(new()
                    {
                        ["t"] = "orders", ["acct"] = acct,
                        ["list"] = new JsonArray(
                            Order("1", "Sell", "Limit", 1, avg + 32 * tick, 0, "BDIQ TP1", "o1"),
                            Order("2", "Sell", "StopMarket", 1, 0, avg - 16 * tick, "BDIQ SL1", "o1"),
                            Order("3", "Sell", "Limit", 1, avg + 64 * tick, 0, "BDIQ TP2", "o2"),
                            Order("4", "Sell", "StopMarket", 1, 0, avg - 16 * tick, "BDIQ SL2", "o2"),
                            Order("5", "Buy", "Limit", 1, (mid - 40) * tick, 0, "BDIQ entry", "")),
                    });
                }
                JsonObject Order(string id, string action, string type, int qty, double limit, double stop, string name, string oco) =>
                    new() { ["id"] = id, ["inst"] = inst, ["action"] = action, ["type"] = type, ["qty"] = qty, ["filled"] = 0, ["limit"] = limit, ["stop"] = stop, ["state"] = "Working", ["oco"] = oco, ["name"] = name };
            }
            else if (t == "hist")
            {
                for (var i = 0; i < hist.Count; i += 5000)
                {
                    var arr = new JsonArray();
                    foreach (var h in hist.Skip(i).Take(5000)) arr.Add(new JsonArray(h.ms, h.t * tick, h.v, h.side.ToString()));
                    Send(new() { ["t"] = "hist", ["inst"] = inst, ["trades"] = arr, ["done"] = i + 5000 >= hist.Count });
                }
            }
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
                bids.Add(new JsonArray((mid - i) * tick, (double)(rnd.Next(3, 40) + (i == 7 ? 160 : 0))));
                asks.Add(new JsonArray((mid + 1 + i) * tick, (double)(rnd.Next(3, 40) + (i == 11 ? 140 : 0))));
            }
            Send(new() { ["t"] = "book", ["bids"] = bids, ["asks"] = asks, ["ms"] = Now() });
            var buy = rnd.Next(2) == 0;
            Send(new() { ["t"] = "trade", ["p"] = (buy ? mid + 1 : mid) * tick, ["v"] = (double)(rnd.Next(1, 8) + (rnd.Next(30) == 0 ? 20 : 0)), ["side"] = buy ? "b" : "s", ["ms"] = Now() });
            await Task.Delay(120);
        }
    }
    catch { }
}
