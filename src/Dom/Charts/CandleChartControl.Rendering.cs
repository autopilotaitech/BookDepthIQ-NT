using System.Globalization;
using Avalonia;
using Avalonia.Media;
using BookDepthIQ.Core.Charts;

namespace BookDepthIQ.Dom.Charts;

public sealed partial class CandleChartControl
{
    static readonly Typeface Mono=new(Pal.Mono),BoldMono=new(Pal.Mono,FontStyle.Normal,FontWeight.SemiBold);
    readonly Dictionary<(string,Color,double,bool,double),FormattedText> textCache=new();
    readonly Queue<(string,Color,double,bool,double)> textOrder=new();
    IReadOnlyList<ChartBar>? bucketSource;
    (int Start,int End,double Count,double Width) bucketKey;
    IReadOnlyList<ChartRenderBucket> renderBuckets=Array.Empty<ChartRenderBucket>();
    public override void Render(DrawingContext context)
    {
        base.Render(context);context.FillRectangle(Pal.B(Pal.Bg),new Rect(Bounds.Size));lastPriceTag=null;
        var bars=snapshot.Bars;var plot=Geometry;var w=plot.PlotWidth;var h=plot.PlotHeight;
        var divider=new Pen(Pal.B(Pal.Line),.7);
        context.FillRectangle(Pal.B(Pal.Panel),new Rect(w,0,ChartGeometry.AxisWidth,h));
        context.DrawLine(divider,new Point(w,0),new Point(w,h));context.DrawLine(divider,new Point(0,h),new Point(Bounds.Width,h));
        if(bars.Count==0) {Text(context,snapshot.Status,14,24,Pal.Muted);return;}
        var prices=ChartAxis.PriceTicks(plot.Low,plot.High,snapshot.TickSize,h);
        foreach(var ticks in prices)
        {
            var y=plot.Y(ticks);context.DrawLine(new Pen(Pal.B(Pal.RowLine),.8),new Point(0,y),new Point(w,y));
            Text(context,Price(ticks),w+8,Math.Clamp(y-6,2,Math.Max(2,h-13)),Pal.Muted);
        }
        int start=Math.Clamp((int)Math.Floor(Viewport.Start),0,bars.Count),end=Math.Clamp((int)Math.Ceiling(Viewport.Start+Viewport.Count),start,bars.Count);
        var timeStep=Math.Max(1,(int)Math.Ceiling(Viewport.Count/Math.Max(2,w/108)));
        var timeHighlight=CursorReadout is {} selected?TimeTag(selected,plot).Bounds:(Rect?)null;
        for(int i=(int)Math.Ceiling(start/(double)timeStep)*timeStep;i<end;i+=timeStep)
        {
            var x=plot.X(i);if(x<0||x>w-36) continue;
            context.DrawLine(new Pen(Pal.B(Pal.RowLine),.7),new Point(x,0),new Point(x,h));
            var label=settings.Frame>=1440?bars[i].TradingDate.ToString("MMM dd",CultureInfo.InvariantCulture):bars[i].Time.ToLocalTime().ToString(settings.Frame>=60?"MM/dd HH:mm":"HH:mm",CultureInfo.InvariantCulture);
            var ft=Formatted(label,Pal.Muted);var origin=new Point(Math.Clamp(x-ft.Width/2,3,Math.Max(3,w-ft.Width-3)),h+8);
            if(timeHighlight==null || !timeHighlight.Value.Intersects(new Rect(origin,new Size(ft.Width,ft.Height)))) context.DrawText(ft,origin);
        }
        // Keep axis glyphs outside/before the nested plot clip: otherwise high-DPI rendering can cull them.
        DrawAxisTags(context,plot);
        using(context.PushClip(new Rect(0,0,w,h)))
        {
            var key=(start,end,Viewport.Count,w);
            if(!ReferenceEquals(bucketSource,bars)||bucketKey!=key) {renderBuckets=ChartRenderBuckets.Build(bars,start,end,Viewport.Count,w);bucketSource=bars;bucketKey=key;}
            var bidPen=new Pen(Pal.B(Pal.Bid),1);var askPen=new Pen(Pal.B(Pal.Ask),1);
            long maxVolume=1;foreach(var bucket in renderBuckets) maxVolume=Math.Max(maxVolume,bucket.Volume);
            foreach(var b in renderBuckets)
            {
                var up=b.Close>=b.Open;var brush=Pal.B(up?Pal.Bid:Pal.Ask);var x=(plot.X(b.First)+plot.X(b.Last))/2;
                var candleWidth=Math.Clamp(w/Viewport.Count*(b.Last-b.First+1)*.68,1,30);
                if(settings.Volume)
                {
                    using(context.PushOpacity(.45)) context.FillRectangle(brush,new Rect(x-candleWidth/2,h-b.Volume/(double)maxVolume*h*.16,candleWidth,b.Volume/(double)maxVolume*h*.16));
                }
                context.DrawLine(up?bidPen:askPen,new Point(x,plot.Y(b.High)),new Point(x,plot.Y(b.Low)));
                context.FillRectangle(brush,new Rect(x-candleWidth/2,Math.Min(plot.Y(b.Open),plot.Y(b.Close)),candleWidth,Math.Max(1,Math.Abs(plot.Y(b.Close)-plot.Y(b.Open)))));
            }
            DrawStudy(context,sma,Pal.Accent,start,end,plot);DrawStudy(context,ema,Pal.Warn,start,end,plot);
            if(settings.PaxActive && end>start)
            {
                foreach(var level in OpeningRanges)
                {
                    if(level.End<bars[start].Time || level.Start>bars[end-1].Time) continue;
                    int a=LowerBound(bars,level.Start),b=LowerBound(bars,level.End);var y=plot.Y(level.Ticks);
                    if(y<0||y>h) continue;
                    context.DrawLine(new Pen(Pal.B(level.Upper?Pal.Bid:Pal.Ask),1,level.Label.StartsWith("OR EXT",StringComparison.Ordinal)?DashStyle.Dash:null),new Point(Math.Max(0,plot.X(a)),y),new Point(Math.Min(w,plot.X(b)),y));
                }
            }
            if(settings.ShowLastPrice && DisplayPriceTicks is {} price)
            {
                var color=PriceColor();var y=plot.Y(price);
                if(y>=0&&y<=h) context.DrawLine(new Pen(Pal.B(color),1),new Point(0,y),new Point(w,y));
            }
            if(settings.ShowOrders) DrawTradeLines(context,w,h,plot.Y);
            if(CursorReadout is {} cursor)
            {
                var cross=new Pen(Pal.B(Pal.Muted),.8,DashStyle.Dash);
                context.DrawLine(cross,new Point(cursor.X,0),new Point(cursor.X,h));
                if(cursor.Y>=0&&cursor.Y<=h) context.DrawLine(cross,new Point(0,cursor.Y),new Point(w,cursor.Y));
                DrawReadout(context,cursor,w);
            }
        }
    }
    void DrawAxisTags(DrawingContext context,ChartGeometry plot)
    {
        var w=plot.PlotWidth;var h=plot.PlotHeight;
        if(settings.ShowLastPrice && DisplayPriceTicks is {} last)
        {
            var color=PriceColor();var y=plot.Y(last);var text=(y<0?"↑ ":y>h?"↓ ":"")+Price(last);
            lastPriceTag=AxisTag(context,text,y,plot,color,snapshot.Status=="LIVE"?Pal.Bg:Pal.Text,snapshot.Status=="LIVE");
        }
        if(CursorReadout is {} hit)
        {
            AxisTag(context,(hit.Y<0?"↑ ":hit.Y>h?"↓ ":"")+Price(hit.PriceTicks),hit.Y,plot,Pal.Text,Pal.Text,false);
            var tag=TimeTag(hit,plot);
            context.FillRectangle(Pal.B(Pal.Card),tag.Bounds);context.DrawRectangle(null,new Pen(Pal.B(Pal.Muted),.7),tag.Bounds);
            context.DrawText(tag.Text,new Point(tag.Bounds.X+8,h+8));
        }
    }
    (FormattedText Text,Rect Bounds) TimeTag(ChartCursorReadout hit,ChartGeometry plot)
    {
        var text=settings.Frame>=1440?hit.Bar.TradingDate.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture):hit.Time.ToLocalTime().ToString("MMM dd · HH:mm",CultureInfo.InvariantCulture);
        var ft=Formatted(text,Pal.Text);var width=Math.Min(plot.PlotWidth,ft.Width+16);var x=Math.Clamp(hit.X-width/2,0,Math.Max(0,plot.PlotWidth-width));
        return (ft,new Rect(x,plot.PlotHeight+1,width,26));
    }
    Color PriceColor()=>snapshot.Status!="LIVE"?Pal.Muted:DisplayPriceTicks>=snapshot.Bars[^1].Open?Pal.Bid:Pal.Ask;
    Rect AxisTag(DrawingContext context,string text,double y,ChartGeometry plot,Color border,Color foreground,bool solid)
    {
        var top=Math.Clamp(y-11,1,Math.Max(1,plot.PlotHeight-23));var rect=new Rect(plot.PlotWidth+1,top,ChartGeometry.AxisWidth-2,22);
        context.FillRectangle(Pal.B(solid?border:Pal.Card),rect);context.DrawRectangle(null,new Pen(Pal.B(border),1),rect);
        var ft=Formatted(text,foreground,11,true,ChartGeometry.AxisWidth-12);
        context.DrawText(ft,new Point(plot.PlotWidth+8,top+(22-ft.Height)/2));return rect;
    }
    void DrawReadout(DrawingContext context,ChartCursorReadout hit,double width)
    {
        var b=hit.Bar;var first=$"O {Price(b.Open)}  H {Price(b.High)}";var second=$"L {Price(b.Low)}  C {Price(b.Close)}  V {b.Volume:N0}";
        var all=Formatted(first+"  "+second,Pal.Text);
        bool compact=all.Width>width-20;var height=compact?36:20;
        using(context.PushOpacity(.94)) context.FillRectangle(Pal.B(Pal.Panel),new Rect(5,5,Math.Min(width-10,compact?Math.Max(Formatted(first,Pal.Text).Width,Formatted(second,Pal.Text).Width)+12:all.Width+12),height));
        if(compact) {Text(context,first,11,8,Pal.Text);Text(context,second,11,24,Pal.Text);}
        else context.DrawText(all,new Point(11,8));
    }
    void DrawStudy(DrawingContext context,double?[] values,Color color,int start,int end,ChartGeometry plot)
    {
        var pen=new Pen(Pal.B(color),1.2);ChartStudySpan? previous=null;
        foreach(var span in ChartRenderBuckets.Study(values,renderBuckets))
        {
            var x=(plot.X(span.First)+plot.X(span.Last))/2;
            if(previous is {} old&&old.Last==span.First-1) context.DrawLine(pen,new Point((plot.X(old.First)+plot.X(old.Last))/2,plot.Y(old.Close)),new Point(x,plot.Y(span.Open)));
            if(span.High>span.Low) context.DrawLine(pen,new Point(x,plot.Y(span.High)),new Point(x,plot.Y(span.Low)));
            previous=span;
        }
    }
    FormattedText Formatted(string text,Color color,double size=10,bool bold=false,double maxWidth=double.PositiveInfinity)
    {
        var key=(text,color,size,bold,maxWidth);
        if(textCache.TryGetValue(key,out var cached)) return cached;
        if(textCache.Count>=256) textCache.Remove(textOrder.Dequeue());
        var value=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,bold?BoldMono:Mono,size,Pal.B(color)) {MaxLineCount=1};
        if(double.IsFinite(maxWidth)) value.MaxTextWidth=maxWidth;
        textCache.Add(key,value);textOrder.Enqueue(key);return value;
    }
    void Text(DrawingContext context,string text,double x,double y,Color color)=>context.DrawText(Formatted(text,color),new Point(x,y));
}
