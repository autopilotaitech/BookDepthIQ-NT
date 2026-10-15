using BookDepthIQ.Core.Charts;
using Xunit;

public class ChartModelTests
{
    static ChartBar Bar(int i, long revision=1) => new(i, DateTimeOffset.UnixEpoch.AddMinutes(i), new DateOnly(2026,10,15),100,104,98,102,20,revision,true);
    [Fact] public void Store_rejects_stale_generation_and_revision_and_bounds_history()
    {
        var store=new ChartStore(10000); store.Reset(7);
        for(int i=10000;i>=0;i--) store.Apply(7,Bar(i));
        Assert.Equal(10000,store.Snapshot().Count); Assert.Equal(1,store.Snapshot()[0].Index);
        store.Apply(6,Bar(10002)); store.Apply(7,Bar(10000,0) with {Close=99});
        Assert.Equal(102,store.Snapshot()[^1].Close);
        store.Apply(7,Bar(10000,2) with {Close=103}); Assert.Equal(103,store.Snapshot()[^1].Close);
        store.Reset(8); Assert.Empty(store.Snapshot());
    }
    [Fact] public void Invalid_prices_are_not_cached()
    {
        var store=new ChartStore(250); store.Reset(1);
        Assert.False(store.Apply(1,Bar(0) with {High=90})); Assert.Empty(store.Snapshot());
    }
    [Theory] [InlineData(1,3000,true)] [InlineData(3,2000,true)] [InlineData(5,2000,true)]
    [InlineData(15,1500,true)] [InlineData(30,1500,true)] [InlineData(60,1500,false)]
    [InlineData(240,1000,false)] [InlineData(1440,500,false)]
    public void Frames_have_independent_history_and_intraday_overlay_rules(int minutes,int count,bool pax)
    { Assert.Equal(count,ChartFrame.DefaultBars(minutes)); Assert.Equal(pax,ChartFrame.SupportsPax(minutes)); }
    [Fact] public void Viewport_pan_preserves_autoscale_and_zoom_keeps_pointer_anchor()
    {
        var v=new ChartViewport(); v.Reset(500); var anchor=v.Start+v.Count*.3;
        v.Zoom(.5,.3,500); Assert.Equal(anchor,v.Start+v.Count*.3,6);
        v.Pan(-10,500); Assert.False(v.Follow); Assert.True(v.Auto);
        v.SetPriceRange(90,110); Assert.False(v.Auto); v.Reset(500); Assert.True(v.Auto); Assert.True(v.Follow);
        var span=v.PriceRange(new[]{Bar(1) with {Open=-10,High=-10,Low=-10,Close=-10}});
        Assert.True(span.High-span.Low>=4);
    }
}
