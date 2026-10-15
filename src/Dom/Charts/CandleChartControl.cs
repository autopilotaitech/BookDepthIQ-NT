using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BookDepthIQ.Core.Charts;

namespace BookDepthIQ.Dom.Charts;

public sealed class CandleChartControl : Control
{
    ChartSnapshot snapshot=new(Array.Empty<ChartBar>(),.25,"OFFLINE",null,0);
    readonly ChartPaneSettings settings;
    public IReadOnlyList<ChartOrLine> OpeningRanges {get;set;}=Array.Empty<ChartOrLine>();
    public IReadOnlyList<ChartTradeLine> TradeLines {get;set;}=Array.Empty<ChartTradeLine>();
    double?[] sma=Array.Empty<double?>(),ema=Array.Empty<double?>();
    IReadOnlyList<ChartBar>? studyBars;
    (bool Sma,bool Ema,int Period) studySettings;
    public ChartViewport Viewport {get;}=new();
    Point? drag,crosshair;
    bool priceDrag;
    (double Low,double High) range;
    public CandleChartControl(ChartPaneSettings settings)
    {
        this.settings=settings;Focusable=true;ClipToBounds=true;
        PointerWheelChanged+=(_,e)=> {
            var p=e.GetPosition(this);var factor=Math.Pow(.85,e.Delta.Y);
            if(p.X>=PlotWidth)
            {
                var at=range.High-(range.High-range.Low)*Math.Clamp(p.Y/PlotHeight,0,1);
                Viewport.SetPriceRange(at+(range.Low-at)*factor,at+(range.High-at)*factor);
            }
            else Viewport.Zoom(factor,Math.Clamp(p.X/PlotWidth,0,1),snapshot.Bars.Count);
            InvalidateVisual();e.Handled=true;
        };
        PointerPressed+=(_,e)=> {
            if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus();var p=e.GetPosition(this);
            if(e.ClickCount==2) {if(p.X>=PlotWidth) Viewport.AutoScale();else Viewport.Reset(snapshot.Bars.Count);InvalidateVisual();return;}
            drag=p;priceDrag=p.X>=PlotWidth;e.Pointer.Capture(this);
        };
        PointerMoved+=(_,e)=> {
            var p=e.GetPosition(this);crosshair=p;
            if(drag is {} previous)
            {
                if(priceDrag) {var d=(p.Y-previous.Y)/PlotHeight*(range.High-range.Low);Viewport.SetPriceRange(range.Low+d,range.High+d);}
                else Viewport.Pan(-(p.X-previous.X)/PlotWidth*Viewport.Count,snapshot.Bars.Count);
                drag=p;
            }
            InvalidateVisual();
        };
        PointerReleased+=(_,e)=> {drag=null;e.Pointer.Capture(null);};
        PointerCaptureLost+=(_,_)=>drag=null;
        PointerExited+=(_,_)=> {crosshair=null;InvalidateVisual();};
        KeyDown+=(_,e)=> {
            if(e.Key==Key.Home) Viewport.Reset(snapshot.Bars.Count);
            else if(e.Key==Key.End) Viewport.Live(snapshot.Bars.Count);
            else if(e.Key==Key.Left) Viewport.Pan(-10,snapshot.Bars.Count);
            else if(e.Key==Key.Right) Viewport.Pan(10,snapshot.Bars.Count);
            else if(e.Key==Key.Add || e.Key==Key.OemPlus) Viewport.Zoom(.8,.5,snapshot.Bars.Count);
            else if(e.Key==Key.Subtract || e.Key==Key.OemMinus) Viewport.Zoom(1.25,.5,snapshot.Bars.Count);
            else return;
            e.Handled=true;InvalidateVisual();
        };
    }
    double PlotWidth=>Math.Max(1,Bounds.Width-76);
    double PlotHeight=>Math.Max(1,Bounds.Height-26);
    public void Update(ChartSnapshot value)
    {
        if(snapshot.Bars.Count>0&&value.Bars.Count>0) Viewport.Evicted(Math.Max(0,value.Bars[0].Index-snapshot.Bars[0].Index),value.Bars.Count);
        snapshot=value;Viewport.Update(value.Bars.Count);
        var preferences=(settings.Sma,settings.Ema,settings.Period);
        if(!ReferenceEquals(studyBars,value.Bars)||studySettings!=preferences)
        {
            var closes=settings.Sma||settings.Ema?value.Bars.Select(b=>(double)b.Close).ToArray():Array.Empty<double>();
            sma=settings.Sma?ChartStudies.Sma(closes,settings.Period):Array.Empty<double?>();ema=settings.Ema?ChartStudies.Ema(closes,settings.Period):Array.Empty<double?>();studyBars=value.Bars;studySettings=preferences;
        }
        InvalidateVisual();
    }
    public void Clear() {snapshot=new(Array.Empty<ChartBar>(),.25,"LOADING",null,0);Viewport.Reset(0);InvalidateVisual();}
    public override void Render(DrawingContext context)
    {
        base.Render(context);context.FillRectangle(Pal.B(Pal.Bg),Bounds.WithX(0).WithY(0));
        var w=PlotWidth;var h=PlotHeight;var bars=snapshot.Bars;
        int start=Math.Clamp((int)Math.Floor(Viewport.Start),0,bars.Count),end=Math.Clamp((int)Math.Ceiling(Viewport.Start+Viewport.Count),start,bars.Count);
        var visible=bars.Skip(start).Take(end-start).ToArray();range=Viewport.PriceRange(visible);
        double Y(double ticks)=>h-(ticks-range.Low)/(range.High-range.Low)*h;
        double X(int i)=>(i-Viewport.Start+.5)/Viewport.Count*w;
        var line=new Pen(Pal.B(Pal.Line),.6);
        for(int n=0;n<=5;n++)
        {
            var y=h*n/5;context.DrawLine(line,new Point(0,y),new Point(w,y));
            Text(context,((range.High-(range.High-range.Low)*n/5)*snapshot.TickSize).ToString("0.00",CultureInfo.InvariantCulture),w+5,Math.Clamp(y-6,1,Math.Max(1,h-12)),Pal.Muted);
        }
        using(context.PushClip(new Rect(0,0,w,h)))
        {
            var candleWidth=Math.Clamp(w/Viewport.Count*.65,1,25);
            var maxVolume=visible.Length>0?Math.Max(1,visible.Max(b=>b.Volume)):1;
            for(int i=start;i<end;i++)
            {
                var b=bars[i];var brush=Pal.B(b.Close>=b.Open?Pal.Bid:Pal.Ask);var x=X(i);
                if(settings.Volume) context.FillRectangle(brush,new Rect(x-candleWidth/2,h-b.Volume/(double)maxVolume*h*.16,candleWidth,b.Volume/(double)maxVolume*h*.16));
                context.DrawLine(new Pen(brush,1),new Point(x,Y(b.High)),new Point(x,Y(b.Low)));
                context.FillRectangle(brush,new Rect(x-candleWidth/2,Math.Min(Y(b.Open),Y(b.Close)),candleWidth,Math.Max(1,Math.Abs(Y(b.Close)-Y(b.Open)))));
            }
            void Study(double?[] values,Color color)
            {
                var pen=new Pen(Pal.B(color),1.2);
                for(int i=Math.Max(1,start);i<end&&i<values.Length;i++) if(values[i-1] is {} a && values[i] is {} b) context.DrawLine(pen,new Point(X(i-1),Y(a)),new Point(X(i),Y(b)));
            }
            Study(sma,Pal.Accent);Study(ema,Pal.Warn);
            if(settings.PaxActive && visible.Length>0)
            {
                foreach(var level in OpeningRanges)
                {
                    if(level.End<visible[0].Time || level.Start>visible[^1].Time) continue;
                    int a=LowerBound(bars,level.Start),b=LowerBound(bars,level.End);var y=Y(level.Ticks);
                    if(y<0||y>h) continue;
                    context.DrawLine(new Pen(Pal.B(level.Upper?Pal.Bid:Pal.Ask),1,level.Label.StartsWith("OR EXT")?DashStyle.Dash:null),new Point(Math.Max(0,X(a)),y),new Point(Math.Min(w,X(b)),y));
                }
            }
            if(bars.Count>0)
            {
                if(settings.ShowOrders) DrawTradeLines(context,w,h,Y);
                var close=bars[^1].Close;var y=Y(close);
                if(y>=0 && y<=h) context.DrawLine(new Pen(Pal.B(Pal.Muted),.7,DashStyle.Dash),new Point(0,y),new Point(w,y));
            }
            if(crosshair is {} p && p.X<w && p.Y<h)
            {
                var pen=new Pen(Pal.B(Pal.Muted),.7,DashStyle.Dash);context.DrawLine(pen,new Point(p.X,0),new Point(p.X,h));context.DrawLine(pen,new Point(0,p.Y),new Point(w,p.Y));
            }
        }
        context.DrawLine(line,new Point(w,0),new Point(w,h));
        if(bars.Count==0) Text(context,snapshot.Status,12,24,Pal.Muted);
        else
        {
            for(int n=0;n<4;n++)
            {
                int i=Math.Clamp(start+(end-start)*n/4,0,bars.Count-1);
                Text(context,settings.Frame>=1440?bars[i].TradingDate.ToString("MMM dd"):bars[i].Time.ToLocalTime().ToString("HH:mm"),X(i),h+6,Pal.Muted);
            }
            var last=snapshot.LastPrice??bars[^1].Close;var y=Y(last);
            if(y>=0 && y<=h) {context.FillRectangle(Pal.B(Pal.Panel),new Rect(w,y-8,76,17));Text(context,(last*snapshot.TickSize).ToString("0.00",CultureInfo.InvariantCulture),w+4,y-6,Pal.Text);}
            if(crosshair is {} p)
            {
                int i=(int)(Viewport.Start+p.X/w*Viewport.Count);
                if(i>=0 && i<bars.Count) {var b=bars[i];Text(context,$"{b.Time.ToLocalTime():MM/dd HH:mm}  O {b.Open*snapshot.TickSize:0.00}  H {b.High*snapshot.TickSize:0.00}  L {b.Low*snapshot.TickSize:0.00}  C {b.Close*snapshot.TickSize:0.00}",8,7,Pal.Text);}
            }
        }
    }
    void DrawTradeLines(DrawingContext context,double w,double h,Func<double,double> yAt)
    {
        if(!double.IsFinite(snapshot.TickSize)||snapshot.TickSize<=0) return;
        Color ColorFor(ChartTradeLine line)=>line.Kind switch {ChartTradeKind.Stop=>Pal.Neg,ChartTradeKind.Target=>Pal.Pos,ChartTradeKind.Position=>Pal.Accent,_=>line.Buy?Pal.Bid:Pal.Ask};
        var groups=TradeLines.Where(l=>double.IsFinite(l.Price)&&l.Quantity>0).GroupBy(l=>(l.Price,l.Kind,l.Buy,l.Label))
            .Select(g=>new {Line=g.First(),Quantity=g.Sum(l=>(long)l.Quantity),Count=g.Count(),Y=yAt(g.Key.Price/snapshot.TickSize)})
            .OrderBy(g=>g.Y).ToArray();
        foreach(var group in groups)
        {
            if(group.Y>=0&&group.Y<=h) context.DrawLine(new Pen(Pal.B(ColorFor(group.Line)),1,DashStyle.Dash),new Point(0,group.Y),new Point(w,group.Y));
        }
        int capacity=Math.Max(1,(int)(h/19)-1);var labels=groups.Take(capacity).ToArray();var positions=new double[labels.Length];
        for(int i=0;i<labels.Length;i++) positions[i]=Math.Max(Math.Clamp(labels[i].Y,8,Math.Max(8,h-9)),i==0?8:positions[i-1]+19);
        for(int i=positions.Length-1;i>=0;i--) positions[i]=Math.Min(positions[i],i==positions.Length-1?h-9:positions[i+1]-19);
        for(int i=0;i<labels.Length;i++)
        {
            var group=labels[i];var color=ColorFor(group.Line);var arrow=group.Y<0?"↑ ":group.Y>h?"↓ ":"";
            var text=$"{arrow}{group.Line.Label} ×{group.Quantity} @ {group.Line.Price.ToString("0.00########",CultureInfo.InvariantCulture)}"+(group.Count>1?$" ({group.Count})":"");
            var ft=new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(Pal.Mono),10,Pal.B(color)) {MaxTextWidth=Math.Max(1,w-12),MaxLineCount=1};
            var x=Math.Max(4,w-ft.Width-8);var y=Math.Clamp(positions[i]-8,0,Math.Max(0,h-17));
            context.FillRectangle(Pal.B(Pal.Panel),new Rect(x-3,y,Math.Min(w-x+3,ft.Width+8),17));
            context.DrawRectangle(null,new Pen(Pal.B(color),.7),new Rect(x-3,y,Math.Min(w-x+3,ft.Width+8),17));context.DrawText(ft,new Point(x,y+2));
        }
        if(groups.Length>labels.Length) Text(context,$"+{groups.Length-labels.Length} order levels",8,h-15,Pal.Muted);
    }
    static void Text(DrawingContext context,string text,double x,double y,Color color)=>context.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface(Pal.Mono),10,Pal.B(color)),new Point(x,y));
    static int LowerBound(IReadOnlyList<ChartBar> bars,DateTimeOffset time)
    {int a=0,b=bars.Count;while(a<b) {int mid=(a+b)/2;if(bars[mid].Time<time) a=mid+1;else b=mid;}return a;}
}
