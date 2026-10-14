// Copyright (c) 2026 autopilotaitech. MIT License: https://github.com/autopilotaitech/BookDepthIQ-NT
// Provided "as is", without warranty. Trading futures involves substantial risk of loss.
//
// BookDepthIQ-NT bridge: streams NinjaTrader 8 depth, prints, account, positions and orders to the
// BookDepthIQ DOM app over 127.0.0.1, and executes the order commands the app sends from a user
// click. It never decides to trade. Protocol: docs/PROTOCOL.md (one JSON object per line).
//
// Install: copy this file to Documents\NinjaTrader 8\bin\Custom\AddOns\, then in NinjaTrader open
// New > NinjaScript Editor and press F5 (compile). The bridge starts with NinjaTrader.
//
// API members used here were read from the installed NinjaTrader.Core.dll (8.1.x) metadata.

#region Using declarations
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
#endregion

namespace NinjaTrader.NinjaScript.AddOns
{
    public class BookDepthIQBridge : AddOnBase
    {
        public const int Port = 36971;
        // Every level NinjaTrader holds, up to this ceiling (it was 40, which hid the deeper book that a
        // Rithmic Level 2 feed delivers: the heatmap went blank 10 points from price).
        private const int Levels = 1000;
        private const int BookMs = 50;

