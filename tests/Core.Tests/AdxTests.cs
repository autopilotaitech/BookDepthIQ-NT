using BookDepthIQ.Core;
using BookDepthIQ.Dom;
using Xunit;

public class AdxTests
{
    static readonly long[][] Fixture = {
        new long[]{12,9,11},new long[]{13,10,12},new long[]{12,8,9},
        new long[]{11,7,8},new long[]{14,9,13},new long[]{14,10,11},
        new long[]{15,10,14},new long[]{13,8,9},new long[]{12,7,8}
    };
    static void Bar(AdxBars bars,int index,long[] values,int minutes=1)
    {
        var start=index*minutes*60_000L;
        bars.Add(start,values[1]); bars.Add(start+1000,values[0]); bars.Add(start+2000,values[2]);
    }

    [Fact]
    public void Closed_bars_match_ninjatrader_seed_and_wilder_smoothing()
    {
        var bars=new AdxBars(1,3);
        for(var i=0;i<Fixture.Length;i++) Bar(bars,i,Fixture[i]);
        var reading=bars.Snapshot(9*60_000L);
        Assert.NotNull(reading);
        Assert.Equal(9,reading.Bars);
        Assert.Equal(AdxGolden,reading.Value,9);
        Assert.Equal(7.500228665508097,reading.PlusDi,9);
        Assert.Equal(17.60724412329644,reading.MinusDi,9);
    }
    // Independent golden fixture calculated from NT's initial ADX=50, running sums and Wilder recurrence.
    const double AdxGolden = 39.554979002180104;

    [Theory]
    [InlineData(1)] [InlineData(3)] [InlineData(5)] [InlineData(10)] [InlineData(15)] [InlineData(30)]
    public void Uses_selected_timeframe_and_ignores_the_forming_bar(int minutes)
    {
        var bars=new AdxBars(minutes,3);
        for(var i=0;i<8;i++) Bar(bars,i,new long[]{100+i,98+i,99+i},minutes);
        Assert.Null(bars.Snapshot(6*minutes*60_000L));
        var closed=bars.Snapshot(7*minutes*60_000L)!;
        Assert.Equal(7,closed.Bars);
        bars.Add(7*minutes*60_000L+3000,500);
        Assert.Equal(closed,bars.Snapshot(7*minutes*60_000L+5000));
        Assert.NotEqual(closed.Value,bars.Snapshot(8*minutes*60_000L)!.Value);
    }

    [Fact]
    public void Late_history_and_live_prints_merge_chronologically_without_changing_close()
    {
        var sorted=new AdxBars(1,3); var mixed=new AdxBars(1,3);
        for(var i=0;i<Fixture.Length;i++) Bar(sorted,i,Fixture[i]);
        for(var i=Fixture.Length-1;i>=0;i--) {
            mixed.Add(i*60_000L+2000,Fixture[i][2]);
            mixed.Add(i*60_000L+1000,Fixture[i][0]); mixed.Add(i*60_000L,Fixture[i][1]);
        }
        Assert.NotNull(mixed.Snapshot(9*60_000L));
        Assert.Equal(sorted.Snapshot(9*60_000L),mixed.Snapshot(9*60_000L));
    }

    [Fact]
    public void Thirty_minute_period14_is_not_limited_to_four_hours()
    {
        var bars=new AdxBars(30,14);
        for(var i=0;i<80;i++) Bar(bars,i,new long[]{100+i,98+i,99+i},30);
        Assert.Equal(80,bars.Snapshot(80*30*60_000L)!.Bars);
        Assert.InRange(new Settings { AdxMinutes=30,AdxPeriod=14 }.EffectiveHistoryHours,36,48);
    }

    [Fact]
    public void Invalid_saved_settings_are_safe_and_sixty_minutes_is_not_supported()
    {
        var cfg=new Settings { AdxMinutes=60,AdxPeriod=-7,AdxThreshold=double.NaN };
        Assert.Equal(3,cfg.AdxMinutes); Assert.Equal(1,cfg.AdxPeriod); Assert.Equal(25,cfg.AdxThreshold);
        Assert.Equal(48,new Settings { AdxMinutes=30,AdxPeriod=100 }.EffectiveHistoryHours);
    }

    [Theory]
    [InlineData(18.4,30,10,25,MarketState.Chop,"CHOP · ADX 18.4")]
    [InlineData(28.6,30,10,25,MarketState.TrendUp,"TREND ▲ · ADX 28.6")]
    [InlineData(28.6,10,30,25,MarketState.TrendDown,"TREND ▼ · ADX 28.6")]
    [InlineData(28.6,30,10,30,MarketState.Chop,"CHOP · ADX 28.6")]
    [InlineData(50,0,0,25,MarketState.Chop,"CHOP · ADX 50.0")]
    public void Score_controls_strength_and_di_controls_only_direction(double score,double plus,double minus,double threshold,MarketState state,string label)
    {
        var view=Structure.MarketAdx(new(score,plus,minus,50),3,14,threshold,null,null,null,null,null);
        Assert.Equal(state,view.State); Assert.Equal(label,view.AdxLabel);
        Assert.Contains("3m",view.Why); Assert.Contains("14",view.Why);
    }

