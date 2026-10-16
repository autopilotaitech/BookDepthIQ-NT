using BookDepthIQ.Core.Charts;
using Xunit;
public class ChartSessionZoneTests
{
    static readonly DateTimeOffset Start=new(2026,10,15,8,0,0,TimeSpan.Zero);
    static ChartBar Bar(int sec,long high=110,long low=90,bool forming=false)=>new(sec,Start.AddSeconds(sec),new(2026,10,15),100,high,low,100,1,1,forming);
    static SessionDefinition Def(string start="08:00",string end="08:01",string zone="UTC")=>new("london","LONDON",true,"#00BBD6",start,end,zone);
    [Fact] public void Exact_boundaries_exclude_outside_prices_and_freeze_completed_range()
    {
        var result=ChartSessionZones.Build(new[]{Bar(0,999,-999),Bar(30,110,90),Bar(60,120,80),Bar(90,999,-999)},new[]{Def()},3,Start.AddMinutes(2),true);
        var zone=Assert.Single(result.Zones);Assert.Equal(120,zone.High);Assert.Equal(80,zone.Low);Assert.Equal("COMPLETE",zone.State);Assert.Equal(Start.AddMinutes(1),zone.Progress);
        Assert.Equal(Start.AddDays(1),zone.ExtendUntil);
    }
    [Fact] public void Interior_source_gaps_cannot_be_labeled_complete()
    {
        var result=ChartSessionZones.Build(new[]{Bar(30),Bar(120)},new[]{Def(end:"08:02")},3,Start.AddMinutes(3),true);
        Assert.Equal("PARTIAL",Assert.Single(result.Zones).State);
    }
    [Fact] public void Old_forming_history_does_not_advance_coverage_to_wall_clock()
    {
        var zone=Assert.Single(ChartSessionZones.Build(new[]{Bar(30,forming:true)},new[]{Def(end:"16:00")},3,Start.AddHours(2),true).Zones);
        Assert.Contains("STALE",zone.State);Assert.Equal(Start.AddSeconds(30),zone.Progress);
    }
    [Fact] public void Truncated_stale_forming_empty_and_invalid_inputs_are_not_certified_complete()
    {
        Assert.Empty(ChartSessionZones.Build(Array.Empty<ChartBar>(),new[]{Def()},3,Start,true).Zones);
        Assert.Equal("PARTIAL",Assert.Single(ChartSessionZones.Build(new[]{Bar(60)},new[]{Def()},3,Start.AddMinutes(2),true).Zones).State);
        Assert.Contains("STALE",Assert.Single(ChartSessionZones.Build(new[]{Bar(30),Bar(60)},new[]{Def()},3,Start.AddMinutes(2),false).Zones).State);
        var active=Assert.Single(ChartSessionZones.Build(new[]{Bar(30),Bar(60,120,80,true)},new[]{Def()},3,Start.AddSeconds(45),true).Zones);
        Assert.Equal("LIVE",active.State);Assert.Equal(120,active.High);Assert.Equal(Start.AddSeconds(45),active.Progress);
        var bad=ChartSessionZones.Build(new[]{Bar(30)},new[]{Def(zone:"not-a-zone")},3,Start,true);Assert.Empty(bad.Zones);Assert.NotEmpty(bad.Notes);
    }
    [Theory]
    [InlineData("Europe/London",2026,3,8,8)] [InlineData("Europe/London",2026,3,29,7)]
    [InlineData("Europe/London",2026,10,25,8)] [InlineData("America/New_York",2026,3,8,12)]
    [InlineData("America/New_York",2026,11,1,13)] [InlineData("Asia/Tokyo",2026,3,29,23)]
    public void Named_zones_follow_independent_dst_dates(string zone,int year,int month,int day,int utcHour)
    {
        Assert.True(ChartSessionZones.TryWindow(Def("08:00","09:00",zone),new DateOnly(year,month,day),out var start,out _,out _));
        Assert.Equal(utcHour,start.Hour);
        if(zone=="Asia/Tokyo") Assert.Equal(new DateOnly(2026,3,28),DateOnly.FromDateTime(start.UtcDateTime));
    }
    [Fact] public void Overnight_and_ambiguous_or_skipped_dst_endpoints_are_explicit()
    {
        Assert.True(ChartSessionZones.TryWindow(Def("23:00","01:00"),new(2026,10,15),out var start,out var end,out _));Assert.Equal(TimeSpan.FromHours(2),end-start);
        Assert.False(ChartSessionZones.TryWindow(Def("02:30","04:00","America/New_York"),new(2026,3,8),out _,out _,out var reason));Assert.Contains("DST",reason);
        Assert.False(ChartSessionZones.TryWindow(Def("01:30","03:00","America/New_York"),new(2026,11,1),out _,out _,out _));
        Assert.False(ChartSessionZones.TryWindow(Def("08:00","08:00"),new(2026,10,15),out _,out _,out _));
    }
    [Fact] public void Overlaps_negative_prices_history_limit_and_corrections_are_independent()
    {
        var bars=Enumerable.Range(0,5).SelectMany(d=>new[]{Bar(30,120,-50) with {Time=Start.AddDays(-d).AddSeconds(30)},Bar(60,130,-60) with {Time=Start.AddDays(-d).AddSeconds(60)}}).OrderBy(b=>b.Time).ToArray();
        var definitions=new[]{Def(),Def() with {Id="ny",Name="NEW YORK"}};
        var result=ChartSessionZones.Build(bars,definitions,2,Start.AddMinutes(2),true);Assert.Equal(4,result.Zones.Count);Assert.All(result.Zones,z=>Assert.Equal(-60,z.Low));
        var corrected=bars.Select(b=>b.Time==Start.AddSeconds(60)?b with {High=140,Revision=2}:b).ToArray();
        Assert.Contains(ChartSessionZones.Build(corrected,definitions,2,Start.AddMinutes(2),true).Zones,z=>z.High==140);
    }
}
