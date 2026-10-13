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
        private const int Levels = 40;
        private const int BookMs = 50;

        // One bridge per NinjaTrader process, whatever number of AddOn instances NT creates.
        private static readonly object Gate = new object();
        private static BridgeServer server;

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
                        server = new BridgeServer();
                        server.Start();
                    }
                }
            }
            else if (State == State.Terminated)
            {
                // NT terminates AddOn clones often; the server lives until NinjaTrader exits.
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

            // Entries sent with a bracket: order → (tp ticks, sl ticks).
            private readonly ConcurrentDictionary<Order, int[]> brackets = new ConcurrentDictionary<Order, int[]>();
            private int ocoSeq;

            public void Start()
            {
                listener = new TcpListener(IPAddress.Loopback, Port);
                listener.Start();
                new Thread(AcceptLoop) { IsBackground = true, Name = "BDIQ bridge accept" }.Start();
                new Thread(WriteLoop) { IsBackground = true, Name = "BDIQ bridge write" }.Start();
                bookTimer = new Timer(_ => FlushBook(), null, BookMs, BookMs);
                Log("BookDepthIQ bridge listening on 127.0.0.1:" + Port);
            }

            // ───────────── socket ─────────────

            private void AcceptLoop()
            {
                while (true)
                {
                    TcpClient c;
                    try { c = listener.AcceptTcpClient(); }
                    catch (Exception e) { Log("bridge accept stopped: " + e.Message); return; }
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

            private void Send(Dictionary<string, object> m)
            {
                bool connected;
                lock (clientGate) connected = client != null;
                if (connected) outbox.TryAdd(json.Serialize(m));
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
                Send(new Dictionary<string, object> { { "t", "hello" }, { "v", 1 }, { "accounts", accts } });
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
                var tp = Int(m, "tp");
                var sl = Int(m, "sl");
                if (tp > 0 || sl > 0) brackets[o] = new[] { tp, sl };
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
            private void OnOrderUpdate(object sender, OrderEventArgs e) { SendOrders(); }

            private void OnExecutionUpdate(object sender, ExecutionEventArgs e)
            {
                var ex = e.Execution;
                if (ex == null || ex.Order == null) return;
                int[] br;
                if (!brackets.TryGetValue(ex.Order, out br)) return;
                if (ex.Order.OrderState == OrderState.Filled) { int[] gone; brackets.TryRemove(ex.Order, out gone); }
                // Bracket for the quantity this execution filled, priced in ticks from its fill.
                var a = ex.Account;
                var inst = ex.Instrument;
                var mi = inst.MasterInstrument;
                var isLong = ex.Order.OrderAction == OrderAction.Buy || ex.Order.OrderAction == OrderAction.BuyToCover;
                var exitAction = isLong ? OrderAction.Sell : OrderAction.BuyToCover;
                var oco = "BDIQ-" + Interlocked.Increment(ref ocoSeq) + "-" + NowMs();
                var legs = new List<Order>();
                if (br[0] > 0)
                {
                    var tpPx = mi.RoundToTickSize(ex.Price + (isLong ? 1 : -1) * br[0] * mi.TickSize);
                    legs.Add(a.CreateOrder(inst, exitAction, OrderType.Limit, OrderEntry.Manual, TimeInForce.Gtc, ex.Quantity, tpPx, 0, br[1] > 0 ? oco : "", "BDIQ target", Core.Globals.MaxDate, null));
                }
                if (br[1] > 0)
                {
                    var slPx = mi.RoundToTickSize(ex.Price - (isLong ? 1 : -1) * br[1] * mi.TickSize);
                    legs.Add(a.CreateOrder(inst, exitAction, OrderType.StopMarket, OrderEntry.Manual, TimeInForce.Gtc, ex.Quantity, 0, slPx, br[0] > 0 ? oco : "", "BDIQ stop", Core.Globals.MaxDate, null));
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
