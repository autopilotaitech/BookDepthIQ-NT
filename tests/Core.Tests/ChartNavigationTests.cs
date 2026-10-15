using BookDepthIQ.Core.Charts;
using Xunit;

public class ChartNavigationTests
{
    static ChartBar Bar(long low,long high)=>new(0,DateTimeOffset.UnixEpoch,new(2026,10,15),low,high,low,high,1,1,true);
    [Fact] public void Recenter_restores_live_center_without_resetting_time_zoom()
    {
        var v=new ChartViewport();v.Reset(500);v.Zoom(.5,.5,500);v.SetPriceRange(50,80);v.Pan(-30,500);
        v.Recenter(500);Assert.Equal(60,v.Count);Assert.True(v.Follow);Assert.True(v.Auto);Assert.True(v.CenterOnLast);
        var range=v.PriceRange(new[]{Bar(100,140)},135);Assert.Equal(135,(range.Low+range.High)/2,6);Assert.True(range.Low<=100&&range.High>=140);
        v.Pan(-10,500);Assert.False(v.CenterOnLast);Assert.False(v.Follow);Assert.True(v.Auto);
    }
    [Fact] public void Manual_price_scaling_stays_manual_until_auto_or_center_is_requested()
    {
        var v=new ChartViewport();v.Reset(500);v.Recenter(500);v.SetPriceRange(80,120);v.Update(501);
        Assert.False(v.Auto);Assert.False(v.CenterOnLast);Assert.Equal((80d,120d),v.PriceRange(new[]{Bar(500,600)},550));
        v.AutoScale();Assert.True(v.Auto);Assert.False(v.CenterOnLast);
    }
    [Theory] [InlineData(.25,30123.25,"30123.25")] [InlineData(1,45001,"45001")]
    [InlineData(.00005,1.08435,"1.08435")] [InlineData(.01,-12.34,"-12.34")]
    public void Axis_price_format_respects_instrument_tick_precision(double tick,double price,string expected)
    {Assert.Equal(expected,ChartAxis.Format(price,tick));}
    [Theory] [InlineData(.25,119970,120120)] [InlineData(.00005,20000,20100)] [InlineData(.01,-1000,-100)]
    public void Axis_labels_are_bounded_and_aligned_to_native_ticks(double tick,double low,double high)
    {
        var values=ChartAxis.PriceTicks(low,high,tick,400);Assert.InRange(values.Count,2,16);
        Assert.All(values,t=>Assert.InRange((double)t,low,high));Assert.Equal(values.Distinct().Count(),values.Count);
    }
    [Fact] public void Geometry_snaps_pointer_to_candle_and_tick_and_rejects_axes_and_future_padding()
    {
        var plot=new ChartGeometry(600,420,50,100,100,200);
        Assert.Equal(52,plot.BarAt(plot.X(52),200));Assert.Equal(-1,plot.BarAt(plot.PlotWidth+3,200));
        Assert.Equal(150,plot.TicksAt(plot.Y(150)));Assert.Equal(150,plot.TicksAt(plot.Y(150.3)));
        Assert.Equal(-1,new ChartGeometry(600,420,150,100,100,200).BarAt(500,180));
    }
}
