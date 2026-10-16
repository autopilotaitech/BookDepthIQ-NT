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
            private Timer accountTimer;
            private string accountSignature;

            // Entries sent with a bracket. Like NT's ATM, each target gets its own stop as one OCO pair;
            // every execution of the entry is allocated to the targets in order, so a partial fill is
            // protected at once.
            private sealed class Bracket
            {
                public int[] TpTicks;   // per target, 0 = no target for that slice
                public int[] Qty;       // contracts per target
                public int[] Done;      // contracts already bracketed per target
                public int SlTicks;
                public int[] Numbers; // original target identity, retained across SCALE children
                public Account Owner;
                public HashSet<string> Executions = new HashSet<string>();
            }
            private readonly ConcurrentDictionary<Order, Bracket> brackets = new ConcurrentDictionary<Order, Bracket>();
            private readonly HashSet<Account> executionAccounts = new HashSet<Account>();
            private sealed class Reduction { public Account Owner; public Instrument Instrument; public bool Long; }
            private readonly ConcurrentDictionary<Order,Reduction> reductions = new ConcurrentDictionary<Order,Reduction>();
            private readonly HashSet<Account> reconciling = new HashSet<Account>();
            private readonly ConcurrentDictionary<Order,int> changingQuantity = new ConcurrentDictionary<Order,int>();
            private readonly ConcurrentDictionary<Tuple<Account,Instrument>,long> positionEpochs = new ConcurrentDictionary<Tuple<Account,Instrument>,long>();
            private sealed class ProtectionBatch
            { public Account Owner; public Instrument Instrument; public bool Long; public long Epoch; public List<ProtectionSlice> Slices; }
            private readonly ConcurrentDictionary<ProtectionBatch,byte> pendingProtection = new ConcurrentDictionary<ProtectionBatch,byte>();
            private readonly HashSet<Account> flushingProtection = new HashSet<Account>();

            private void WatchExecutions(Account a)
            {
                lock(executionAccounts) if(executionAccounts.Add(a))
                { a.ExecutionUpdate += OnExecutionUpdate; a.PositionUpdate += OnPositionUpdate; a.OrderUpdate += OnOrderUpdate; }
            }
            private void ReleaseExecutions(Account a)
            {
                if(a==account || brackets.Values.Any(b=>b.Owner==a) || reductions.Values.Any(r=>r.Owner==a) || pendingProtection.Keys.Any(b=>b.Owner==a)) return;
                lock(a.Orders) if(a.Orders.Any(o=>Active(o) && OwnedBracket(o))) return;
                lock(executionAccounts) if(executionAccounts.Remove(a))
                { a.ExecutionUpdate -= OnExecutionUpdate; a.PositionUpdate -= OnPositionUpdate; a.OrderUpdate -= OnOrderUpdate; }
            }
            private int ocoSeq;

            private volatile bool stopped;

            public void Start()
            {
                new Thread(AcceptLoop) { IsBackground = true, Name = "BDIQ bridge accept" }.Start();
                new Thread(WriteLoop) { IsBackground = true, Name = "BDIQ bridge write" }.Start();
                bookTimer = new Timer(_ => FlushBook(), null, BookMs, BookMs);
                accountTimer = new Timer(_ => OnUi(() => SendAccounts(false, false)), null, 2000, 2000);
            }

            // ───────────── socket ─────────────

            /// <summary>Stops listening and drops the DOM connection (recompile or NinjaTrader exit).</summary>
            public void Stop()
            {
                stopped = true;
                try { if (listener != null) listener.Stop(); } catch { }
                lock (clientGate) { if (client != null) try { client.Close(); } catch { } client = null; }
                if (bookTimer != null) bookTimer.Dispose();
                if (accountTimer != null) accountTimer.Dispose();
                outbox.CompleteAdding();
                var inst = instrument;
                var d = depth;
                var md = marketData;
                if (inst != null) inst.Dispatcher.InvokeAsync(() => { if (d != null) d.Update -= OnDepth; if (md != null) md.Update -= OnMarketData; });
                var a = account;
                if (a != null)
                {
                    a.AccountItemUpdate -= OnAccountItemUpdate;
                }
                lock(executionAccounts)
                {
                    foreach(var watched in executionAccounts)
                    { watched.ExecutionUpdate -= OnExecutionUpdate; watched.PositionUpdate -= OnPositionUpdate; watched.OrderUpdate -= OnOrderUpdate; }
                    executionAccounts.Clear();
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
                    case "accounts": OnUi(() => SendAccounts(false, true)); break;
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
                SendAccounts(true, true);
                if (instrument != null) SendInstrument();
                if (account != null) { SendAccount(); SendPositions(); SendOrders(); }
            }

            private void SendAccounts(bool hello, bool force)
            {
                lock (clientGate) { if (client == null || stopped) return; }
                var accts = new List<object>();
                lock (Account.All)
                    foreach (var a in Account.All.OrderBy(a => a.Name))
                        accts.Add(new Dictionary<string, object> { { "name", a.Name }, { "conn", a.ConnectionStatus.ToString() } });
                var signature = json.Serialize(accts);
                if (!force && signature == accountSignature) return;
                accountSignature = signature;
                Send(new Dictionary<string, object> { { "t", hello ? "hello" : "accounts" }, { "v", 2 }, { "orderVersion", 3 }, { "accounts", accts } });
                if(!hello && account!=null) { SendAccount(); SendPositions(); SendOrders(); }
            }

            private void Subscribe(string name)
            {
                var inst = Instrument.GetInstrument(name, false);
                if (inst == null) { Send(new Dictionary<string, object> { { "t", "log" }, { "msg", "no NinjaTrader instrument named '" + name + "'" } }); return; }
                if (inst == instrument) { SendInstrument(); SendPositions(); SendOrders(); return; }
                Unsubscribe();
                instrument = inst;
                lastBid = lastAsk = 0;
                SendInstrument();
                SendPositions(); SendOrders();
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
                    account.AccountItemUpdate -= OnAccountItemUpdate;
                }
                var previous=account;
                account = a;
                if(previous!=null) ReleaseExecutions(previous);
                WatchExecutions(a);
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
                var reducing = (posQty > 0 && !buy) || (posQty < 0 && buy);
                object reductionFlag;
                if(m.TryGetValue("reduceOnly",out reductionFlag) && reductionFlag is bool && (bool)reductionFlag && !reducing)
                { Ack(cid,false,"reduction refused: the original position is flat or has changed direction"); return; }
                if(reducing)
                {
                    var reserved=ReservedReductions(a,inst,ot==OrderType.StopMarket);
                    if(qty>Math.Abs(posQty)-reserved) { Ack(cid,false,"exit quantity exceeds the unreserved position — use REVERSE to change direction"); return; }
                }
                var action = buy ? (posQty < 0 ? OrderAction.BuyToCover : OrderAction.Buy) : (posQty > 0 ? OrderAction.Sell : OrderAction.SellShort);
                var o = a.CreateOrder(inst, action, ot, OrderEntry.Manual, TimeInForce.Day, qty, ot == OrderType.Limit ? px : 0, ot == OrderType.StopMarket ? px : 0, "", "BDIQ entry", Core.Globals.MaxDate, null);
                var sl = Int(m, "sl");
                var tps = new List<int>();
                var qs = new List<int>();
                var numbers = new List<int>();
                object tv;
                if (m.TryGetValue("targets", out tv) && tv is System.Collections.ArrayList)
                    foreach (var t in (System.Collections.ArrayList)tv)
                    {
                        var pair = t as System.Collections.ArrayList;
                        if (pair == null || pair.Count < 2) continue;
                        tps.Add(Convert.ToInt32(pair[0], System.Globalization.CultureInfo.InvariantCulture));
                        qs.Add(Convert.ToInt32(pair[1], System.Globalization.CultureInfo.InvariantCulture));
                        numbers.Add(pair.Count > 2 ? Convert.ToInt32(pair[2], System.Globalization.CultureInfo.InvariantCulture) : tps.Count);
                    }
                if (tps.Count == 0 && Int(m, "tp") > 0) { tps.Add(Int(m, "tp")); qs.Add(qty); numbers.Add(1); }
                if (tps.Count == 0 && sl > 0) { tps.Add(0); qs.Add(qty); numbers.Add(1); }
                if (tps.Count > 0 && qs.Sum() != qty) { Ack(cid, false, "target sizes add up to " + qs.Sum() + ", not " + qty); return; }
                // Opposite-side orders REDUCE current exposure: their fills must never open a bracket.
                if(reducing) reductions[o]=new Reduction {Owner=a,Instrument=inst,Long=posQty>0};
                else if (tps.Count > 0) brackets[o] = new Bracket { Owner=a, TpTicks = tps.ToArray(), Qty = qs.ToArray(), Numbers=numbers.ToArray(), Done = new int[tps.Count], SlTicks = sl };
                WatchExecutions(a);
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

            /// <summary>Submit a reduction; resize protection only after ACTUAL fills.</summary>
            private void ClosePart(Dictionary<string, object> m, string cid)
            {
                var a=AccountFor(m); var inst=InstrumentFor(m);
                if(a==null || inst==null) { Ack(cid,false,"no account or instrument"); return; }
                Position pos;
                lock(a.Positions) pos=a.Positions.FirstOrDefault(p=>p.Instrument==inst && p.MarketPosition!=MarketPosition.Flat);
                if(pos==null) { Ack(cid,false,"flat — nothing to close"); return; }
                var qty=Math.Min(Int(m,"qty"),pos.Quantity);
                if(qty<=0) { Ack(cid,false,"quantity must be > 0"); return; }
                if(qty==pos.Quantity) { a.Flatten(new[]{inst}); Ack(cid,true,"flatten sent"); return; }
                var reserved=ReservedReductions(a,inst,false);
                if(qty>pos.Quantity-reserved) { Ack(cid,false,"exit quantity exceeds the unreserved position"); return; }
                var isLong=pos.MarketPosition==MarketPosition.Long;
                var o=a.CreateOrder(inst,isLong?OrderAction.Sell:OrderAction.BuyToCover,OrderType.Market,OrderEntry.Manual,TimeInForce.Day,qty,0,0,"","BDIQ close",Core.Globals.MaxDate,null);
                reductions[o]=new Reduction {Owner=a,Instrument=inst,Long=isLong};
                WatchExecutions(a); a.Submit(new[]{o});
                Ack(cid,true,"close "+qty+" sent; protection reconciles on confirmed fills");
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
                    Send(LiveTradePacket(e, side));
                    return;
                }
                else return;
                Send(new Dictionary<string, object> { { "t", "quote" }, { "bid", lastBid }, { "ask", lastAsk } });
            }

            // ───────────── account ─────────────

            // Additive chart metadata only. Legacy analytics/execution timestamps remain unchanged.
            private static Dictionary<string, object> LiveTradePacket(MarketDataEventArgs e, string side)
            {
                var packet = new Dictionary<string, object> { { "t", "trade" }, { "p", e.Price }, { "v", e.Volume }, { "side", side }, { "ms", ToMs(e.Time) }, { "inst", e.Instrument.FullName } };
                try
                {
                    var utc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(e.Time, DateTimeKind.Unspecified), Core.Globals.GeneralOptions.TimeZoneInfo);
                    packet["chartMs"] = (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds;
                }
                catch (ArgumentException) { } // Unproved DST time: omit chart marker, keep legacy feed running.
                return packet;
            }

            private static bool OwnedBracket(Order o)
            { return o.Name!=null && (o.Name.StartsWith("BDIQ TP") || o.Name.StartsWith("BDIQ SL")); }
            private static bool Active(Order o)
            { return IsWorking(o) || (o.OrderState.ToString().StartsWith("Cancel") && o.OrderState.ToString()!="Cancelled"); }
            private int ReservedReductions(Account a,Instrument inst,bool stops)
            {
                return reductions.Where(r=>r.Value.Owner==a && r.Value.Instrument==inst && Active(r.Key)
                    && (r.Key.OrderType==OrderType.StopMarket || r.Key.OrderType==OrderType.StopLimit)==stops)
                    .Sum(r=>Math.Max(0,r.Key.Quantity-r.Key.Filled));
            }

            /// <summary>Only shrink/cancel owned protection to match ACTUAL remaining exposure.
            /// Never attach a new bracket to a reduction or modify unrelated native orders.</summary>
            private void ReconcileProtection(Account a)
            {
                lock(reconciling) if(!reconciling.Add(a)) return;
                try
                {
                    List<Order> all;
                    lock(a.Orders) all=a.Orders.ToList();
                    foreach(var instrumentGroup in all.Where(o=>OwnedBracket(o) || reductions.ContainsKey(o)).GroupBy(o=>o.Instrument))
                    {
                        Position pos;
                        lock(a.Positions) pos=a.Positions.FirstOrDefault(p=>p.Instrument==instrumentGroup.Key && p.MarketPosition!=MarketPosition.Flat);
                        var remaining=pos==null ? 0 : pos.Quantity;
                        var isLong=pos!=null && pos.MarketPosition==MarketPosition.Long;
                        var cancel=new List<Order>(); var change=new List<Order>();
                        // A protective stop and a profit-taking exit cover the SAME exposure.
                        // Bound each family independently; neither consumes the other's coverage.
                        var reductionCapacity=remaining;var stopCapacity=remaining;
                        foreach(var pending in instrumentGroup.Where(o=>reductions.ContainsKey(o) && Active(o)))
                        {
                            Reduction r;
                            if(!reductions.TryGetValue(pending,out r)) continue;
                            var stop=pending.OrderType==OrderType.StopMarket || pending.OrderType==OrderType.StopLimit;
                            var keep=r.Long!=isLong ? 0 : Math.Min(stop?stopCapacity:reductionCapacity,Math.Max(0,pending.Quantity-pending.Filled));
                            if(stop) stopCapacity-=keep;else reductionCapacity-=keep;
                            if(keep==0) {cancel.Add(pending); continue;}
                            var wanted=pending.Filled+keep;
                            if(wanted!=pending.Quantity && !changingQuantity.ContainsKey(pending))
                            {changingQuantity[pending]=wanted; pending.QuantityChanged=wanted; change.Add(pending);}
                        }
                        // An OCO pair consumes ONE allocation, not TP + SL quantities separately.
                        foreach(var pair in instrumentGroup.Where(o=>OwnedBracket(o) && Active(o))
                            .GroupBy(o=>string.IsNullOrEmpty(o.Oco) ? (object)o : o.Oco)
                            .OrderBy(g=>g.Select(o=>o.Name).OrderBy(n=>n).First()))
                        {
                            var members=pair.ToList();
                            var closed=!string.IsNullOrEmpty(members[0].Oco) && all.Any(o=>o.Oco==members[0].Oco && o.OrderState==OrderState.Filled);
                            var rightSide=members.All(o=>isLong ? o.OrderAction==OrderAction.Sell : o.OrderAction==OrderAction.BuyToCover);
                            var available=members.Min(o=>Math.Max(0,o.Quantity-o.Filled));
                            var keep=closed || !rightSide ? 0 : Math.Min(remaining,available);
                            remaining-=keep;
                            foreach(var leg in members)
                            {
                                if(keep==0) { cancel.Add(leg); continue; }
                                var wanted=leg.Filled+keep;
                                if(wanted==leg.Quantity || changingQuantity.ContainsKey(leg)) continue;
                                changingQuantity[leg]=wanted; leg.QuantityChanged=wanted; change.Add(leg);
                            }
                        }
                        if(cancel.Count>0) a.Cancel(cancel.Where(o=>!o.OrderState.ToString().StartsWith("Cancel")).Distinct().ToArray());
                        if(change.Count>0) a.Change(change);
                    }
                }
                finally { lock(reconciling) reconciling.Remove(a); }
                FlushProtection(a);
            }

            private sealed class ProtectionSlice
            { public int Qty,Number; public double Target,Stop; public string Oco; }

            private void FlushProtection(Account a)
            {
                lock(flushingProtection) if(!flushingProtection.Add(a)) return;
                try {foreach(var batch in pendingProtection.Keys.Where(b=>b.Owner==a).ToList()) SubmitProtection(batch);}
                finally {lock(flushingProtection) flushingProtection.Remove(a);}
            }

            private void SubmitProtection(ProtectionBatch batch)
            {
                var a=batch.Owner; var inst=batch.Instrument; var isLong=batch.Long; var slices=batch.Slices;
                Position pos;
                lock(a.Positions) pos=a.Positions.FirstOrDefault(p=>p.Instrument==inst && p.MarketPosition!=MarketPosition.Flat);
                long epoch; positionEpochs.TryGetValue(Tuple.Create(a,inst),out epoch);
                if(pos==null || (pos.MarketPosition==MarketPosition.Long)!=isLong || epoch!=batch.Epoch)
                {byte ignored; pendingProtection.TryRemove(batch,out ignored); return;}
                List<Order> existing;
                lock(a.Orders) existing=a.Orders.Where(o=>o.Instrument==inst && Active(o) && OwnedBracket(o)).ToList();
                var withStop=slices.Any(x=>x.Stop>0);
                var used=existing.Where(o=>withStop ? o.Name.StartsWith("BDIQ SL") : o.Name.StartsWith("BDIQ TP"))
                    .Sum(o=>Math.Max(0,o.Quantity-o.Filled));
                var capacity=Math.Max(0,pos.Quantity-used);
                var legs=new List<Order>(); var action=isLong ? OrderAction.Sell : OrderAction.BuyToCover;
                foreach(var slice in slices)
                {
                    if(slice.Qty<=0) continue;
                    var qty=Math.Min(capacity,slice.Qty); if(qty<=0) break; capacity-=qty; slice.Qty-=qty;
                    var oco=slice.Target>0 && slice.Stop>0 ? "BDIQ-"+Interlocked.Increment(ref ocoSeq)+"-"+NowMs() : "";
                    if(slice.Target>0) legs.Add(a.CreateOrder(inst,action,OrderType.Limit,OrderEntry.Manual,TimeInForce.Gtc,qty,slice.Target,0,oco,"BDIQ TP"+slice.Number,Core.Globals.MaxDate,null));
                    if(slice.Stop>0) legs.Add(a.CreateOrder(inst,action,OrderType.StopMarket,OrderEntry.Manual,TimeInForce.Gtc,qty,0,slice.Stop,oco,"BDIQ SL"+slice.Number,Core.Globals.MaxDate,null));
                }
                if(slices.All(s=>s.Qty==0)) {byte ignored; pendingProtection.TryRemove(batch,out ignored);}
                if(legs.Count>0) {WatchExecutions(a); a.Submit(legs);}
            }

            private void OnAccountItemUpdate(object sender, AccountItemEventArgs e) { SendAccount(); }
            private void OnPositionUpdate(object sender, PositionEventArgs e)
            {
                var a=e.Position!=null ? e.Position.Account : sender as Account;
                if(a!=null && e.Position!=null && e.Position.MarketPosition==MarketPosition.Flat)
                    positionEpochs.AddOrUpdate(Tuple.Create(a,e.Position.Instrument),1,(key,old)=>old+1);
                if(a!=null) OnUi(()=>ReconcileProtection(a));
                SendPositions();
            }
            private void OnOrderUpdate(object sender, OrderEventArgs e)
            {
                if(e.Order!=null)
                {
                    int wanted;
                    if(changingQuantity.TryGetValue(e.Order,out wanted) && (e.Order.Quantity==wanted || !Active(e.Order) || e.Error!=ErrorCode.NoError))
                        changingQuantity.TryRemove(e.Order,out wanted);
                    if(!Active(e.Order)) { Reduction gone; reductions.TryRemove(e.Order,out gone); }
                }
                // A rejection says why, in the DOM's result bar and order log (NinjaTrader's own text).
                if (e.OrderState == OrderState.Rejected && e.Order != null)
                    Send(new Dictionary<string, object>
                    {
                        { "t", "reject" }, { "name", e.Order.Name ?? "" }, { "inst", e.Order.Instrument.FullName }, { "action", e.Order.OrderAction.ToString() },
                        { "type", e.Order.OrderType.ToString() }, { "qty", e.Order.Quantity }, { "error", e.Error.ToString() }, { "msg", e.Comment ?? "" },
                    });
                SendOrders();
                var a=e.Order!=null ? e.Order.Account : sender as Account;
                if(a!=null) OnUi(()=>ReconcileProtection(a));
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
                var left = ex.Quantity;
                var legs = new List<ProtectionSlice>();
                lock (br)
                {
                    if(!string.IsNullOrEmpty(ex.ExecutionId) && !br.Executions.Add(ex.ExecutionId)) return;
                    for (var i = 0; i < br.TpTicks.Length && left > 0; i++)
                    {
                        var take = Math.Min(left, br.Qty[i] - br.Done[i]);
                        if (take <= 0) continue;
                        br.Done[i] += take;
                        left -= take;
                        var both = br.TpTicks[i] > 0 && br.SlTicks > 0;
                        var oco = both ? "BDIQ-" + Interlocked.Increment(ref ocoSeq) + "-" + NowMs() : "";
                        legs.Add(new ProtectionSlice {Qty=take,Number=br.Numbers[i],Oco=oco,
                            Target=br.TpTicks[i]>0 ? mi.RoundToTickSize(ex.Price+(isLong?1:-1)*br.TpTicks[i]*mi.TickSize) : 0,
                            Stop=br.SlTicks>0 ? mi.RoundToTickSize(ex.Price-(isLong?1:-1)*br.SlTicks*mi.TickSize) : 0});
                    }
                    if (br.Done.Sum() >= br.Qty.Sum()) { Bracket gone; brackets.TryRemove(ex.Order, out gone); }
                }
                if (legs.Count > 0)
                {
                    long epoch; positionEpochs.TryGetValue(Tuple.Create(a,inst),out epoch);
                    var batch=new ProtectionBatch {Owner=a,Instrument=inst,Long=isLong,Epoch=epoch,Slices=legs};
                    pendingProtection[batch]=0;
                    OnUi(() => {FlushProtection(a); ReleaseExecutions(a);});
                }
                else ReleaseExecutions(a);
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
