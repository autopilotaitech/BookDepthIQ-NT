using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using BookDepthIQ.Core.Charts;
using BookDepthIQ.Dom.Charts;
using Xunit;
public class ChartTradeUiTests
{
    [AvaloniaTheory] [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void Threshold_print_has_visible_quantity_without_requiring_a_large_marker(double scale)
    {
        var chart=Fixture(new() {ShowLargeTrades=true});chart.TradeProfile=new();
        chart.CapturedTrades=new(new[]{new ChartPrint(1,"NQ TEST",100,50,'s',90000)},Array.Empty<ChartTradeCoverage>(),0,1);
        var labeled=Render(chart,scale);chart.TradeProfile.Labels=TradeQuantityLabels.Off;
        Assert.False(labeled.SequenceEqual(Render(chart,scale)));
    }
    [Fact] public void Legacy_default_style_upgrades_without_changing_threshold_colors_or_custom_preferences()
    {
        var old=new LargeTradeProfile {MinContracts=50,MinDiameter=12,MaxDiameter=42,Opacity=.2,Labels=TradeQuantityLabels.Large,SellColor="#AA7733"};old.Normalize();
        Assert.Equal(TradeQuantityLabels.All,old.Labels);Assert.True(old.MinDiameter>=24);Assert.Equal(50,old.MinContracts);Assert.Equal("#AA7733",old.SellColor);
        var custom=new LargeTradeProfile {MinDiameter=18,MaxDiameter=55,Opacity=.7,Labels=TradeQuantityLabels.Off};custom.Normalize();
        Assert.Equal(18,custom.MinDiameter);Assert.Equal(.7,custom.Opacity);Assert.Equal(TradeQuantityLabels.Off,custom.Labels);
    }
    [Fact] public void Saved_legacy_style_migrates_once_and_later_user_choices_are_preserved()
    {
        var profile=System.Text.Json.JsonSerializer.Deserialize<LargeTradeProfile>("{\"MinContracts\":75,\"MinDiameter\":12,\"MaxDiameter\":42,\"Opacity\":0.2,\"Labels\":1}")!;
        profile.Normalize();Assert.Equal(75,profile.MinContracts);Assert.Equal(TradeQuantityLabels.All,profile.Labels);
        profile.MinDiameter=12;profile.MaxDiameter=42;profile.Opacity=.2;profile.Labels=TradeQuantityLabels.Large;
        var saved=System.Text.Json.JsonSerializer.Deserialize<LargeTradeProfile>(System.Text.Json.JsonSerializer.Serialize(profile))!;saved.Normalize();
        Assert.Equal(12,saved.MinDiameter);Assert.Equal(TradeQuantityLabels.Large,saved.Labels);
    }
    static CandleChartControl Fixture(ChartPaneSettings settings)
    {
        var chart=new CandleChartControl(settings);
        var bars=Enumerable.Range(0,10).Select(i=>new ChartBar(i,DateTimeOffset.UnixEpoch.AddMinutes(i),new(2026,10,15),100,110,90,100,1,1,false)).ToArray();
        chart.Update(new(bars,.25,"LIVE",100,1));chart.Measure(new Size(600,420));chart.Arrange(new Rect(0,0,600,420));
        chart.TradeProfile=new() {MinDiameter=32,MaxDiameter=42,Labels=TradeQuantityLabels.All};
        chart.CapturedTrades=new(new[]{new ChartPrint(1,"NQ TEST",100,85,'b',90000),new ChartPrint(2,"NQ TEST",105,120,'s',150000),new ChartPrint(3,"NQ TEST",95,65,'?',210000)},Array.Empty<ChartTradeCoverage>(),0,1);
        return chart;
    }
    static byte[] Render(CandleChartControl chart,double scale=1)
    {
        using var bmp=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)(600*scale),(int)(420*scale)),new Vector(96*scale,96*scale));bmp.Render(chart);
        using var bytes=new MemoryStream();bmp.Save(bytes);return bytes.ToArray();
    }
    [AvaloniaTheory] [InlineData(1)] [InlineData(1.5)] [InlineData(2)]
    public void Bubbles_toggle_and_labels_render_without_changing_autoscale(double scale)
    {
        var settings=new ChartPaneSettings();var chart=Fixture(settings);var range=chart.Geometry;var before=Render(chart,scale);
        settings.ShowLargeTrades=true;var visible=Render(chart,scale);Assert.False(before.SequenceEqual(visible));
        Assert.Equal(range.Low,chart.Geometry.Low);Assert.Equal(range.High,chart.Geometry.High);
        chart.TradeProfile=chart.TradeProfile with {Labels=TradeQuantityLabels.Off};Assert.False(visible.SequenceEqual(Render(chart,scale)));
        settings.ShowLargeTrades=false;Assert.True(before.SequenceEqual(Render(chart,scale)));
    }
    [AvaloniaFact] public void Hover_identifies_print_not_parent_order_and_does_not_send_ticket_commands()
    {
        using var h=new TicketHarness();var chart=Fixture(new() {ShowLargeTrades=true});Render(chart);
        var x=chart.Geometry.X(2);var y=chart.Geometry.Y(100);chart.SetCursor(new Point(x,y));
        Assert.Contains("85",ToolTip.GetTip(chart)?.ToString());Assert.Contains("Ask-side",ToolTip.GetTip(chart)?.ToString());
        Assert.Empty(h.Commands);
    }
    [AvaloniaFact] public void Actual_pointer_events_hover_then_zoom_pan_and_clear_without_crosshair()
    {
        var chart=Fixture(new() {ShowLargeTrades=true,ShowCrosshair=false});
        var window=new Window {Width=600,Height=420,SystemDecorations=SystemDecorations.None,Content=chart};
        try
        {
            window.Show();chart.Viewport.Zoom(.1,.1,10);chart.Viewport.Pan(-100,10);Render(chart);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var point=new Point(chart.Geometry.X(2),chart.Geometry.Y(100));window.MouseMove(point);
            Assert.Contains("85",ToolTip.GetTip(chart)?.ToString());
            var count=chart.Viewport.Count;window.MouseWheel(point,new Vector(0,-1),RawInputModifiers.None);Assert.True(chart.Viewport.Count>count);
            var start=chart.Viewport.Start;
            window.MouseDown(point,MouseButton.Left);window.MouseMove(new Point(point.X-40,point.Y),RawInputModifiers.LeftMouseButton);
            window.MouseUp(new Point(point.X-40,point.Y),MouseButton.Left);Assert.True(chart.Viewport.Start>=start);
            window.MouseMove(new Point(580,410));Assert.Null(ToolTip.GetTip(chart));
        }
        finally {window.Close();}
    }
}
