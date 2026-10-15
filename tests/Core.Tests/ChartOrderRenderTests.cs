using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using BookDepthIQ.Core.Charts;
using BookDepthIQ.Dom;
using BookDepthIQ.Dom.Charts;
using Xunit;

public class ChartOrderRenderTests
{
    [AvaloniaFact] public void Overlay_projection_never_sends_commands_through_real_ticket_harness()
    {
        using var h=new TicketHarness();h.Position(3);
        h.State.Orders.Add(new WorkingOrder("tp",h.State.Instrument,"Sell","Limit",3,1,30020,0,"PartFilled","","BDIQ TP1"));
        var lines=ChartOrderOverlay.Build(h.State,h.Config.Account,h.Config.Instrument);
        Assert.Contains(lines,l=>l.Id=="tp"&&l.Quantity==2);Assert.Empty(h.Commands);
    }
    static byte[] Render(CandleChartControl chart)
    {
        chart.Measure(new Size(600,420));chart.Arrange(new Rect(0,0,600,420));
        using var bmp=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(600,420));bmp.Render(chart);
        using var bytes=new MemoryStream();bmp.Save(bytes);return bytes.ToArray();
    }
    [AvaloniaFact] public void Trade_lines_render_and_toggle_off_without_affecting_candle_viewport()
    {
        var settings=new ChartPaneSettings();var chart=new CandleChartControl(settings);
        var bars=Enumerable.Range(0,180).Select(i=>new ChartBar(i,DateTimeOffset.UnixEpoch.AddMinutes(i),new(2026,10,15),120000,120040,119960,120010,10,1,false)).ToArray();
        chart.Update(new(bars,.25,"TEST",null,1));var before=Render(chart);var start=chart.Viewport.Start;
        chart.TradeLines=new[]{new ChartTradeLine("tp",30005,2,false,ChartTradeKind.Target,"TP SELL"),new ChartTradeLine("sl",29995,2,false,ChartTradeKind.Stop,"SL SELL"),new ChartTradeLine("avg",30000,3,true,ChartTradeKind.Position,"AVG LONG")};
        var visible=Render(chart);Assert.False(before.SequenceEqual(visible));Assert.Equal(start,chart.Viewport.Start);
        settings.ShowOrders=false;Assert.True(before.SequenceEqual(Render(chart)));
        if(Environment.GetEnvironmentVariable("BDIQ_ORDER_CHART_IMAGE") is {Length:>0} path) File.WriteAllBytes(path,visible);
    }
    [AvaloniaFact] public void Both_panes_receive_overlay_snapshot_and_clear_it_when_source_clears()
    {
        IReadOnlyList<ChartTradeLine> lines=new[]{new ChartTradeLine("entry",30000,1,true,ChartTradeKind.Entry,"BUY LIMIT")};
        using var workspace=new ChartWorkspace(new Border(),new ChartWorkspaceSettings(),new ChartCoordinator(),()=>{}) {TradingLinesProvider=()=>lines};
        workspace.RefreshTradingLines();var charts=workspace.GetVisualDescendants().OfType<CandleChartControl>().ToArray();Assert.Equal(2,charts.Length);Assert.All(charts,c=>Assert.Single(c.TradeLines));
        lines=Array.Empty<ChartTradeLine>();workspace.RefreshTradingLines();Assert.All(charts,c=>Assert.Empty(c.TradeLines));
    }
}