        // One bridge per NinjaTrader process, whatever number of AddOn instances NT creates.
        private static readonly object Gate = new object();
        private static BridgeServer server;
        /// <summary>The AddOn instance that started the server; its Terminated (recompile, NT exit) stops it.</summary>
        private static BookDepthIQBridge owner;
        /// <summary>AppDomain slot shared by every compiled copy of this AddOn: the running bridge's stop
        /// action. After an F5 the new copy calls it, so the old listener frees the port.</summary>
        private const string StopSlot = "BookDepthIQ.Bridge.Stop";

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "BookDepthIQBridge";
                Description = "Streams depth, prints, account and orders to the BookDepthIQ DOM app on 127.0.0.1 and executes its click orders.";
            }
            else if (State == State.Active || State == State.Configure)
            {
                lock (Gate)
                {
                    if (server == null)
                    {
                        // An older compiled copy may still hold the port: ask it to stop first.
                        var old = AppDomain.CurrentDomain.GetData(StopSlot) as Action;
                        if (old != null) { try { old(); } catch { } }
                        server = new BridgeServer();
                        owner = this;
                        AppDomain.CurrentDomain.SetData(StopSlot, (Action)StopServer);
                        server.Start();
                    }
                }
            }
            else if (State == State.Terminated)
            {
                // NT terminates AddOn clones often; only the instance that started the server stops it.
                if (owner == this) StopServer();
            }
        }

        private static void StopServer()
        {
            lock (Gate)
            {
                if (server != null) server.Stop();
                server = null;
                owner = null;
            }
        }

        private sealed class BridgeServer
        {
            private readonly JavaScriptSerializer json = new JavaScriptSerializer();
            private readonly BlockingCollection<string> outbox = new BlockingCollection<string>(new ConcurrentQueue<string>(), 20000);
            private TcpListener listener;
            private TcpClient client;
            private readonly object clientGate = new object();

            private Instrument instrument;
            private MarketDepth<MarketDepthRow> depth;
            private MarketData marketData;
            private Account account;
            private double lastBid, lastAsk;
            private readonly object bookGate = new object();
            private double[][] bookBids = new double[0][], bookAsks = new double[0][];
            private bool bookDirty;
            private Timer bookTimer;

            // Entries sent with a bracket. Like NT's ATM, each target gets its own stop as one OCO pair;
            // every execution of the entry is allocated to the targets in order, so a partial fill is
            // protected at once.
            private sealed class Bracket
            {
                public int[] TpTicks;   // per target, 0 = no target for that slice
                public int[] Qty;       // contracts per target
                public int[] Done;      // contracts already bracketed per target
                public int SlTicks;
            }
            private readonly ConcurrentDictionary<Order, Bracket> brackets = new ConcurrentDictionary<Order, Bracket>();
            private int ocoSeq;

            private volatile bool stopped;

            public void Start()
            {
                new Thread(AcceptLoop) { IsBackground = true, Name = "BDIQ bridge accept" }.Start();
                new Thread(WriteLoop) { IsBackground = true, Name = "BDIQ bridge write" }.Start();
                bookTimer = new Timer(_ => FlushBook(), null, BookMs, BookMs);
            }

            // ───────────── socket ─────────────

            /// <summary>Stops listening and drops the DOM connection (recompile or NinjaTrader exit).</summary>
            public void Stop()
            {
                stopped = true;
                try { if (listener != null) listener.Stop(); } catch { }
                lock (clientGate) { if (client != null) try { client.Close(); } catch { } client = null; }
                if (bookTimer != null) bookTimer.Dispose();
                outbox.CompleteAdding();
                var inst = instrument;
                var d = depth;
                var md = marketData;
                if (inst != null) inst.Dispatcher.InvokeAsync(() => { if (d != null) d.Update -= OnDepth; if (md != null) md.Update -= OnMarketData; });
                var a = account;
                if (a != null)
                {
                    a.OrderUpdate -= OnOrderUpdate;
                    a.ExecutionUpdate -= OnExecutionUpdate;
                    a.PositionUpdate -= OnPositionUpdate;
                    a.AccountItemUpdate -= OnAccountItemUpdate;
                }
                Log("BookDepthIQ bridge stopped");
            }

            private void AcceptLoop()
            {
                // Bind, retrying every 2 s while another program (or an older copy) holds the port.
                var warned = false;
                while (!stopped)
                {
                    try { listener = new TcpListener(IPAddress.Loopback, Port); listener.Start(); break; }
                    catch (Exception e)
                    {
                        if (!warned) { Log("BookDepthIQ bridge: port " + Port + " busy (" + e.Message + ") — retrying; restart NinjaTrader once if this persists"); warned = true; }
                        Thread.Sleep(2000);
                    }
                }
                if (stopped) return;
                Log("BookDepthIQ bridge listening on 127.0.0.1:" + Port);
                while (!stopped)
                {
                    TcpClient c;
                    try { c = listener.AcceptTcpClient(); }
                    catch (Exception e) { if (!stopped) Log("bridge accept stopped: " + e.Message); return; }
                    c.NoDelay = true;
                    lock (clientGate)
                    {
                        if (client != null) try { client.Close(); } catch { }
                        client = c;
                    }
                    SendHello();
                    var t = new Thread(() => ReadLoop(c)) { IsBackground = true, Name = "BDIQ bridge read" };
                    t.Start();
                }
            }

            private void ReadLoop(TcpClient c)
            {
                try
                {
                    var reader = new StreamReader(c.GetStream(), new UTF8Encoding(false));
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Length == 0) continue;
                        Dictionary<string, object> m;
                        try { m = json.Deserialize<Dictionary<string, object>>(line); }
                        catch (Exception e) { Send(new Dictionary<string, object> { { "t", "log" }, { "msg", "bad command: " + e.Message } }); continue; }
                        try { Handle(m); }
                        catch (Exception e) { Ack(Str(m, "cid"), false, e.Message); }
                    }
                }
                catch (Exception) { }
                lock (clientGate) { if (client == c) client = null; }
            }

            private void WriteLoop()
            {
                foreach (var line in outbox.GetConsumingEnumerable())
                {
                    if (stopped) return;
                {
                    TcpClient c;
                    lock (clientGate) c = client;
                    if (c == null) continue;
                    try
                    {
                        var bytes = Encoding.UTF8.GetBytes(line + "\n");
                        c.GetStream().Write(bytes, 0, bytes.Length);
                    }
                    catch (Exception) { lock (clientGate) { if (client == c) client = null; } }
                }
                }
            }

            private void Send(Dictionary<string, object> m)
            {
                bool connected;
                lock (clientGate) connected = client != null;
                if (connected && !stopped && !outbox.IsAddingCompleted) { try { outbox.TryAdd(json.Serialize(m)); } catch (InvalidOperationException) { } }
            }

            private void Ack(string cid, bool ok, string msg)
            {
                Send(new Dictionary<string, object> { { "t", "ack" }, { "cid", cid ?? "" }, { "ok", ok }, { "msg", msg ?? "" } });
            }

            private static void Log(string msg)
            {
                NinjaTrader.NinjaScript.NinjaScript.Log(msg, LogLevel.Information);
            }

            // ───────────── commands (each one comes from a click in the DOM) ─────────────

            private void Handle(Dictionary<string, object> m)
            {
                var t = Str(m, "t");
                var cid = Str(m, "cid");
                switch (t)
                {
                    case "sub": OnUi(() => Subscribe(Str(m, "inst"))); break;
                    case "acct": OnUi(() => SelectAccount(Str(m, "name"))); break;
                    case "order": OnUi(() => PlaceOrder(m, cid)); break;
                    case "cancel": OnUi(() => CancelOrder(m, cid)); break;
                    case "move": OnUi(() => MoveOrder(m, cid)); break;
                    case "flatten": OnUi(() => FlattenInstrument(m, cid)); break;
                    case "cancelAll": OnUi(() => CancelAllOrders(m, cid)); break;
                    case "closePart": OnUi(() => ClosePart(m, cid)); break;
                    case "addStop": OnUi(() => AddStop(m, cid)); break;
                    case "hist": OnUi(() => History(m)); break;
                    case "kill": OnUi(() => Kill(m, cid)); break;
                    default: Ack(cid, false, "unknown command " + t); break;
                }
            }

            /// <summary>Order and subscription work runs on NinjaTrader's UI thread, as the SuperDOM's does.</summary>
            private void OnUi(Action a)
            {
                var app = Application.Current;
                if (app == null) { a(); return; }
                app.Dispatcher.InvokeAsync(() =>
                {
                    try { a(); }
                    catch (Exception e) { Send(new Dictionary<string, object> { { "t", "log" }, { "msg", e.Message } }); }
                });
            }

            private void SendHello()
            {
                var accts = new List<object>();
                lock (Account.All)
                    foreach (var a in Account.All)
                        accts.Add(new Dictionary<string, object> { { "name", a.Name }, { "conn", a.ConnectionStatus.ToString() } });
                Send(new Dictionary<string, object> { { "t", "hello" }, { "v", 2 }, { "accounts", accts } });
                if (instrument != null) SendInstrument();
                if (account != null) { SendAccount(); SendPositions(); SendOrders(); }
            }

            private void Subscribe(string name)
            {
                var inst = Instrument.GetInstrument(name, false);
                if (inst == null) { Send(new Dictionary<string, object> { { "t", "log" }, { "msg", "no NinjaTrader instrument named '" + name + "'" } }); return; }
                if (inst == instrument) { SendInstrument(); return; }
                Unsubscribe();
                instrument = inst;
                lastBid = lastAsk = 0;
                SendInstrument();
                inst.Dispatcher.InvokeAsync(() =>
                {
                    depth = new MarketDepth<MarketDepthRow>(inst);
                    depth.Update += OnDepth;
                    marketData = new MarketData(inst);
                    marketData.Update += OnMarketData;
                });
            }

            private void Unsubscribe()
            {
                var d = depth;
                var md = marketData;
                var inst = instrument;
                depth = null;
                marketData = null;
                if (inst == null) return;
                inst.Dispatcher.InvokeAsync(() =>
                {
                    if (d != null) d.Update -= OnDepth;
                    if (md != null) md.Update -= OnMarketData;
                });
                lock (bookGate) { bookBids = new double[0][]; bookAsks = new double[0][]; bookDirty = true; }
            }

            private void SendInstrument()
            {
                var mi = instrument.MasterInstrument;
                Send(new Dictionary<string, object>
                {
                    { "t", "inst" }, { "name", instrument.FullName }, { "tick", mi.TickSize }, { "pv", mi.PointValue }, { "desc", mi.Description ?? "" },
                });
            }

            private void SelectAccount(string name)
            {
                Account a;
                lock (Account.All) a = Account.All.FirstOrDefault(x => x.Name == name);
                if (a == null) { Send(new Dictionary<string, object> { { "t", "log" }, { "msg", "no account named '" + name + "'" } }); return; }
                if (account != null)
                {
                    account.OrderUpdate -= OnOrderUpdate;
                    account.ExecutionUpdate -= OnExecutionUpdate;
                    account.PositionUpdate -= OnPositionUpdate;
                    account.AccountItemUpdate -= OnAccountItemUpdate;
                }
                account = a;
                a.OrderUpdate += OnOrderUpdate;
                a.ExecutionUpdate += OnExecutionUpdate;
                a.PositionUpdate += OnPositionUpdate;
                a.AccountItemUpdate += OnAccountItemUpdate;
                SendAccount();
                SendPositions();
                SendOrders();
            }

            private Account AccountFor(Dictionary<string, object> m)
            {
                var name = Str(m, "acct");
                lock (Account.All) return Account.All.FirstOrDefault(x => x.Name == name);
            }

            private Instrument InstrumentFor(Dictionary<string, object> m)
            {
                var name = Str(m, "inst");
                return string.IsNullOrEmpty(name) ? instrument : Instrument.GetInstrument(name, false);
            }

            private void PlaceOrder(Dictionary<string, object> m, string cid)
            {
                var a = AccountFor(m);
                var inst = InstrumentFor(m);
                if (a == null) { Ack(cid, false, "no account " + Str(m, "acct")); return; }
                if (inst == null) { Ack(cid, false, "no instrument " + Str(m, "inst")); return; }
                var qty = Int(m, "qty");
                if (qty <= 0) { Ack(cid, false, "quantity must be > 0"); return; }
                var buy = Str(m, "side") == "buy";
                var type = Str(m, "type");
                var ot = type == "limit" ? OrderType.Limit : type == "stop" ? OrderType.StopMarket : OrderType.Market;
                var price = Num(m, "price");
                if (ot != OrderType.Market && !(price > 0)) { Ack(cid, false, type + " order needs a price"); return; }
                var mi = inst.MasterInstrument;
                var px = ot == OrderType.Market ? 0 : mi.RoundToTickSize(price);
                var pos = a.Positions.FirstOrDefault(p => p.Instrument == inst);
                var posQty = pos == null ? 0 : pos.MarketPosition == MarketPosition.Long ? pos.Quantity : pos.MarketPosition == MarketPosition.Short ? -pos.Quantity : 0;
                var action = buy ? (posQty < 0 ? OrderAction.BuyToCover : OrderAction.Buy) : (posQty > 0 ? OrderAction.Sell : OrderAction.SellShort);
                var o = a.CreateOrder(inst, action, ot, OrderEntry.Manual, TimeInForce.Day, qty, ot == OrderType.Limit ? px : 0, ot == OrderType.StopMarket ? px : 0, "", "BDIQ entry", Core.Globals.MaxDate, null);
                var sl = Int(m, "sl");
                var tps = new List<int>();
                var qs = new List<int>();
                object tv;
                if (m.TryGetValue("targets", out tv) && tv is System.Collections.ArrayList)
                    foreach (var t in (System.Collections.ArrayList)tv)
                    {
                        var pair = t as System.Collections.ArrayList;
                        if (pair == null || pair.Count < 2) continue;
                        tps.Add(Convert.ToInt32(pair[0], System.Globalization.CultureInfo.InvariantCulture));
                        qs.Add(Convert.ToInt32(pair[1], System.Globalization.CultureInfo.InvariantCulture));
                    }
                if (tps.Count == 0 && Int(m, "tp") > 0) { tps.Add(Int(m, "tp")); qs.Add(qty); }
                if (tps.Count == 0 && sl > 0) { tps.Add(0); qs.Add(qty); }
                if (tps.Count > 0 && qs.Sum() != qty) { Ack(cid, false, "target sizes add up to " + qs.Sum() + ", not " + qty); return; }
                if (tps.Count > 0) brackets[o] = new Bracket { TpTicks = tps.ToArray(), Qty = qs.ToArray(), Done = new int[tps.Count], SlTicks = sl };
                a.Submit(new[] { o });
                Ack(cid, true, string.Format("{0} {1} {2} {3}{4} sent to {5}", action, qty, inst.FullName, ot, ot == OrderType.Market ? "" : " @ " + px, a.Name));
            }

            private Order FindOrder(Account a, string id)
            {
                lock (a.Orders) return a.Orders.FirstOrDefault(o => o.OrderId == id || o.Id.ToString() == id);
            }

            private void CancelOrder(Dictionary<string, object> m, string cid)
            {
                var a = AccountFor(m);
                if (a == null) { Ack(cid, false, "no account"); return; }
                var o = FindOrder(a, Str(m, "id"));
                if (o == null) { Ack(cid, false, "order not found (filled or gone)"); return; }
                a.Cancel(new[] { o });
                Ack(cid, true, "cancel sent");
            }

            private void MoveOrder(Dictionary<string, object> m, string cid)
            {
                var a = AccountFor(m);
                if (a == null) { Ack(cid, false, "no account"); return; }
                var o = FindOrder(a, Str(m, "id"));
                if (o == null) { Ack(cid, false, "order not found (filled or gone)"); return; }
                var px = o.Instrument.MasterInstrument.RoundToTickSize(Num(m, "price"));
                if (o.OrderType == OrderType.Limit || o.OrderType == OrderType.MIT) o.LimitPriceChanged = px;
                else if (o.OrderType == OrderType.StopMarket) o.StopPriceChanged = px;
                else if (o.OrderType == OrderType.StopLimit) { var off = o.LimitPrice - o.StopPrice; o.StopPriceChanged = px; o.LimitPriceChanged = px + off; }
                else { Ack(cid, false, "a " + o.OrderType + " order can't be moved"); return; }
                a.Change(new[] { o });
                Ack(cid, true, "move to " + px + " sent");
            }

            // FLATTEN and CANCEL ALL are never gated.
            private void FlattenInstrument(Dictionary<string, object> m, string cid)
            {
                var a = AccountFor(m);
                var inst = InstrumentFor(m);
                if (a == null || inst == null) { Ack(cid, false, "no account or instrument"); return; }
                a.Flatten(new[] { inst });
                Ack(cid, true, "flatten sent: " + inst.FullName);
            }

            private void CancelAllOrders(Dictionary<string, object> m, string cid)
            {
                var a = AccountFor(m);
                var inst = InstrumentFor(m);
                if (a == null || inst == null) { Ack(cid, false, "no account or instrument"); return; }
                a.CancelAllOrders(inst);
                Ack(cid, true, "cancel all sent: " + inst.FullName);
            }

            /// <summary>Close part of a position at market, then trim the panel's bracket legs from the last
            /// target backwards so no leg is larger than what is left. An exit: never gated.</summary>
            private void ClosePart(Dictionary<string, object> m, string cid)
            {
                var a = AccountFor(m);
                var inst = InstrumentFor(m);
                if (a == null || inst == null) { Ack(cid, false, "no account or instrument"); return; }
                var pos = a.Positions.FirstOrDefault(p => p.Instrument == inst);
                if (pos == null || pos.MarketPosition == MarketPosition.Flat) { Ack(cid, false, "flat — nothing to close"); return; }
                var qty = Math.Min(Int(m, "qty"), pos.Quantity);
                if (qty <= 0) { Ack(cid, false, "quantity must be > 0"); return; }
                if (qty >= pos.Quantity) { a.Flatten(new[] { inst }); Ack(cid, true, "that is the whole position: flatten sent"); return; }
                var isLong = pos.MarketPosition == MarketPosition.Long;
                var o = a.CreateOrder(inst, isLong ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.Market, OrderEntry.Manual, TimeInForce.Day, qty, 0, 0, "", "BDIQ close", Core.Globals.MaxDate, null);
                a.Submit(new[] { o });
                // Trim legs: the panel's working TP/SL pairs, furthest target first.
                var left = qty;
                List<Order> legs;
                lock (a.Orders) legs = a.Orders.Where(x => x.Instrument == inst && IsWorking(x) && x.Name != null && x.Name.StartsWith("BDIQ TP")).OrderByDescending(x => x.Name).ToList();
                var change = new List<Order>();
                var cancel = new List<Order>();
                foreach (var tp in legs)
                {
                    if (left <= 0) break;
                    var stop = FindPair(a, tp);
                    var cut = Math.Min(left, tp.Quantity - tp.Filled);
                    left -= cut;
                    if (cut >= tp.Quantity - tp.Filled) { cancel.Add(tp); if (stop != null) cancel.Add(stop); }
                    else { tp.QuantityChanged = tp.Quantity - cut; change.Add(tp); if (stop != null) { stop.QuantityChanged = stop.Quantity - cut; change.Add(stop); } }
                }
                if (left > 0)
                {
                    // Stops without targets: shrink them too.
                    List<Order> stops;
                    lock (a.Orders) stops = a.Orders.Where(x => x.Instrument == inst && IsWorking(x) && x.Name != null && x.Name.StartsWith("BDIQ SL") && !change.Contains(x) && !cancel.Contains(x)).ToList();
                    foreach (var st in stops)
                    {
                        if (left <= 0) break;
                        var cut = Math.Min(left, st.Quantity);
                        left -= cut;
                        if (cut >= st.Quantity) cancel.Add(st); else { st.QuantityChanged = st.Quantity - cut; change.Add(st); }
                    }
                }
                if (cancel.Count > 0) a.Cancel(cancel);
                if (change.Count > 0) a.Change(change);
                Ack(cid, true, "close " + qty + " of " + pos.Quantity + " sent; bracket legs trimmed");
            }

            /// <summary>KILL: every position on the account closed at market and every order cancelled. Never gated.</summary>
            private void Kill(Dictionary<string, object> m, string cid)
            {
                var a = AccountFor(m);
                if (a == null) { Ack(cid, false, "no account"); return; }
                List<Instrument> insts;
                lock (a.Positions) insts = a.Positions.Where(p => p.MarketPosition != MarketPosition.Flat).Select(p => p.Instrument).ToList();
                List<Order> working;
                lock (a.Orders) working = a.Orders.Where(IsWorking).ToList();
                if (working.Count > 0) a.Cancel(working);
                if (insts.Count > 0) a.Flatten(insts);
                Ack(cid, true, "KILL: " + insts.Count + " position(s) flattened, " + working.Count + " order(s) cancelled on " + a.Name);
            }

            private Order FindPair(Account a, Order leg)
            {
                if (string.IsNullOrEmpty(leg.Oco)) return null;
                lock (a.Orders) return a.Orders.FirstOrDefault(x => x != leg && x.Oco == leg.Oco && IsWorking(x));
            }

            /// <summary>Place a protective stop for the position (stop guard, or a stop the user adds).</summary>
            private void AddStop(Dictionary<string, object> m, string cid)
            {
                var a = AccountFor(m);
                var inst = InstrumentFor(m);
                if (a == null || inst == null) { Ack(cid, false, "no account or instrument"); return; }
                var pos = a.Positions.FirstOrDefault(p => p.Instrument == inst);
                if (pos == null || pos.MarketPosition == MarketPosition.Flat) { Ack(cid, false, "flat — no stop needed"); return; }
                var qty = Int(m, "qty") > 0 ? Math.Min(Int(m, "qty"), pos.Quantity) : pos.Quantity;
                var px = inst.MasterInstrument.RoundToTickSize(Num(m, "price"));
                var isLong = pos.MarketPosition == MarketPosition.Long;
                var o = a.CreateOrder(inst, isLong ? OrderAction.Sell : OrderAction.BuyToCover, OrderType.StopMarket, OrderEntry.Manual, TimeInForce.Gtc, qty, 0, px, "", "BDIQ SL", Core.Globals.MaxDate, null);
                a.Submit(new[] { o });
                Ack(cid, true, "stop " + qty + " @ " + px + " sent");
            }

            /// <summary>Past prints for seeding SuperTrend, VWAP, the opening range and the profile: 1-tick
            /// bars from NinjaTrader's history, side from the bid/ask stored with each tick.</summary>
            private void History(Dictionary<string, object> m)
            {
                var inst = InstrumentFor(m);
                if (inst == null) return;
                var hours = Math.Max(1, Math.Min(48, Int(m, "hours")));
                var req = new BarsRequest(inst, DateTime.Now.AddHours(-hours), DateTime.Now);
                req.BarsPeriod = new BarsPeriod { BarsPeriodType = BarsPeriodType.Tick, Value = 1 };
                req.TradingHours = inst.MasterInstrument.TradingHours;
                req.Request((r, err, msg) =>
                {
                    if (err != ErrorCode.NoError || r.Bars == null)
                    {
                        Send(new Dictionary<string, object> { { "t", "hist" }, { "inst", inst.FullName }, { "trades", new object[0] }, { "done", true }, { "err", msg ?? err.ToString() } });
                        return;
                    }
                    var bars = r.Bars;
                    var chunk = new List<object>();
                    double prev = 0;
                    for (var i = 0; i < bars.Count; i++)
                    {
                        var px = bars.GetClose(i);
                        var ask = bars.GetAsk(i);
                        var bid = bars.GetBid(i);
                        var side = ask > 0 && px >= ask ? "b" : bid > 0 && px <= bid ? "s" : px > prev ? "b" : px < prev ? "s" : "?";
                        prev = px;
                        chunk.Add(new object[] { ToMs(bars.GetTime(i)), px, bars.GetVolume(i), side });
                        if (chunk.Count >= 5000)
                        {
                            Send(new Dictionary<string, object> { { "t", "hist" }, { "inst", inst.FullName }, { "trades", chunk }, { "done", false } });
                            chunk = new List<object>();
                        }
                    }
                    Send(new Dictionary<string, object> { { "t", "hist" }, { "inst", inst.FullName }, { "trades", chunk }, { "done", true } });
                });
            }

            private static bool IsWorking(Order o)
            {
                var s = o.OrderState;
                return s == OrderState.Working || s == OrderState.Accepted || s == OrderState.Submitted || s == OrderState.TriggerPending
                    || s == OrderState.PartFilled || s == OrderState.ChangePending || s == OrderState.ChangeSubmitted || s == OrderState.Initialized;
            }

            // ───────────── market data ─────────────

            private void OnDepth(object sender, MarketDepthEventArgs e)
            {
                var d = depth;
                if (d == null || e.Instrument != instrument) return;
                var b = d.Bids.Take(Levels).Select(r => new[] { r.Price, (double)r.Volume }).ToArray();
                var a = d.Asks.Take(Levels).Select(r => new[] { r.Price, (double)r.Volume }).ToArray();
                lock (bookGate) { bookBids = b; bookAsks = a; bookDirty = true; }
            }

            private void FlushBook()
            {
                double[][] b, a;
                lock (bookGate)
                {
                    if (!bookDirty) return;
                    bookDirty = false;
                    b = bookBids;
                    a = bookAsks;
                }
                Send(new Dictionary<string, object> { { "t", "book" }, { "bids", b }, { "asks", a }, { "ms", NowMs() } });
            }

            private void OnMarketData(object sender, MarketDataEventArgs e)
            {
                if (e.Instrument != instrument) return;
                if (e.MarketDataType == MarketDataType.Bid) lastBid = e.Price;
                else if (e.MarketDataType == MarketDataType.Ask) lastAsk = e.Price;
                else if (e.MarketDataType == MarketDataType.Last)
                {
                    var side = lastAsk > 0 && e.Price >= lastAsk ? "b" : lastBid > 0 && e.Price <= lastBid ? "s" : "?";
                    Send(new Dictionary<string, object> { { "t", "trade" }, { "p", e.Price }, { "v", e.Volume }, { "side", side }, { "ms", ToMs(e.Time) } });
                    return;
                }
                else return;
                Send(new Dictionary<string, object> { { "t", "quote" }, { "bid", lastBid }, { "ask", lastAsk } });
            }

            // ───────────── account ─────────────

            private void OnAccountItemUpdate(object sender, AccountItemEventArgs e) { SendAccount(); }
            private void OnPositionUpdate(object sender, PositionEventArgs e) { SendPositions(); }
            private void OnOrderUpdate(object sender, OrderEventArgs e)
            {
                // A rejection says why, in the DOM's result bar and order log (NinjaTrader's own text).
                if (e.OrderState == OrderState.Rejected && e.Order != null)
                    Send(new Dictionary<string, object>
                    {
                        { "t", "reject" }, { "name", e.Order.Name ?? "" }, { "inst", e.Order.Instrument.FullName }, { "action", e.Order.OrderAction.ToString() },
                        { "type", e.Order.OrderType.ToString() }, { "qty", e.Order.Quantity }, { "error", e.Error.ToString() }, { "msg", e.Comment ?? "" },
                    });
                SendOrders();
            }

            private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
            {
                var ex = e.Execution;
                if (ex == null) return;
                Send(new Dictionary<string, object>
                {
                    { "t", "exec" }, { "acct", ex.Account.Name }, { "inst", ex.Instrument.FullName }, { "id", ex.ExecutionId ?? "" },
                    { "name", ex.Order != null ? ex.Order.Name ?? "" : "" }, { "side", ex.Order != null ? (ex.Order.OrderAction == OrderAction.Buy || ex.Order.OrderAction == OrderAction.BuyToCover ? "b" : "s") : (e.MarketPosition == MarketPosition.Long ? "b" : "s") },
                    { "qty", ex.Quantity }, { "price", ex.Price }, { "commission", ex.Commission }, { "fee", ex.Fee }, { "ms", ToMs(ex.Time) },
                });
                if (ex.Order == null) return;
                Bracket br;
                if (!brackets.TryGetValue(ex.Order, out br)) return;
                // Allocate this execution's quantity to the targets in order; each slice gets its own OCO pair.
                var a = ex.Account;
                var inst = ex.Instrument;
                var mi = inst.MasterInstrument;
                var isLong = ex.Order.OrderAction == OrderAction.Buy || ex.Order.OrderAction == OrderAction.BuyToCover;
                var exitAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
                var left = ex.Quantity;
                var legs = new List<Order>();
                lock (br)
                {
                    for (var i = 0; i < br.TpTicks.Length && left > 0; i++)
                    {
                        var take = Math.Min(left, br.Qty[i] - br.Done[i]);
                        if (take <= 0) continue;
                        br.Done[i] += take;
                        left -= take;
                        var both = br.TpTicks[i] > 0 && br.SlTicks > 0;
                        var oco = both ? "BDIQ-" + Interlocked.Increment(ref ocoSeq) + "-" + NowMs() : "";
                        if (br.TpTicks[i] > 0)
                        {
                            var tpPx = mi.RoundToTickSize(ex.Price + (isLong ? 1 : -1) * br.TpTicks[i] * mi.TickSize);
                            legs.Add(a.CreateOrder(inst, exitAction, OrderType.Limit, OrderEntry.Manual, TimeInForce.Gtc, take, tpPx, 0, oco, "BDIQ TP" + (i + 1), Core.Globals.MaxDate, null));
                        }
                        if (br.SlTicks > 0)
                        {
                            var slPx = mi.RoundToTickSize(ex.Price - (isLong ? 1 : -1) * br.SlTicks * mi.TickSize);
                            legs.Add(a.CreateOrder(inst, exitAction, OrderType.StopMarket, OrderEntry.Manual, TimeInForce.Gtc, take, 0, slPx, oco, "BDIQ SL" + (i + 1), Core.Globals.MaxDate, null));
                        }
                    }
                    if (br.Done.Sum() >= br.Qty.Sum()) { Bracket gone; brackets.TryRemove(ex.Order, out gone); }
                }
                if (legs.Count > 0) OnUi(() => a.Submit(legs));
            }

            private void SendAccount()
            {
                var a = account;
                if (a == null) return;
                Send(new Dictionary<string, object>
                {
                    { "t", "account" }, { "name", a.Name },
                    { "cash", a.Get(AccountItem.CashValue, Currency.UsDollar) },
                    { "realized", a.Get(AccountItem.RealizedProfitLoss, Currency.UsDollar) },
                    { "unrealized", a.Get(AccountItem.UnrealizedProfitLoss, Currency.UsDollar) },
                    { "netliq", a.Get(AccountItem.NetLiquidation, Currency.UsDollar) },
                    { "trailDd", a.Get(AccountItem.TrailingMaxDrawdown, Currency.UsDollar) },
                    { "dailyLimit", a.Get(AccountItem.DailyLossLimit, Currency.UsDollar) },
                    { "commission", a.Get(AccountItem.Commission, Currency.UsDollar) },
                });
            }

            private void SendPositions()
            {
                var a = account;
                if (a == null) return;
                List<Position> list;
                lock (a.Positions) list = a.Positions.ToList();
                if (instrument != null && list.All(p => p.Instrument != instrument))
                    Send(new Dictionary<string, object> { { "t", "pos" }, { "acct", a.Name }, { "inst", instrument.FullName }, { "qty", 0 }, { "avg", 0.0 } });
                foreach (var p in list)
                {
                    var q = p.MarketPosition == MarketPosition.Long ? p.Quantity : p.MarketPosition == MarketPosition.Short ? -p.Quantity : 0;
                    Send(new Dictionary<string, object> { { "t", "pos" }, { "acct", a.Name }, { "inst", p.Instrument.FullName }, { "qty", q }, { "avg", p.AveragePrice } });
                }
            }

            private void SendOrders()
            {
                var a = account;
                if (a == null) return;
                List<Order> list;
                lock (a.Orders) list = a.Orders.Where(o => o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted || o.OrderState == OrderState.Submitted
                    || o.OrderState == OrderState.TriggerPending || o.OrderState == OrderState.PartFilled || o.OrderState == OrderState.ChangePending
                    || o.OrderState == OrderState.ChangeSubmitted || o.OrderState == OrderState.CancelPending || o.OrderState == OrderState.Initialized).ToList();
                var rows = list.Select(o => (object)new Dictionary<string, object>
                {
                    { "id", string.IsNullOrEmpty(o.OrderId) ? o.Id.ToString() : o.OrderId }, { "inst", o.Instrument.FullName },
                    { "action", o.OrderAction.ToString() }, { "type", o.OrderType.ToString() }, { "qty", o.Quantity }, { "filled", o.Filled },
                    { "limit", o.LimitPrice }, { "stop", o.StopPrice }, { "state", o.OrderState.ToString() }, { "oco", o.Oco ?? "" }, { "name", o.Name ?? "" },
                }).ToList();
                Send(new Dictionary<string, object> { { "t", "orders" }, { "acct", a.Name }, { "list", rows } });
            }

            // ───────────── helpers ─────────────

            private static long NowMs() { return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }
            private static long ToMs(DateTime t) { return (long)(t.ToUniversalTime() - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds; }
            private static string Str(Dictionary<string, object> m, string k) { object v; return m.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null; }
            private static int Int(Dictionary<string, object> m, string k) { object v; return m.TryGetValue(k, out v) && v != null ? Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture) : 0; }
            private static double Num(Dictionary<string, object> m, string k) { object v; return m.TryGetValue(k, out v) && v != null ? Convert.ToDouble(v, System.Globalization.CultureInfo.InvariantCulture) : 0; }
        }
    }
}
