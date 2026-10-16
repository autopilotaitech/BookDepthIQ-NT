using System.Text.Json;
using BookDepthIQ.Dom.Charts;
using Xunit;

public class ChartFreshnessTests
{
    [Fact] public void Live_ticks_cannot_make_old_candles_live_and_future_bars_are_flagged()
    {
        var now=new DateTimeOffset(2026,10,16,12,7,0,TimeSpan.Zero);
        var model=new ChartCoordinator();var generation=model.Reset(0,3,250);
        void Bar(DateTimeOffset time,int revision)=>model.Apply(JsonSerializer.Serialize(new {t="bars",slot=0,generation,tickSize=.25,bars=new[]{new {index=0,ms=time.ToUnixTimeMilliseconds(),date="2026-10-16",open=100,high=102,low=99,close=101,volume=1,revision,forming=true}}}));
        Bar(now.AddHours(-12),1);model.Apply(JsonSerializer.Serialize(new {t="complete",slot=0,generation}));
        model.Apply(JsonSerializer.Serialize(new {t="last",slot=0,generation,sequence=1,price=120440}));
        Assert.Contains("STALE",model.Snapshot(0,now).Status);
        Bar(now.AddHours(5),2);Assert.Contains("TIME",model.Snapshot(0,now).Status);
        Bar(now.AddMinutes(2),3);Assert.Equal("LIVE",model.Snapshot(0,now).Status);
    }
}
