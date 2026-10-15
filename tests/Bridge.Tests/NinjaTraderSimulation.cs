// Test-only API doubles. Never references NT DLLs or starts the bridge listener.
// The production bridge source and REAL .NET Framework JavaScriptSerializer are linked into this project.
using System.Collections.Generic;
using System.Linq;

namespace System.Windows
{
    public class Application { public static Application Current {get;set;} public TestDispatcher Dispatcher = new TestDispatcher(); }
    public class TestDispatcher
    {
        public bool Deferred;
        readonly Queue<Action> queue=new Queue<Action>();
        public void InvokeAsync(Action action) {if(Deferred) queue.Enqueue(action); else action();}
        public void Drain() {while(queue.Count>0) queue.Dequeue()();}
    }
}
namespace NinjaTrader.Core { public static class Globals { public static DateTime MaxDate = DateTime.MaxValue; public static GeneralOptions GeneralOptions=new GeneralOptions(); } public class GeneralOptions {public TimeZoneInfo TimeZoneInfo=TimeZoneInfo.Utc;} }
namespace NinjaTrader.NinjaScript
{
    public enum State { SetDefaults, Active, Configure, Terminated }
    public class AddOnBase { public State State; public string Name, Description; protected virtual void OnStateChange() {} }
    public static class NinjaScript { public static void Log(string text,NinjaTrader.Cbi.LogLevel level) {} }
}
namespace NinjaTrader.Cbi
{
    public enum LogLevel { Information }
    public enum MarketPosition { Flat, Long, Short }
    public enum OrderAction { Buy, Sell, BuyToCover, SellShort }
    public enum OrderType { Market, Limit, StopMarket, MIT, StopLimit }
    public enum OrderEntry { Manual }
    public enum TimeInForce { Day, Gtc }
    public enum OrderState { Working, Accepted, Submitted, TriggerPending, PartFilled, ChangePending, ChangeSubmitted, Initialized, CancelPending, Rejected, Filled, Cancelled }
    public enum AccountItem { CashValue, RealizedProfitLoss, UnrealizedProfitLoss, NetLiquidation, TrailingMaxDrawdown, DailyLossLimit, Commission }
    public enum Currency { UsDollar }
    public enum ErrorCode { NoError }
    public class MasterInstrument
    {
        public double TickSize=.25; public double PointValue=2; public string Description="SIM"; public object TradingHours;
        public double RoundToTickSize(double p) => Math.Round(p/TickSize)*TickSize;
    }
    public class Instrument
    {
        static readonly Dictionary<string,Instrument> items=new Dictionary<string,Instrument>();
        public string FullName; public MasterInstrument MasterInstrument=new MasterInstrument();
        public System.Windows.TestDispatcher Dispatcher=new System.Windows.TestDispatcher();
        public static Instrument GetInstrument(string name,bool ignored) { if(!items.TryGetValue(name,out var i)) items[name]=i=new Instrument {FullName=name}; return i; }
    }
    public class Position { public Account Account; public Instrument Instrument; public MarketPosition MarketPosition; public int Quantity; public double AveragePrice; }
    public class Order
    {
        public Account Account; public string OrderId=Guid.NewGuid().ToString("N"), Name,Oco; public int Id;
        public Instrument Instrument; public OrderAction OrderAction; public OrderType OrderType; public OrderState OrderState;
        public int Quantity,Filled,QuantityChanged; public double LimitPrice,StopPrice,LimitPriceChanged,StopPriceChanged;
        public TimeInForce TimeInForce;
    }
    public class Execution { public Account Account; public Instrument Instrument; public string ExecutionId; public Order Order; public int Quantity; public double Price,Commission,Fee; public DateTime Time; }
    public class ExecutionEventArgs : EventArgs { public Execution Execution; public MarketPosition MarketPosition; }
    public class PositionEventArgs : EventArgs {public Position Position;}
    public class AccountItemEventArgs : EventArgs {}
    public class OrderEventArgs : EventArgs { public Order Order; public OrderState OrderState; public ErrorCode Error; public string Comment; }
    public class Account
    {
        public static List<Account> All=new List<Account>();
        public string Name="Sim101",ConnectionStatus="Connected";
        public List<Order> Orders=new List<Order>(); public List<Position> Positions=new List<Position>();
        public event EventHandler<OrderEventArgs> OrderUpdate;
        public event EventHandler<ExecutionEventArgs> ExecutionUpdate;
        public event EventHandler<PositionEventArgs> PositionUpdate;
        public event EventHandler<AccountItemEventArgs> AccountItemUpdate { add {} remove {} }
        public double Get(AccountItem item,Currency currency) => 50000;
        public Order CreateOrder(Instrument inst,OrderAction action,OrderType type,OrderEntry entry,TimeInForce tif,int qty,double limit,double stop,string oco,string name,DateTime expiry,object custom) =>
            new Order {Account=this,Instrument=inst,OrderAction=action,OrderType=type,TimeInForce=tif,Quantity=qty,LimitPrice=limit,StopPrice=stop,Oco=oco,Name=name,OrderState=OrderState.Initialized};
        public void Submit(IEnumerable<Order> orders) { foreach(var o in orders) { Orders.Add(o); o.OrderState=OrderState.Working; Notify(o); } }
        void Notify(Order o) => OrderUpdate?.Invoke(this,new OrderEventArgs {Order=o,OrderState=o.OrderState});
        public bool DeferCancels;
        readonly List<Order> pendingCancels=new List<Order>();
        public void ConfirmCancels() {var copy=pendingCancels.ToArray(); pendingCancels.Clear(); DeferCancels=false; Cancel(copy);}
        public void Cancel(IEnumerable<Order> orders) { foreach(var o in orders.Distinct()) {
            if(DeferCancels) {pendingCancels.Add(o); o.OrderState=OrderState.CancelPending;} else o.OrderState=OrderState.Cancelled; Notify(o); } }
        public bool DeferChanges;
        readonly List<Order> pendingChanges=new List<Order>();
        public void ConfirmChanges() {var copy=pendingChanges.ToArray(); pendingChanges.Clear(); DeferChanges=false; Change(copy);}
        public void Change(IEnumerable<Order> orders)
        {
            if(DeferChanges) {foreach(var o in orders) {pendingChanges.Add(o); o.OrderState=OrderState.ChangePending; Notify(o);} return;}
            foreach(var o in orders) { if(o.QuantityChanged>0) o.Quantity=o.QuantityChanged;
                if(o.LimitPriceChanged>0) o.LimitPrice=o.LimitPriceChanged; if(o.StopPriceChanged>0) o.StopPrice=o.StopPriceChanged; Notify(o); }
        }
        public void Flatten(IEnumerable<Instrument> instruments)
        {
            foreach(var i in instruments) { CancelAllOrders(i); Positions.RemoveAll(p=>p.Instrument==i);
                PositionUpdate?.Invoke(this,new PositionEventArgs {Position=new Position {Account=this,Instrument=i,MarketPosition=MarketPosition.Flat}}); }
        }
        public void CancelAllOrders(Instrument instrument) => Cancel(Orders.Where(o=>o.Instrument==instrument && Working(o)).ToArray());
        public static bool Working(Order o) => o.OrderState!=OrderState.Cancelled && o.OrderState!=OrderState.Filled && o.OrderState!=OrderState.Rejected;
        public void Fill(Order order,int qty,double price,string id=null)
        {
            order.Filled+=qty; order.OrderState=order.Filled==order.Quantity?OrderState.Filled:OrderState.PartFilled;
            var p=Positions.FirstOrDefault(x=>x.Instrument==order.Instrument);
            var before=p==null?0:p.Quantity*(p.MarketPosition==MarketPosition.Long?1:-1);
            var signed=qty*(order.OrderAction==OrderAction.Buy || order.OrderAction==OrderAction.BuyToCover?1:-1);
            if(p==null) Positions.Add(p=new Position {Account=this,Instrument=order.Instrument});
            var after=before+signed; p.Quantity=Math.Abs(after); p.MarketPosition=after==0?MarketPosition.Flat:after>0?MarketPosition.Long:MarketPosition.Short; p.AveragePrice=price;
            Notify(order);
            ExecutionUpdate?.Invoke(this,new ExecutionEventArgs {Execution=new Execution {Account=this,Instrument=order.Instrument,ExecutionId=id??Guid.NewGuid().ToString(),Order=order,Quantity=qty,Price=price,Time=DateTime.UtcNow},MarketPosition=p.MarketPosition});
            PositionUpdate?.Invoke(this,new PositionEventArgs {Position=p});
            if(order.OrderState==OrderState.Filled && !string.IsNullOrEmpty(order.Oco)) Cancel(Orders.Where(o=>o!=order && o.Oco==order.Oco && Working(o)).ToArray());
        }
        public void Replay(Order order,int qty,double price,string id) => ExecutionUpdate?.Invoke(this,new ExecutionEventArgs
            {Execution=new Execution {Account=this,Instrument=order.Instrument,ExecutionId=id,Order=order,Quantity=qty,Price=price,Time=DateTime.UtcNow}});
    }
}
namespace NinjaTrader.Data
{
    using NinjaTrader.Cbi;
    public class MarketDepthRow { public double Price; public long Volume; }
    public class MarketDepthEventArgs : EventArgs { public Instrument Instrument; }
    public class MarketDepth<T> where T:new()
    {
        public MarketDepth(Instrument i) {} public List<T> Bids=new List<T>(),Asks=new List<T>();
        public event EventHandler<MarketDepthEventArgs> Update { add {} remove {} }
    }
    public enum MarketDataType { Bid, Ask, Last }
    public class MarketDataEventArgs : EventArgs { public Instrument Instrument; public MarketDataType MarketDataType; public double Price; public long Volume; public DateTime Time; }
    public class MarketData { public MarketData(Instrument i) {} public event EventHandler<MarketDataEventArgs> Update { add {} remove {} } }
    public enum BarsPeriodType { Tick,Minute,Second,Day }
    public class BarsPeriod { public BarsPeriodType BarsPeriodType; public int Value; }
    public class Bars
    {
        public class Row {public DateTime Time;public double Open,High,Low,Close;public long Volume;}
        public List<Row> Rows=new List<Row>();public int Count=>Rows.Count;
        public double GetOpen(int i)=>Rows[i].Open;public double GetHigh(int i)=>Rows[i].High;public double GetLow(int i)=>Rows[i].Low;public double GetClose(int i)=>Rows[i].Close;
        public double GetAsk(int i)=>0;public double GetBid(int i)=>0;public long GetVolume(int i)=>Rows[i].Volume;public DateTime GetTime(int i)=>Rows[i].Time;
    }
    public class BarsUpdateEventArgs : EventArgs {public int MinIndex,MaxIndex;}
    public class BarsRequest
    {
        [ThreadStatic] public static Bars NextBars;
        [ThreadStatic] public static BarsRequest LastCreated;
        public BarsRequest(Instrument i,DateTime from,DateTime to) {} public BarsRequest(Instrument i,int count) {Bars=NextBars??new Bars();NextBars=null;LastCreated=this;} public BarsPeriod BarsPeriod; public object TradingHours; public Bars Bars;
        public event EventHandler<BarsUpdateEventArgs> Update;
        public void Raise(int min,int max)=>Update?.Invoke(this,new BarsUpdateEventArgs {MinIndex=min,MaxIndex=max});
        public void Dispose() {}
        public void Request(Action<BarsRequest,ErrorCode,string> callback) { callback(this,ErrorCode.NoError,""); }
    }
}
