using BookDepthIQ.Core;
using BookDepthIQ.Core.Charts;
using BookDepthIQ.Dom.Charts;
using Xunit;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Avalonia.Interactivity;

public class PaxChartParityTests
{
    [AvaloniaFact] public void Settings_applies_zero_previous_sessions_and_json_keeps_it()
    {
        var pane=new ChartPaneSettings {PaxDays=2};int changes=0;
        var window=new ChartSettingsWindow(pane,()=>changes++);window.Show();
        try
        {
            var field=window.GetVisualDescendants().OfType<NumericUpDown>().Single(n=>n.Maximum==8);
            field.Value=0;
            window.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"APPLY")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(0,pane.PaxDays);Assert.Equal(1,changes);
            var saved=System.Text.Json.JsonSerializer.Deserialize<ChartPaneSettings>(System.Text.Json.JsonSerializer.Serialize(pane))!;saved.Normalize();Assert.Equal(0,saved.PaxDays);
        }
        finally {window.Close();}
    }
    static readonly DateTimeOffset Bell=new(2026,10,16,13,30,0,TimeSpan.Zero);
    static ChartBar Bar(int index,int seconds,long high=408,long low=396,bool forming=false)=>new(index,Bell.AddSeconds(seconds),new(2026,10,16),400,high,low,400,1,1,forming);
    static readonly PaxOrCfg Config=new() {NqLevelPoints=2,EndTime="17:00:00"};
    [Fact] public void History_zero_is_preserved_and_means_current_calendar_day_only()
    {
        var pane=new ChartPaneSettings {PaxDays=0};pane.Normalize();Assert.Equal(0,pane.PaxDays);
        var yesterday=Bar(0,30) with {Time=Bell.AddDays(-1).AddSeconds(30)};
        var lines=ChartOpeningRanges.Build(new[]{yesterday,Bar(1,30)},Config,"NQ",.25,3,0,Bell.AddHours(1));
        Assert.All(lines,l=>Assert.Equal(Bell.AddSeconds(30),l.Start));Assert.NotEmpty(lines);
        Assert.Empty(ChartOpeningRanges.Build(new[]{yesterday},Config,"NQ",.25,3,0,Bell.AddMinutes(-1)));
    }
    [Fact] public void Completed_bar_adds_one_rung_per_side_and_forming_bars_do_not_activate_extensions()
    {
        var lines=ChartOpeningRanges.Build(new[]{Bar(0,30),Bar(1,60,500,300),Bar(2,90,550,250,true)},Config,"NQ",.25,3,0,Bell.AddSeconds(75));
        Assert.Equal(2,lines.Count(l=>l.Label=="OR EXT2"));Assert.DoesNotContain(lines,l=>l.Label=="OR EXT3");
        Assert.All(lines.Where(l=>l.Label=="OR EXT2"),l=>Assert.Equal(Bell.AddSeconds(60),l.Start));
        Assert.All(lines,l=>Assert.Equal(new DateTimeOffset(2026,10,16,22,0,0,TimeSpan.Zero),l.End));
    }
    [Fact] public void After_plot_end_cannot_trigger_a_new_extension()
    {
        var lines=ChartOpeningRanges.Build(new[]{Bar(0,30),Bar(1,9*3600,600,200)},Config,"NQ",.25,3,0,Bell.AddHours(10));
        Assert.DoesNotContain(lines,l=>l.Label=="OR EXT2");
    }
    [Fact] public void Zero_step_keeps_or_high_low_but_disables_extensions()
    {
        var lines=ChartOpeningRanges.Build(new[]{Bar(0,30)},Config with {NqLevelPoints=0},"NQ",.25,3,0,Bell.AddMinutes(1));
        Assert.Contains(lines,l=>l.Label=="OR H");Assert.Contains(lines,l=>l.Label=="OR L");
        Assert.DoesNotContain(lines,l=>l.Label.StartsWith("OR EXT"));
    }
    [Fact] public void Prior_and_current_ranges_have_distinct_calendar_identity()
    {
        var yesterday=Bar(0,30) with {Time=Bell.AddDays(-1).AddSeconds(30)};
        var lines=ChartOpeningRanges.Build(new[]{yesterday,Bar(1,30)},Config,"NQ",.25,3,1,Bell.AddHours(1));
        Assert.Contains(lines,l=>l.IsCurrent&&l.SessionDate==new DateOnly(2026,10,16));
        Assert.Contains(lines,l=>!l.IsCurrent&&l.SessionDate==new DateOnly(2026,10,15));
    }
}
