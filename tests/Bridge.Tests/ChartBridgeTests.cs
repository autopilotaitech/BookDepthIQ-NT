using System.Web.Script.Serialization;
using NinjaTrader.NinjaScript.AddOns;
using Xunit;
using NinjaTrader.Data;

public class ChartBridgeTests
{
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
