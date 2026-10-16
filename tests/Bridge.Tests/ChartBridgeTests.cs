using System.Web.Script.Serialization;
using NinjaTrader.NinjaScript.AddOns;
using Xunit;
using NinjaTrader.Data;

public class ChartBridgeTests
{
    [Fact] public void New_bar_only_update_finalizes_previous_forming_bar_once()
    {
        var history=new Bars();history.Rows.Add(new Bars.Row {Time=new DateTime(2026,10,16,8,30,30),Open=30000,High=30010,Low=29990,Close=30000,Volume=10});
        BarsRequest.NextBars=history;var output=new List<object>();var json=new JavaScriptSerializer();
        using(var session=new BookDepthIQChartBridge.ChartSession(output.Add))
        {
            session.Handle("{\"t\":\"subscribe\",\"slot\":2,\"generation\":1,\"instrument\":\"NQ 12-26\",\"frame\":-30,\"count\":1000}");
            var live=new BarsSeries();live.Rows.Add(new Bars.Row {Time=history.Rows[0].Time,Open=30000,High=30010,Low=29950,Close=29960,Volume=20});live.Rows.Add(new Bars.Row {Time=new DateTime(2026,10,16,8,31,0),Open=30000,High=30000,Low=29900,Close=29910,Volume=15});
            output.Clear();BarsRequest.LastCreated.Raise(1,1,live);
            var packet=json.Deserialize<Dictionary<string,object>>(json.Serialize(Assert.Single(output)));
            var rows=(System.Collections.ArrayList)packet["bars"];
            Assert.Equal(2,rows.Count);var previous=(Dictionary<string,object>)rows[0];var current=(Dictionary<string,object>)rows[1];
            Assert.Equal(0,Convert.ToInt32(previous["index"]));Assert.False(Convert.ToBoolean(previous["forming"]));Assert.True(Convert.ToBoolean(current["forming"]));
            Assert.Equal(119800,Convert.ToInt64(previous["low"]));Assert.Equal(20,Convert.ToInt64(previous["volume"]));
            output.Clear();BarsRequest.LastCreated.Raise(1,1,live);
            packet=json.Deserialize<Dictionary<string,object>>(json.Serialize(Assert.Single(output)));Assert.Single((System.Collections.ArrayList)packet["bars"]);
        }
    }
    [Fact] public void Repeated_new_bar_callbacks_leave_only_the_latest_bar_forming()
    {
        var history=new Bars();history.Rows.Add(new Bars.Row {Time=new DateTime(2026,10,16,8,30,30),Open=30000,High=30000,Low=30000,Close=30000,Volume=10});
        BarsRequest.NextBars=history;var output=new List<object>();var json=new JavaScriptSerializer();
        using(var session=new BookDepthIQChartBridge.ChartSession(output.Add))
        {
            session.Handle("{\"t\":\"subscribe\",\"slot\":2,\"generation\":1,\"instrument\":\"NQ 12-26\",\"frame\":-30,\"count\":1000}");
            var live=new BarsSeries();live.Rows.Add(history.Rows[0]);var cached=new Dictionary<int,bool>();
            for(int i=1;i<=12;i++)
            {
                live.Rows.Add(new Bars.Row {Time=history.Rows[0].Time.AddSeconds(30*i),Open=30000-i*65,High=30001-i*65,Low=29999-i*65,Close=30000-i*65,Volume=20});
                output.Clear();BarsRequest.LastCreated.Raise(i,i,live);
                var packet=json.Deserialize<Dictionary<string,object>>(json.Serialize(Assert.Single(output)));
                foreach(Dictionary<string,object> row in (System.Collections.ArrayList)packet["bars"]) cached[Convert.ToInt32(row["index"])]=Convert.ToBoolean(row["forming"]);
                Assert.Equal(i+1,cached.Count);Assert.True(cached[i]);Assert.All(cached.Where(p=>p.Key<i),p=>Assert.False(p.Value));
            }
        }
    }
    [Theory] [InlineData(0,3)] [InlineData(1,15)] [InlineData(2,-30)]
    public void Live_update_reads_event_series_not_frozen_history(int slot,int frame)
    {
        var old=new Bars();old.Rows.Add(new Bars.Row {Time=new DateTime(2026,10,15,18,0,0),Open=30200,High=30201,Low=30199,Close=30200,Volume=10});
        BarsRequest.NextBars=old;var output=new List<object>();
        using(var session=new BookDepthIQChartBridge.ChartSession(output.Add))
        {
            session.Handle("{\"t\":\"subscribe\",\"slot\":"+slot+",\"generation\":1,\"instrument\":\"NQ 12-26\",\"frame\":"+frame+",\"count\":1000}");
            var live=new BarsSeries();live.Rows.Add(old.Rows[0]);
            live.Rows.Add(new Bars.Row {Time=new DateTime(2026,10,16,7,9,0),Open=30110,High=30112,Low=30109,Close=30111,Volume=99});
            output.Clear();BarsRequest.LastCreated.Raise(1,1,live);
            var json=new JavaScriptSerializer();var packet=json.Deserialize<Dictionary<string,object>>(json.Serialize(Assert.Single(output)));
            var row=((System.Collections.ArrayList)packet["bars"]).Cast<Dictionary<string,object>>().Single(r=>Convert.ToInt32(r["index"])==1);
            Assert.Equal(120444,Convert.ToInt64(row["close"]));Assert.Equal(99,Convert.ToInt64(row["volume"]));
            var utc=TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026,10,16,7,9,0),NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo);
            Assert.Equal(new DateTimeOffset(utc).ToUnixTimeMilliseconds(),Convert.ToInt64(row["ms"]));Assert.True(Convert.ToBoolean(row["forming"]));
        }
    }
    [Fact] public void Native_history_is_chunked_and_late_updates_after_unsubscribe_are_ignored()
    {
        var bars=new Bars();for(int i=0;i<600;i++) bars.Rows.Add(new Bars.Row {Time=new DateTime(2026,10,15,13,30,0).AddMinutes(i),Open=100,High=101,Low=99,Close=100.5,Volume=17});
        BarsRequest.NextBars=bars;var output=new List<object>();
        using(var session=new BookDepthIQChartBridge.ChartSession(output.Add))
        {
            session.Handle("{\"t\":\"subscribe\",\"slot\":0,\"generation\":1,\"instrument\":\"NQ\",\"frame\":3,\"count\":1000}");
            var json=new JavaScriptSerializer();var packets=output.Select(o=>json.Deserialize<Dictionary<string,object>>(json.Serialize(o))).ToArray();
            Assert.Equal(3,packets.Count(p=>Convert.ToString(p["t"])=="bars"));
            var first=(System.Collections.ArrayList)packets[0]["bars"];Assert.Equal(256,first.Count);
            var row=(Dictionary<string,object>)first[0];Assert.Equal(400,Convert.ToInt32(row["open"]));Assert.Equal(17,Convert.ToInt32(row["volume"]));
            var request=BarsRequest.LastCreated;output.Clear();session.Handle("{\"t\":\"unsubscribe\",\"slot\":0}");
            request.Raise(0,599);Assert.Empty(output);
        }
    }
    [Fact] public void Chart_channel_rejects_orders_and_disposes_replaced_requests()
    {
        var output=new List<object>();
        using(var session=new BookDepthIQChartBridge.ChartSession(output.Add))
        {
            session.Handle("{\"t\":\"submit\"}");
            Assert.Contains("unsupported",new JavaScriptSerializer().Serialize(output));
            output.Clear();
            session.Handle("{\"t\":\"subscribe\",\"slot\":0,\"generation\":1,\"instrument\":\"NQ 12-26\",\"frame\":3,\"count\":2000}");
            Assert.Equal(1,session.SubscriptionCount);
            session.Handle("{\"t\":\"unsubscribe\",\"slot\":0}");
            Assert.Equal(0,session.SubscriptionCount);
        }
    }
    [Theory] [InlineData(10)] [InlineData(0)] [InlineData(-1)]
    public void Unsupported_frames_never_create_subscriptions(int frame)
    {
        using(var session=new BookDepthIQChartBridge.ChartSession(_=>{}))
        {session.Handle("{\"t\":\"subscribe\",\"slot\":0,\"generation\":1,\"instrument\":\"NQ\",\"frame\":"+frame+",\"count\":500}"); Assert.Equal(0,session.SubscriptionCount);}
    }
}
