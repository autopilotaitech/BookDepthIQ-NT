// Copyright (c) 2026 autopilotaitech. MIT. Read-only chart channel: no account/order APIs.
using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;

namespace NinjaTrader.NinjaScript.AddOns
{
    public class BookDepthIQChartBridge : AddOnBase
    {
        public const int Port=36972;
        static readonly object Gate=new object();
        static BookDepthIQChartBridge owner;
        static TcpListener listener;
        static TcpClient client;
        const string StopSlot="BookDepthIQ.Chart.Stop";
        protected override void OnStateChange()
        {
            if(State==State.SetDefaults) {Name="BookDepthIQChartBridge"; Description="Read-only BookDepthIQ candle history and live bars";}
            else if(State==State.Active || State==State.Configure)
            {
                lock(Gate)
                {
                    if(listener!=null) return;
                    var previous=AppDomain.CurrentDomain.GetData(StopSlot) as Action; if(previous!=null) previous();
                    var l=new TcpListener(IPAddress.Loopback,Port); l.Start(); listener=l; owner=this;
                    AppDomain.CurrentDomain.SetData(StopSlot,(Action)Stop);
                    Task.Run(()=>Accept(l));
                }
            }
            else if(State==State.Terminated && owner==this) Stop();
        }
        static void Stop() {lock(Gate) {if(client!=null) client.Close(); if(listener!=null) listener.Stop(); client=null;listener=null;owner=null;}}
        static void Accept(TcpListener l)
        {
            try {while(true) {using(var c=l.AcceptTcpClient()) {lock(Gate) client=c; Serve(c); lock(Gate) if(client==c) client=null;}}}
            catch(SocketException) {} catch(ObjectDisposedException) {}
        }
        static void Serve(TcpClient c)
        {
            c.NoDelay=true;
            using(var queue=new BlockingCollection<object>(4096))
            using(var stream=c.GetStream())
            using(var session=new ChartSession(message=> {if(!queue.TryAdd(message)) c.Close();}))
            {
                var write=Task.Run(()=> {
                    try {using(var writer=new StreamWriter(stream,new UTF8Encoding(false),4096,true) {AutoFlush=true})
                    {var json=new JavaScriptSerializer(); foreach(var item in queue.GetConsumingEnumerable()) writer.WriteLine(json.Serialize(item));}}
                    catch(IOException) {c.Close();} catch(ObjectDisposedException) {}
                });
                try
                {
                    session.Hello();
                    using(var reader=new StreamReader(stream,Encoding.UTF8,false,4096,true))
                    {string line; while((line=reader.ReadLine())!=null) {if(line.Length>8192) break; session.Handle(line);}}
                }
                catch(IOException) {} catch(ObjectDisposedException) {}
                finally {session.Dispose();queue.CompleteAdding();c.Close();try {write.Wait();} catch(AggregateException) {}}
            }
        }

