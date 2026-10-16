using Avalonia;
using Avalonia.Media;
using BookDepthIQ.Core.Charts;
namespace BookDepthIQ.Dom.Charts;
public sealed partial class CandleChartControl
{
    bool SessionsShown=>settings.ShowSessionZones&&SessionStyle.Enabled&&settings.Frame<1440;
    public string SessionCoverageSummary=>settings.Frame>=1440?"Day charts omit intraday session zones.":
        string.Join("\n",SessionZones.Notes.Concat(SessionZones.Zones.Select(z=>$"{z.Name}: {z.State}")).Distinct().DefaultIfEmpty("Waiting for native 30-second session history."));
    bool SessionX(DateTimeOffset time,ChartGeometry plot,out double x)
    {
        x=0;var bars=snapshot.Bars;if(bars.Count==0||settings.Frame<=0||settings.Frame>=1440) return false;
        var interval=TimeSpan.FromMinutes(settings.Frame);
        if(time<=bars[0].Time-interval) {x=plot.X(-.5);return true;}
        if(time>=bars[^1].Time) {x=plot.X(bars.Count-.5);return true;}
        int index=LowerBound(bars,time);if(index>=bars.Count) return false;
        var from=bars[index].Time-interval;
        if(index>0) from=bars[index-1].Time>from?bars[index-1].Time:from;
        if(time<from) return false; // Never interpolate across an unproved market/session gap.
        var fraction=(time-from).TotalMilliseconds/(bars[index].Time-from).TotalMilliseconds;
        x=plot.X(index-.5+fraction);return double.IsFinite(x);
    }
    bool SessionRect(SessionZone zone,ChartGeometry plot,out Rect rect)
    {
        rect=default;if(!SessionX(zone.Start,plot,out var left)||!SessionX(zone.Progress,plot,out var right)||right<=left) return false;
        var top=plot.Y(zone.High);var bottom=plot.Y(zone.Low);rect=new(left,Math.Min(top,bottom),right-left,Math.Max(1,Math.Abs(bottom-top)));return true;
    }
    bool SessionExtension(SessionZone zone,ChartGeometry plot,out double start,out double end)
    {
        start=end=0;
        return SessionStyle.ExtendLevels&&zone.Progress>=zone.End&&SessionX(zone.End,plot,out start)&&SessionX(zone.ExtendUntil,plot,out end)&&end>start;
    }
    void DrawSessions(DrawingContext context,ChartGeometry plot,bool labels)
    {
        if(!SessionsShown) return;var occupied=new List<Rect>();
        foreach(var zone in SessionZones.Zones.Take(27).Reverse())
        {
            var color=Color.TryParse(zone.Color,out var parsed)?parsed:Pal.Muted;var pen=new Pen(Pal.B(color),.9);
            if(!labels&&SessionExtension(zone,plot,out var extensionStart,out var extensionEnd))
            {
                var dashed=new Pen(new SolidColorBrush(color,.55),.8,DashStyle.Dash);
                context.DrawLine(dashed,new Point(extensionStart,plot.Y(zone.High)),new Point(extensionEnd,plot.Y(zone.High)));
                context.DrawLine(dashed,new Point(extensionStart,plot.Y(zone.Low)),new Point(extensionEnd,plot.Y(zone.Low)));
            }
            if(!SessionRect(zone,plot,out var rect)||rect.Right<0||rect.X>plot.PlotWidth) continue;
            if(!labels)
            {
                context.DrawRectangle(new SolidColorBrush(color,SessionStyle.Opacity),pen,rect);
                continue;
            }
            if(!SessionStyle.ShowLabels) continue;
            void Label(string text,double x,double y,bool centered)
            {
                var ft=Formatted(text,color,10);var box=new Rect(x,y-(centered?ft.Height/2:0),ft.Width+8,ft.Height+2);
                if(box.X<0||box.Right>plot.PlotWidth||box.Y<0||box.Bottom>plot.PlotHeight||occupied.Any(r=>r.Intersects(box))) return;
                occupied.Add(box);context.FillRectangle(Pal.B(Pal.Card),box);context.DrawRectangle(null,new Pen(Pal.B(color),.6),box);context.DrawText(ft,new Point(box.X+4,box.Y+1));
            }
            var prefix=zone.Id=="tokyo"?"TYO":zone.Id=="london"?"LDN":"NY";
            var high=$"{prefix} H {Price(zone.High)}";var low=$"{prefix} L {Price(zone.Low)}";
            var highWidth=Formatted(high,color).Width+8;var lowWidth=Formatted(low,color).Width+8;
            Label(high,Math.Max(0,Math.Min(rect.Right-highWidth,plot.PlotWidth-highWidth-2)),plot.Y(zone.High),true);
            Label(low,Math.Max(0,Math.Min(rect.Right-lowWidth,plot.PlotWidth-lowWidth-2)),plot.Y(zone.Low),true);
            var title=zone.Name+(zone.State=="COMPLETE"?"":" · "+zone.State);
            Label(title,Math.Max(2,rect.X+3),plot.Y(zone.High)-25,false);
        }
    }
    bool TrySessionTooltip(out string text)
    {
        text="";if(!SessionsShown||pointer is not {} point) return false;var plot=Geometry;
        foreach(var zone in SessionZones.Zones.Reverse())
        {
            bool inBox=SessionRect(zone,plot,out var rect)&&rect.Contains(point);
            bool onExtension=SessionExtension(zone,plot,out var start,out var end)&&point.X>=start&&point.X<=end&&
                (Math.Abs(point.Y-plot.Y(zone.High))<=4||Math.Abs(point.Y-plot.Y(zone.Low))<=4);
            if(!inBox&&!onExtension) continue;
            text=$"{zone.Name} · {zone.State}\nH {Price(zone.High)} · L {Price(zone.Low)}\nUTC {zone.Start:yyyy-MM-dd HH:mm} → {zone.End:yyyy-MM-dd HH:mm}";return true;
        }
        return false;
    }
}
