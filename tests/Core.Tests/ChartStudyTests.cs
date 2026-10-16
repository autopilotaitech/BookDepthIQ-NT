using BookDepthIQ.Core;
using BookDepthIQ.Core.Charts;
using Xunit;

public class ChartStudyTests
{
    [Theory] [InlineData(2026,3,6,23)] [InlineData(2026,10,30,22)]
    public void Historical_endpoints_stop_at_configured_same_day_plot_end(int year,int month,int day,int utcHour)
    {
        var time=new DateTimeOffset(year,month,day,month==3?14:13,30,30,TimeSpan.Zero);
        var bar=new ChartBar(0,time,new(year,month,day),400,408,396,404,25,1,false);
        var lines=ChartOpeningRanges.Build(new[]{bar},new PaxOrCfg(),"NQ",.25,3,8,time.AddHours(1));
        Assert.Equal(new DateTimeOffset(year,month,day,utcHour,0,0,TimeSpan.Zero),lines[0].End);
    }
    [Fact] public void Studies_warm_up_and_use_corrected_selected_frame_closes()
    {
        var closes=new double[]{1,2,3,4};
        Assert.Equal(new double?[]{null,null,2,3},ChartStudies.Sma(closes,3));
        Assert.Equal(new double?[]{null,null,2,3},ChartStudies.Ema(closes,3));
    }
    [Fact] public void Opening_range_requires_complete_exact_thirty_second_bar_and_disables_on_htf()
    {
        var start=new DateTimeOffset(2026,10,15,13,30,0,TimeSpan.Zero);
        var first=new ChartBar(0,start.AddSeconds(30),new(2026,10,15),400,408,396,404,25,1,false);
        var after=first with {Index=1,Time=start.AddMinutes(1),High=420,Low=400};
        var config=new PaxOrCfg {Timezone="America/Chicago",StartTime="08:30:00",NqLevelPoints=2};
        var lines=ChartOpeningRanges.Build(new[]{first,after},config,"NQ 12-26",.25,3,8);
        Assert.Contains(lines,l=>l.Label=="OR H"&&l.Ticks==408);
        Assert.Contains(lines,l=>l.Label=="OR EXT1"&&l.Ticks==416);
        Assert.Empty(ChartOpeningRanges.Build(new[]{first with {Forming=true}},config,"NQ 12-26",.25,3,8));
        Assert.Empty(ChartOpeningRanges.Build(new[]{after},config,"NQ 12-26",.25,3,8));
        Assert.Empty(ChartOpeningRanges.Build(new[]{first,after},config,"NQ 12-26",.25,60,8));
    }
}