        public sealed class ChartSession : IDisposable
        {
            readonly Action<object> output;
            readonly object gate=new object();
            readonly Dictionary<int,Subscription> subscriptions=new Dictionary<int,Subscription>();
            volatile bool disposed;
            public ChartSession(Action<object> output) {this.output=output;}
            public int SubscriptionCount {get {lock(gate) return subscriptions.Count;}}
            public void Hello() {output(new {t="hello",chartVersion=1,timezone=Core.Globals.GeneralOptions.TimeZoneInfo.Id});}
            public void Handle(string line)
            {
                int slot=-1; long generation=0;
                try
                {
                    var m=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(line);
                    var t=Convert.ToString(m["t"]);
                    if(t!="subscribe" && t!="unsubscribe") throw new ArgumentException("unsupported chart command");
                    slot=Convert.ToInt32(m["slot"]); if(slot<0 || slot>2) throw new ArgumentException("invalid chart slot");
                    lock(gate)
                    {
                        if(disposed) return;
                        Subscription old;
                        if(subscriptions.TryGetValue(slot,out old)) {subscriptions.Remove(slot); old.Dispose();}
                        if(t=="unsubscribe") return;
                        generation=Convert.ToInt64(m["generation"]);
                        var frame=Convert.ToInt32(m["frame"]); var count=Convert.ToInt32(m["count"]);
                        if(!(new[]{1,3,5,15,30,60,240,1440}).Contains(frame) && !(slot==2 && frame==-30)) throw new ArgumentException("unsupported chart frame");
                        if(count<250 || count>(slot==2?75000:10000)) throw new ArgumentException("invalid chart lookback");
                        var name=Convert.ToString(m["instrument"]);
                        if(string.IsNullOrWhiteSpace(name)||name.Length>100) throw new ArgumentException("invalid instrument");
                        var inst=Instrument.GetInstrument(name,false); if(inst==null) throw new ArgumentException("instrument unavailable");
                        var sub=new Subscription(this,slot,generation,frame,count,inst);
                        subscriptions[slot]=sub; sub.Start();
                    }
                }
                catch(Exception e) {Emit(new {t="error",slot=slot,generation=generation,message=e.Message});}
            }
            void Emit(object message) {if(!disposed) {try {output(message);} catch(InvalidOperationException) {}}}
            public void Dispose() {lock(gate) {if(disposed) return;disposed=true;foreach(var s in subscriptions.Values) s.Dispose();subscriptions.Clear();}}
            sealed class Subscription : IDisposable
            {
                readonly ChartSession session; readonly int slot,frame,count; readonly long generation;
                readonly Instrument instrument; readonly BarsRequest request;
                MarketData market;
                volatile bool stopped; long revision,sequence;
                public Subscription(ChartSession session,int slot,long generation,int frame,int count,Instrument inst)
                {
                    this.session=session;this.slot=slot;this.generation=generation;this.frame=frame;this.count=count;instrument=inst;
                    request=slot==2?new BarsRequest(inst,DateTime.Now.AddDays(-28),DateTime.Now):new BarsRequest(inst,count);
                    request.BarsPeriod=new BarsPeriod {BarsPeriodType=frame==-30?BarsPeriodType.Second:frame==1440?BarsPeriodType.Day:BarsPeriodType.Minute,Value=frame==-30?30:frame==1440?1:frame};
                    request.TradingHours=inst.MasterInstrument.TradingHours;
                }
                public void Start()
                {
                    request.Update+=Update;
                    request.Request((r,error,message)=> {
                        if(stopped) return;
                        if(error!=ErrorCode.NoError || r.Bars==null) {session.Emit(new {t="error",slot=slot,generation=generation,message=message??error.ToString()});return;}
                        Send(0,r.Bars.Count-1,true);
                    });
                    if(slot!=2) instrument.Dispatcher.InvokeAsync(()=> {if(stopped) return;market=new MarketData(instrument);market.Update+=Last;});
                }
                void Update(object sender,BarsUpdateEventArgs e) {if(!stopped) Send(e.MinIndex,e.MaxIndex,false);}
                long Ticks(double price) {return (long)Math.Round(price/instrument.MasterInstrument.TickSize);}
                void Send(int min,int max,bool complete)
                {
                    var bars=request.Bars; if(stopped || bars==null) return;
                    var version=Interlocked.Increment(ref revision);
                    for(var first=Math.Max(Math.Max(0,min),bars.Count-count);first<=Math.Min(max,bars.Count-1);first+=256)
                    {
                        var items=new List<object>();
                        for(var i=first;i<=Math.Min(Math.Min(max,bars.Count-1),first+255);i++)
                        {
                            var time=DateTime.SpecifyKind(bars.GetTime(i),DateTimeKind.Unspecified);
                            var utc=TimeZoneInfo.ConvertTimeToUtc(time,Core.Globals.GeneralOptions.TimeZoneInfo);
                            items.Add(new {index=i,ms=new DateTimeOffset(utc).ToUnixTimeMilliseconds(),date=time.ToString("yyyy-MM-dd"),open=Ticks(bars.GetOpen(i)),high=Ticks(bars.GetHigh(i)),low=Ticks(bars.GetLow(i)),close=Ticks(bars.GetClose(i)),volume=bars.GetVolume(i),revision=version,forming=i==bars.Count-1});
                        }
                        if(stopped) return;
                        session.Emit(new {t="bars",slot=slot,generation=generation,instrument=instrument.FullName,frame=frame,tickSize=instrument.MasterInstrument.TickSize,bars=items});
                    }
                    if(complete && !stopped) session.Emit(new {t="complete",slot=slot,generation=generation});
                }
                void Last(object sender,MarketDataEventArgs e)
                {
                    if(stopped || e.MarketDataType!=MarketDataType.Last) return;
                    var utc=TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(e.Time,DateTimeKind.Unspecified),Core.Globals.GeneralOptions.TimeZoneInfo);
                    session.Emit(new {t="last",slot=slot,generation=generation,price=Ticks(e.Price),volume=e.Volume,ms=new DateTimeOffset(utc).ToUnixTimeMilliseconds(),received=Stopwatch.GetTimestamp(),sequence=Interlocked.Increment(ref sequence)});
                }
                public void Dispose()
                {
                    stopped=true;request.Update-=Update;request.Dispose();
                    instrument.Dispatcher.InvokeAsync(()=> {if(market!=null) {market.Update-=Last;market=null;}});
                }
            }
        }
    }
}
