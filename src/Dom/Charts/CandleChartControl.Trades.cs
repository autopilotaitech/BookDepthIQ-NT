using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using BookDepthIQ.Core.Charts;
namespace BookDepthIQ.Dom.Charts;

public sealed partial class CandleChartControl
{
    ChartTradeSnapshot? projectedTrades;IReadOnlyList<ChartBar>? projectedBars;
    (double Start,double Count,double Low,double High,int Frame,LargeTradeProfile Profile) projectedKey;
    ChartBubbleProjection projected=new(Array.Empty<ChartBubble>(),0);
    ChartBubbleProjection Bubbles(ChartGeometry plot)
    {
        if(!settings.ShowLargeTrades||snapshot.Bars.Count==0) return new(Array.Empty<ChartBubble>(),0);
        var key=(Viewport.Start,Viewport.Count,plot.Low,plot.High,settings.Frame,TradeProfile);
        if(ReferenceEquals(projectedTrades,CapturedTrades)&&ReferenceEquals(projectedBars,snapshot.Bars)&&projectedKey==key) return projected;
        var first=Math.Clamp((int)Math.Floor(Viewport.Start),0,snapshot.Bars.Count-1);
        var last=Math.Clamp((int)Math.Ceiling(Viewport.Start+Viewport.Count),first,snapshot.Bars.Count-1);
        projected=ChartTradeProjection.Build(CapturedTrades.Prints,snapshot.Bars,settings.Frame,
            snapshot.Bars[Math.Max(0,first-1)].Time.ToUnixTimeMilliseconds(),snapshot.Bars[last].Time.ToUnixTimeMilliseconds(),
            TradeProfile.MinContracts,TradeProfile.MinDiameter,TradeProfile.MaxDiameter,plot.Low,plot.High);
        projectedTrades=CapturedTrades;projectedBars=snapshot.Bars;projectedKey=key;
        return projected;
    }
    public string TradeCoverageSummary
    {
        get
        {
            var data=CapturedTrades;var first=data.Coverage.FirstOrDefault();var last=data.Coverage.LastOrDefault();
            var coverage=first==null?"Waiting for live prints":$"Captured since {DateTimeOffset.FromUnixTimeMilliseconds(first.StartMs).ToLocalTime():HH:mm:ss} · {data.Coverage.Count} coverage segments · {(last!.EndMs==null?"active":"paused")}";
            return $"{coverage}\n{data.Prints.Count:N0} retained · {data.Evicted:N0} evicted · {Bubbles(Geometry).Omitted:N0} omitted by visible cap";
        }
    }
    void DrawBubbles(DrawingContext context,ChartGeometry plot)
    {
        foreach(var bubble in Bubbles(plot).Markers)
        {
            var x=plot.X(bubble.BarPosition);var y=plot.Y(bubble.Print.Tick);if(x<0||x>=plot.PlotWidth) continue;
            var color=TradeProfile.ColorFor(bubble.Print.Side);var radius=bubble.Diameter/2;
            var fill=new SolidColorBrush(color,TradeProfile.Opacity);
            context.DrawEllipse(fill,new Pen(Pal.B(color),1.2),new Point(x,y),radius,radius);
            if(TradeProfile.Labels==TradeQuantityLabels.Off||(TradeProfile.Labels==TradeQuantityLabels.Large&&bubble.Diameter<24)) continue;
            var text=bubble.Print.Quantity.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var size=Math.Clamp(bubble.Diameter*.33,8,13);var ft=Formatted(text,Pal.Text,size,true);
            if(ft.Width>bubble.Diameter-4) ft=Formatted(text,Pal.Text,Math.Max(6,size*(bubble.Diameter-4)/ft.Width),true);
            // Dark underlay keeps quantities legible over candles and user-selected colors.
            using(context.PushOpacity(.8)) context.FillRectangle(Pal.B(Pal.Bg),new Rect(x-ft.Width/2-1,y-ft.Height/2,ft.Width+2,ft.Height));
            context.DrawText(ft,new Point(x-ft.Width/2,y-ft.Height/2));
        }
    }
    void RefreshTradeTooltip()
    {
        if(pointer is {} point&&settings.ShowLargeTrades)
        {
            var plot=Geometry;
            var hits=Bubbles(plot).Markers.Where(m=> {
                var dx=point.X-plot.X(m.BarPosition);var dy=point.Y-plot.Y(m.Print.Tick);return dx*dx+dy*dy<=m.Diameter*m.Diameter/4;
            }).ToArray();
            if(hits.Length>0)
            {
                var print=hits[^1].Print;var side=print.Side=='b'?"Ask-side":print.Side=='s'?"Bid-side":"Unknown side";
                ToolTip.SetTip(this,$"{print.Quantity:N0} contracts · {side} executed print\n{Price(print.Tick)} · {DateTimeOffset.FromUnixTimeMilliseconds(print.TimeMs).ToLocalTime():yyyy-MM-dd HH:mm:ss.fff}\n{hits.Length} overlapping marker(s)\n{TradeCoverageSummary}");return;
            }
        }
        var hit=CursorReadout;
        ToolTip.SetTip(this,hit==null?null:$"{hit.Time.ToLocalTime():yyyy-MM-dd HH:mm} · O {Price(hit.Bar.Open)} H {Price(hit.Bar.High)} L {Price(hit.Bar.Low)} C {Price(hit.Bar.Close)} · Volume {hit.Bar.Volume:N0}");
    }
}
