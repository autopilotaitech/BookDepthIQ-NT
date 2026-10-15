using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using BookDepthIQ.Dom;
using BookDepthIQ.Core;
using Xunit;

public class AdxUiTests
{
    static Grid Row(Window window,string name) => window.GetVisualDescendants().OfType<TextBlock>()
        .Single(t=>t.Text==name).GetVisualAncestors().OfType<Grid>().First();

    [AvaloniaFact]
    public void Existing_market_badge_renders_adx_and_clears_stale_score()
    {
        using var h=new TicketHarness(); h.Config.AdxMinutes=1; h.Config.AdxPeriod=3;
        h.State.ResetAnalytics();
        var pill=Ui.Pill("STATE",Pal.Muted); TicketHarness.Set(h.Window,"statePill",pill);
        var now=DomState.NowMs()/60_000L*60_000L;
        var prints=Enumerable.Range(0,20).Select(i=>new object[]{now-(20-i)*60_000L,30000+i,1,"b"}).ToArray();
        h.Message(System.Text.Json.JsonSerializer.Serialize(new {t="hist",inst=h.State.Instrument,trades=prints,done=true}));
        h.Call("RefreshMarketState",now);
        Assert.Equal("TREND ▲ · ADX 100.0 · EXTENDED",Assert.IsType<TextBlock>(pill.Child).Text);
        Assert.Contains("1m",ToolTip.GetTip(pill)!.ToString());
        h.Config.AdxThreshold=100;
        h.State.LastFeedMs=Environment.TickCount64-5000;
        h.Call("RefreshMarketState",now);
        Assert.Equal("ADX —",Assert.IsType<TextBlock>(pill.Child).Text);
        Assert.Empty(h.Commands);
    }

    [AvaloniaFact]
    public void Indicator_settings_offer_all_six_timeframes_and_apply_period_and_threshold()
    {
        var cfg=new Settings();
        var window=new SettingsWindow(cfg,()=>throw new Exception("Save not invoked in this isolated test"),"Indicators");
        try
        {
            window.Show();
            if(Environment.GetEnvironmentVariable("BDIQ_UI_CAPTURE_DIR") is {Length:>0} captureDir)
            {
                Directory.CreateDirectory(captureDir);
                using var frame=Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(window);
                Assert.NotNull(frame); frame.Save(Path.Combine(captureDir,"adx-settings.png"));
            }
            var timeframe=Row(window,"ADX timeframe").Children.OfType<Seg>().Single();
            Assert.Equal(1,timeframe.SelectedIndex);
            var labels=timeframe.GetVisualDescendants().OfType<TextBlock>().Select(t=>t.Text).ToArray();
            foreach(var label in new[]{"1M","3M","5M","10M","15M","30M"}) Assert.Contains(label,labels);
            Assert.DoesNotContain("60M",labels);
            timeframe.SelectedIndex=5;
            Row(window,"ADX period").Children.OfType<TextBox>().Single().Text="20";
            Row(window,"Trend threshold").Children.OfType<TextBox>().Single().Text="22.5";
            Assert.Equal(3,cfg.AdxMinutes); Assert.Equal(14,cfg.AdxPeriod);
            // Execute the real deferred field setters, not Save(): no appdata/settings writes.
            var setters=(List<Action>)typeof(SettingsWindow).GetField("apply",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            foreach(var setter in setters) setter();
            Assert.Equal(30,cfg.AdxMinutes); Assert.Equal(20,cfg.AdxPeriod); Assert.Equal(22.5,cfg.AdxThreshold);
        }
        finally {window.Close();}
    }

    [AvaloniaFact]
    public void Cancelling_settings_does_not_change_adx_configuration()
    {
        var cfg=new Settings(); var window=new SettingsWindow(cfg,()=>{},"Indicators");
        window.Show();
        Row(window,"ADX timeframe").Children.OfType<Seg>().Single().SelectedIndex=0;
        Row(window,"ADX period").Children.OfType<TextBox>().Single().Text="50";
        window.Close();
        Assert.Equal(3,cfg.AdxMinutes); Assert.Equal(14,cfg.AdxPeriod); Assert.Equal(25,cfg.AdxThreshold);
    }
}
