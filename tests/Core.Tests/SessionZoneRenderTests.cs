using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using BookDepthIQ.Core.Charts;
using BookDepthIQ.Dom.Charts;
using SkiaSharp;
using Xunit;
public class SessionZoneRenderTests
{
    static (CandleChartControl Chart,ChartPaneSettings Settings) Fixture(int width=600)
    {
        var cfg=new ChartPaneSettings {Frame=3};var chart=new CandleChartControl(cfg);
        var start=new DateTimeOffset(2026,10,15,0,0,0,TimeSpan.Zero);
        var bars=Enumerable.Range(0,100).Select(i=>new ChartBar(i,start.AddMinutes(i*3),new(2026,10,15),100,110,90,102,1,1,false)).ToArray();
        chart.Update(new(bars,.25,"LIVE",102,1));chart.Measure(new Size(width,420));chart.Arrange(new Rect(0,0,width,420));
        chart.SessionStyle=new() {Enabled=true};chart.SessionZones=new(new[]{new SessionZone("tokyo","TOKYO",start.AddMinutes(60),start.AddMinutes(120),start.AddMinutes(120),start.AddDays(1),108,92,"COMPLETE","#9A85EF")},Array.Empty<string>());
        return (chart,cfg);
    }
    static byte[] Render(CandleChartControl chart,int width=600,double scale=1)
    {
        using var bitmap=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)(width*scale),(int)(420*scale)),new Vector(96*scale,96*scale));bitmap.Render(chart);
        using var bytes=new MemoryStream();bitmap.Save(bytes);return bytes.ToArray();
    }
    [AvaloniaTheory] [InlineData(600,1)] [InlineData(600,1.5)] [InlineData(600,2)] [InlineData(320,2)]
    public void Zones_change_plot_only_without_moving_autoscale_or_axis_labels(int width,double scale)
    {
        var (chart,cfg)=Fixture(width);var before=Render(chart,width,scale);var geometry=chart.Geometry;
        cfg.ShowSessionZones=true;var after=Render(chart,width,scale);Assert.False(before.SequenceEqual(after));Assert.Equal(geometry,chart.Geometry);
        using var a=SKBitmap.Decode(before);using var b=SKBitmap.Decode(after);
        for(int x=(int)((geometry.PlotWidth+1)*scale);x<a.Width;x+=3) for(int y=0;y<a.Height;y+=3) Assert.Equal(a.GetPixel(x,y),b.GetPixel(x,y));
        cfg.Frame=1440;cfg.ShowSessionZones=false;var daily=Render(chart,width,scale);
        cfg.ShowSessionZones=true;Assert.True(daily.SequenceEqual(Render(chart,width,scale)));
    }
    [AvaloniaFact] public void Hover_reports_true_session_values_while_pointer_navigation_and_ticket_stay_readonly()
    {
        using var harness=new TicketHarness();var (chart,cfg)=Fixture();cfg.ShowSessionZones=true;cfg.ShowCrosshair=false;
        var window=new Window {Width=600,Height=420,SystemDecorations=SystemDecorations.None,Content=chart};window.Show();
        try
        {
            Render(chart);Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var point=new Point(chart.Geometry.X(30),chart.Geometry.Y(108));window.MouseMove(point);
            Assert.Contains("TOKYO",ToolTip.GetTip(chart)?.ToString());Assert.Contains("27.00",ToolTip.GetTip(chart)?.ToString());
            var count=chart.Viewport.Count;window.MouseWheel(point,new Vector(0,1),RawInputModifiers.None);Assert.True(chart.Viewport.Count<count);
            chart.Recenter();Assert.True(chart.Viewport.CenterOnLast);Assert.Empty(harness.Commands);
        }
        finally {window.Close();}
    }
}
