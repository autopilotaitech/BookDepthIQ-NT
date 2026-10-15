using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using System.Reflection;
using BookDepthIQ.Core;
using BookDepthIQ.Dom;
using Xunit;

public class NoChaseTests
{
    [AvaloniaFact]
    public void Settings_toggle_is_off_until_user_applies_it()
    {
        var cfg=new Settings(); var window=new SettingsWindow(cfg,()=>{},"Indicators");
        try
        {
            window.Show();
            var row=window.GetVisualDescendants().OfType<TextBlock>().Single(t=>t.Text=="Show no-chase level")
                .GetVisualAncestors().OfType<Grid>().First();
            var toggle=row.Children.OfType<ToggleButton>().Single();
            Assert.False(toggle.IsChecked); toggle.IsChecked=true;
            Assert.False(cfg.ShowNoChase);
            var setters=(List<Action>)typeof(SettingsWindow).GetField("apply",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
            foreach(var setter in setters) setter();
            Assert.True(cfg.ShowNoChase); Assert.True(cfg.ShowTrend);
        }
        finally {window.Close();}
    }

    [AvaloniaTheory]
    [InlineData(true)] [InlineData(false)]
    public void No_chase_is_hidden_by_default_without_removing_supertrend_lines(bool up)
    {
        using var h=new TicketHarness(); h.Config.Gex.On=false;
        for(var i=0;i<80;i++) h.State.Trend.OnTrade(i*60_000L,120000+(up?i:-i)*4,10,up);
        var snap=h.State.Trend.Snapshot();
        Assert.NotNull(snap.T1.LineTicks); Assert.NotNull(snap.T2.LineTicks);
        Assert.True(h.State.Trend.T1.AtrTicks>0);
        TicketHarness.Set(h.State,"<TrendSnap>k__BackingField",snap);
        var lines=(IReadOnlyList<LadderLine>)h.Call("LadderLines")!;
        Assert.DoesNotContain(lines,l=>l.Label.Contains("NO CHASE"));
        Assert.Contains(lines,l=>l.Label.EndsWith("ST1") && l.Ticks==snap.T1.LineTicks);
        Assert.Contains(lines,l=>l.Label.EndsWith("ST2") && l.Ticks==snap.T2.LineTicks);
        Assert.Empty(h.Commands);
    }

    [AvaloniaTheory]
    [InlineData(true)] [InlineData(false)]
    public void No_chase_can_be_explicitly_enabled_without_changing_its_distance_rule(bool up)
    {
        using var h=new TicketHarness(); h.Config.Gex.On=false; h.Config.ShowNoChase=true;
        for(var i=0;i<80;i++) h.State.Trend.OnTrade(i*60_000L,120000+(up?i:-i)*4,10,up);
        var snap=h.State.Trend.Snapshot();
        TicketHarness.Set(h.State,"<TrendSnap>k__BackingField",snap);
        var lines=(IReadOnlyList<LadderLine>)h.Call("LadderLines")!;
        var guide=Assert.Single(lines,l=>l.Label.Contains("NO CHASE"));
        Assert.Equal((long)Math.Round(snap.T1.LineTicks!.Value+(snap.T1.Dir==TrendDir.Up?1:-1)*2*h.State.Trend.T1.AtrTicks!.Value),guide.Ticks);
        h.Config.ShowTrend=false;
        Assert.DoesNotContain((IReadOnlyList<LadderLine>)h.Call("LadderLines")!,l=>l.Label.Contains("NO CHASE"));
        h.Config.ShowTrend=true; h.Config.ShowNoChase=false;
        Assert.DoesNotContain((IReadOnlyList<LadderLine>)h.Call("LadderLines")!,l=>l.Label.Contains("NO CHASE"));
        Assert.Empty(h.Commands);
    }

    [Fact]
    public void Older_settings_hide_the_guide_and_the_opt_in_round_trips()
    {
        var cfg=System.Text.Json.JsonSerializer.Deserialize<Settings>("{\"ShowTrend\":true}")!;
        Assert.False(cfg.ShowNoChase); Assert.True(cfg.ShowTrend);
        cfg.ShowNoChase=true;
        var saved=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(cfg))!;
        Assert.True(saved.ShowNoChase); Assert.True(saved.ShowTrend);
    }
}
