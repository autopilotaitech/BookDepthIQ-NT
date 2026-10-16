using System.Reflection;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.AddOns;
using Xunit;
public class ChartTradePacketTests
{
    [Fact] public void Chart_timestamp_uses_configured_zone_and_preserves_legacy_timestamp()
    {
        var type=typeof(BookDepthIQBridge).GetNestedType("BridgeServer",BindingFlags.NonPublic);
        var method=type.GetMethod("LiveTradePacket",BindingFlags.Static|BindingFlags.NonPublic);Assert.NotNull(method);
        var saved=NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo;
        try
        {
            NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo=TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            var time=new DateTime(2026,10,15,10,30,0,DateTimeKind.Unspecified);
            var packet=(Dictionary<string,object>)method.Invoke(null,new object[]{new MarketDataEventArgs {Instrument=Instrument.GetInstrument("NQ TEST",false),Time=time,Price=30000,Volume=85},"b"});
            var expected=(long)(new DateTime(2026,10,15,14,30,0,DateTimeKind.Utc)-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds;
            Assert.Equal(expected,packet["chartMs"]);Assert.Equal("NQ TEST",packet["inst"]);
            Assert.Equal((long)(time.ToUniversalTime()-new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc)).TotalMilliseconds,packet["ms"]);
        }
        finally {NinjaTrader.Core.Globals.GeneralOptions.TimeZoneInfo=saved;}
    }
}
