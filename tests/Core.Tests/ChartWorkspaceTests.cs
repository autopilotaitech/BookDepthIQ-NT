using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using BookDepthIQ.Dom.Charts;
using Xunit;

public class ChartWorkspaceTests
{
    [AvaloniaFact] public void Narrow_window_preserves_saved_ratio_and_hides_charts_temporarily()
    {
        var settings=new ChartWorkspaceSettings {Enabled=true,WidthRatio=.36};
        using var workspace=new ChartWorkspace(new Border(),settings,new ChartCoordinator(),()=>{});
        workspace.Measure(new Size(1280,900));workspace.Arrange(new Rect(0,0,1280,900));Assert.False(workspace.ChartsVisible);Assert.True(settings.Enabled);
        workspace.Measure(new Size(1920,900));workspace.Arrange(new Rect(0,0,1920,900));Assert.True(workspace.ChartsVisible);Assert.Equal(.36,settings.WidthRatio);
    }
    [AvaloniaFact] public void Renderer_shows_candles_and_refreshes_study_preferences_without_new_data()
    {
        var settings=new ChartPaneSettings();var chart=new CandleChartControl(settings);
        var bars=Enumerable.Range(0,180).Select(i=>new BookDepthIQ.Core.Charts.ChartBar(i,DateTimeOffset.UnixEpoch.AddMinutes(i),new(2026,10,15),120000+i*2,120012+i*2,119990+i*2,120006+i*2,10,1,false)).ToArray();
        chart.Measure(new Size(600,420));chart.Arrange(new Rect(0,0,600,420));
        var snapshot=new ChartSnapshot(bars,.25,"TEST FIXTURE",null,1);chart.Update(snapshot);
        byte[] Render()
        {
            using var bmp=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(600,420));bmp.Render(chart);using var bytes=new MemoryStream();bmp.Save(bytes);return bytes.ToArray();
        }
        var before=Render();settings.Sma=true;chart.Update(snapshot);var after=Render();Assert.False(before.SequenceEqual(after));
        if(Environment.GetEnvironmentVariable("BDIQ_CHART_TEST_IMAGE") is {Length:>0} path) File.WriteAllBytes(path,after);
    }
    [AvaloniaFact] public void Charts_wrap_original_dom_and_collapse_without_rebuilding_it()
    {
        var original=new Border();var settings=new ChartWorkspaceSettings();var model=new ChartCoordinator();
        using var workspace=new ChartWorkspace(original,settings,model,()=>{});
        workspace.Measure(new Size(1920,1000));workspace.Arrange(new Rect(0,0,1920,1000));
        Assert.Same(original,workspace.Dom);
        Assert.False(workspace.ChartsVisible);
        workspace.SetEnabled(true);workspace.Measure(new Size(1920,1000));workspace.Arrange(new Rect(0,0,1920,1000));
        Assert.True(workspace.ChartsVisible); Assert.InRange(original.Bounds.Width,1290,1310);
        workspace.SetEnabled(false);workspace.Measure(new Size(1920,1000));workspace.Arrange(new Rect(0,0,1920,1000));
        Assert.InRange(original.Bounds.Width,1919,1920);Assert.Same(original,workspace.Dom);
    }
    [Fact] public void Chart_preferences_normalize_bad_values_and_keep_history_per_frame()
    {
        var s=new ChartWorkspaceSettings {WidthRatio=double.NaN};s.Normalize();Assert.Equal(.32,s.WidthRatio);
        s.Top.SetLookback(3,333);Assert.Equal(333,s.Top.Lookback(3));Assert.Equal(500,s.Top.Lookback(1440));
        s.Top.SetLookback(3,100000);Assert.Equal(10000,s.Top.Lookback(3));
        s.Top.Pax=true;s.Top.Frame=60;Assert.False(s.Top.PaxActive);s.Top.Frame=3;Assert.True(s.Top.PaxActive);
    }
    [AvaloniaFact] public void Wide_saved_tape_width_does_not_force_chart_workspace_offscreen()
    {
        var settings=new ChartWorkspaceSettings {Enabled=true};
        using var workspace=new ChartWorkspace(new Border(),settings,new ChartCoordinator(),()=>{}) {MinimumDomWidth=1665};
        workspace.Measure(new Size(1600,900));workspace.Arrange(new Rect(0,0,1600,900));
        Assert.False(workspace.ChartsVisible);Assert.True(workspace.Dom.Bounds.Width<=1600);
    }
}
