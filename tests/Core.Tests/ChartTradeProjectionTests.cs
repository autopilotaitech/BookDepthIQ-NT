using BookDepthIQ.Core.Charts;
using Xunit;
public class ChartTradeProjectionTests
{
    [Fact] public void Fifty_thousand_print_query_allocates_only_bounded_marker_candidates()
    {
        var input=Enumerable.Range(1,50000).Select(i=>Print(i,1000,i)).ToArray();var bars=new[]{Bar(0,0),Bar(1,60000)};
        ChartTradeProjection.Build(input,bars,1,0,60000,1,12,42);
        var before=GC.GetAllocatedBytesForCurrentThread();var result=ChartTradeProjection.Build(input,bars,1,0,60000,1,12,42);
        var allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        Assert.Equal(500,result.Markers.Count);Assert.True(allocated<500000,$"Projection allocated {allocated:N0} bytes for a 500-marker output");
    }
    [Fact] public void Dense_view_budget_preserves_largest_prints_without_summing_or_changing_source()
    {
        var source=new ChartBubbleProjection(Enumerable.Range(1,500).Select(i=>new ChartBubble(Print(i,1000,i),i/5d,42)).ToArray(),1);
        var result=ChartTradeProjection.LimitDensity(source,new ChartGeometry(600,420,0,120,90,110),42);
        Assert.InRange(result.Markers.Count,1,113);Assert.Contains(result.Markers,m=>m.Print.Quantity==500);
        Assert.Equal(501-result.Markers.Count,result.Omitted);Assert.Equal(500,source.Markers.Count);
        Assert.All(result.Markers,m=>Assert.Equal(m.Print.Id,m.Print.Quantity));
    }
    static ChartBar Bar(int i,long ms)=>new(i,DateTimeOffset.FromUnixTimeMilliseconds(ms),new(2026,10,15),100,110,90,100,1,1,false);
    static ChartPrint Print(long id,long ms,long qty=50)=>new(id,"NQ TEST",100,qty,'b',ms);
    [Fact] public void Threshold_size_and_native_end_boundaries_use_individual_prints()
    {
        var bars=new[]{Bar(0,0),Bar(1,60000),Bar(2,120000)};
        var result=ChartTradeProjection.Build(new[]{Print(1,1000,49),Print(2,30000),Print(3,60000,200),Print(4,60001,100000)},bars,1,0,120000,50,12,42);
        Assert.Equal(3,result.Markers.Count);Assert.Equal(new double[]{12,24,42},result.Markers.Select(m=>m.Diameter));
        Assert.InRange(result.Markers[1].BarPosition,.6,1.4);Assert.InRange(result.Markers[2].BarPosition,1.6,2.4);
    }
    [Fact] public void Missing_sessions_unproved_first_bar_and_daily_are_not_guessed()
    {
        var bars=new[]{Bar(0,0),Bar(1,60000),Bar(2,3600000)};
        Assert.Empty(ChartTradeProjection.Build(new[]{Print(1,-100),Print(2,3590000)},bars,1,-60000,3600000,50,12,42).Markers);
        Assert.Empty(ChartTradeProjection.Build(new[]{Print(1,30000)},bars,1440,0,3600000,50,12,42).Markers);
    }
    [Fact] public void Dense_cap_selects_largest_without_merging_and_is_deterministic()
    {
        var input=Enumerable.Range(1,501).Select(i=>Print(i,1000,i)).ToArray();
        var result=ChartTradeProjection.Build(input,new[]{Bar(0,0),Bar(1,60000)},1,0,60000,1,12,42);
        Assert.Equal(500,result.Markers.Count);Assert.Equal(1,result.Omitted);
        Assert.DoesNotContain(result.Markers,m=>m.Print.Id==1);Assert.Equal(501,input.Length);
        Assert.Equal(500,result.Markers.Select(m=>m.Print.Id).Distinct().Count());
    }
    [Fact] public void Utc_offsets_and_overnight_dates_use_elapsed_time_not_wall_clock()
    {
        var end=new DateTimeOffset(2026,11,1,1,30,0,TimeSpan.FromHours(-6));
        var bars=new[]{Bar(0,end.AddMinutes(-1).ToUnixTimeMilliseconds()),Bar(1,end.ToUnixTimeMilliseconds())};
        var result=ChartTradeProjection.Build(new[]{Print(1,end.AddSeconds(-30).ToUnixTimeMilliseconds())},bars,1,long.MinValue,long.MaxValue,50,12,42);
        Assert.Single(result.Markers);Assert.Equal(1,result.Markers[0].BarPosition,6);
    }
}