    [Fact]
    public void Insufficient_bars_do_not_show_a_score_or_trend()
    {
        var view=Structure.MarketAdx(null,30,14,25,null,null,null,null,null);
        Assert.Equal(MarketState.Warming,view.State); Assert.Equal("ADX —",view.AdxLabel);
    }

    [Fact]
    public void New_settings_default_and_round_trip_without_touching_user_files()
    {
        var old=System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"HistoryHours\":4}")!;
        Assert.Equal(3,old.AdxMinutes); Assert.Equal(14,old.AdxPeriod); Assert.Equal(25,old.AdxThreshold);
        old.AdxMinutes=15; old.AdxPeriod=20; old.AdxThreshold=22.5;
        var saved=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(old))!;
        Assert.Equal(15,saved.AdxMinutes); Assert.Equal(20,saved.AdxPeriod); Assert.Equal(22.5,saved.AdxThreshold);
    }

    [Theory]
    [InlineData(1,3)] [InlineData(14,80)] [InlineData(100,240)]
    public void Steady_direction_matches_closed_form_for_multiple_periods(int period,int count)
    {
        var bars=new AdxBars(1,period);
        for(var i=0;i<count;i++) Bar(bars,i,new long[]{100+i,98+i,99+i});
        var reading=bars.Snapshot(count*60_000L)!;
        Assert.NotNull(reading);
        Assert.Equal(100-50*Math.Pow((period-1.0)/period,count-1),reading.Value,9);
        Assert.True(reading.PlusDi>0); Assert.Equal(0,reading.MinusDi);
    }

    [Fact]
    public void Flat_bars_match_ninjatrader_fallback_but_do_not_claim_direction()
    {
        var bars=new AdxBars(1,3);
        for(var i=0;i<8;i++) bars.Add(i*60_000L,100);
        Assert.Equal(new AdxReading(50,0,0,8),bars.Snapshot(8*60_000L));
    }

    [Fact]
    public void No_synthetic_bars_are_added_during_gaps_and_closed_bar_cache_is_reused()
    {
        var bars=new AdxBars(1,3);
        for(var i=0;i<8;i++) Bar(bars,i*10,new long[]{100+i,98+i,99+i});
        var reading=bars.Snapshot(80*60_000L)!;
        Assert.Equal(8,reading.Bars);
        Assert.Same(reading,bars.Snapshot(80*60_000L+1000));
        bars.Add(80*60_000L+2000,500);
        Assert.Same(reading,bars.Snapshot(80*60_000L+3000));
    }

    [Fact]
    public void Late_completed_candle_invalidates_cache_but_does_not_overwrite_newer_close()
    {
        var bars=new AdxBars(1,3);
        for(var i=0;i<Fixture.Length;i++) Bar(bars,i,Fixture[i]);
        var before=bars.Snapshot(9*60_000L)!;
        bars.Add(3*60_000L+1500,20);
        var after=bars.Snapshot(9*60_000L)!;
        Assert.NotEqual(before.Value,after.Value);
        Assert.Equal(9,after.Bars);
    }

    [Fact]
    public void Long_running_cache_stays_bounded()
    {
        var bars=new AdxBars(1,14);
        for(var i=0;i<5000;i++) bars.Add(i*60_000L,100+i);
        var reading=bars.Snapshot(5000*60_000L)!;
        Assert.Equal(4096,reading.Bars); Assert.InRange(reading.Value,99.99,100);
    }

    [Avalonia.Headless.XUnit.AvaloniaFact]
    public void Dom_uses_history_and_resets_adx_when_analytics_settings_change()
    {
        using var h=new TicketHarness(); h.Config.AdxMinutes=1; h.Config.AdxPeriod=3; h.State.ResetAnalytics();
        var prints=new List<object[]>();
        for(var i=0;i<9;i++) foreach(var offset in new[]{0,1000,2000})
            prints.Add(new object[]{i*60_000L+offset,Fixture[i][offset/1000]/4.0,1,"b"});
        h.Message(System.Text.Json.JsonSerializer.Serialize(new {t="hist",inst=h.State.Instrument,trades=prints,done=true}));
        Assert.Equal(AdxGolden,h.State.MarketView(9*60_000L).Adx!.Value,9);
        h.State.Connected=false;
        Assert.Equal("ADX —",h.State.MarketView(9*60_000L).AdxLabel);
        h.State.Connected=true; h.State.LastFeedMs=Environment.TickCount64-5000;
        Assert.Equal("ADX —",h.State.MarketView(9*60_000L).AdxLabel);
        h.State.LastFeedMs=Environment.TickCount64;
        h.Config.AdxMinutes=30; h.State.ResetAnalytics();
        Assert.Equal("ADX —",h.State.MarketView(9*60_000L).AdxLabel);
    }
}
