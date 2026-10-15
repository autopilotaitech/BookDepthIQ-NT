using Avalonia;
using Avalonia.Controls;
using BookDepthIQ.Core.Charts;

namespace BookDepthIQ.Dom.Charts;

public sealed record ChartCursorLink(DateTimeOffset Time,long PriceTicks,DateOnly? TradingDate=null);
public sealed record ChartCursorReadout(int Index,DateTimeOffset Time,long PriceTicks,double X,double Y,ChartBar Bar);

/// <summary>Read-only native candle surface. Input changes the viewport, never orders.</summary>
public sealed partial class CandleChartControl : Control
{
    ChartSnapshot snapshot=new(Array.Empty<ChartBar>(),.25,"OFFLINE",null,0);
    readonly ChartPaneSettings settings;
    public IReadOnlyList<ChartOrLine> OpeningRanges {get;set;}=Array.Empty<ChartOrLine>();
    public IReadOnlyList<ChartTradeLine> TradeLines {get;set;}=Array.Empty<ChartTradeLine>();
    double?[] sma=Array.Empty<double?>(),ema=Array.Empty<double?>();
    IReadOnlyList<ChartBar>? studyBars;
    (bool Sma,bool Ema,int Period) studySettings;
    public ChartViewport Viewport {get;}=new();
    public event Action? ViewChanged;
    public event Action<ChartCursorLink?>? CursorChanged;
    Point? pointer;
    ChartCursorLink? linkedCursor,publishedCursor;
    public long? DisplayPriceTicks=>snapshot.LastPrice??(snapshot.Bars.Count>0?snapshot.Bars[^1].Close:null);
    public CandleChartControl(ChartPaneSettings settings)
    {
        this.settings=settings;Focusable=true;ClipToBounds=true;WireInput();
    }
    public ChartGeometry Geometry
    {
        get
        {
            int start=Math.Clamp((int)Math.Floor(Viewport.Start),0,snapshot.Bars.Count);
            int end=Math.Clamp((int)Math.Ceiling(Viewport.Start+Viewport.Count),start,snapshot.Bars.Count);
            var range=Viewport.PriceRange(snapshot.Bars.Skip(start).Take(end-start).ToArray(),DisplayPriceTicks);
            return new(Bounds.Width,Bounds.Height,Viewport.Start,Viewport.Count,range.Low,range.High);
        }
    }
    public ChartCursorReadout? CursorReadout
    {
        get
        {
            if(!settings.ShowCrosshair||snapshot.Bars.Count==0) return null;
            var plot=Geometry;int index;long ticks;double x;
            if(pointer is {} p && p.Y>=0 && p.Y<plot.PlotHeight)
            {
                index=plot.BarAt(p.X,snapshot.Bars.Count);if(index<0) return null;
                ticks=plot.TicksAt(p.Y);x=settings.SnapCrosshair?plot.X(index):p.X;
            }
            else if(linkedCursor is {} linked)
            {
                if(settings.Frame>=1440 && linked.TradingDate is {} date)
                {
                    index=-1;for(int i=0;i<snapshot.Bars.Count;i++) if(snapshot.Bars[i].TradingDate==date) {index=i;break;}
                    if(index<0) return null;
                }
                else index=LowerBound(snapshot.Bars,linked.Time);
                if(index>=snapshot.Bars.Count) return null;
                x=plot.X(index);if(x<0||x>=plot.PlotWidth) return null;ticks=linked.PriceTicks;
            }
            else return null;
            var bar=snapshot.Bars[index];return new(index,bar.Time,ticks,x,plot.Y(ticks),bar);
        }
    }
    public void SetCursor(Point? point)
    {
        var geometry=Geometry;
        pointer=point is {} p&&p.X>=0&&p.X<geometry.PlotWidth&&p.Y>=0&&p.Y<geometry.PlotHeight?p:null;
        linkedCursor=null;PublishCursor();InvalidateVisual();
    }
    public void SetLinkedCursor(ChartCursorLink? value) {linkedCursor=value;InvalidateVisual();}
    void PublishCursor()
    {
        var hit=CursorReadout;
        var value=pointer!=null&&hit!=null?new ChartCursorLink(hit.Time,hit.PriceTicks,hit.Bar.TradingDate):null;
        if(value==publishedCursor) return;publishedCursor=value;CursorChanged?.Invoke(value);
        ToolTip.SetTip(this,hit==null?null:$"{hit.Time.ToLocalTime():yyyy-MM-dd HH:mm} · O {Price(hit.Bar.Open)} H {Price(hit.Bar.High)} L {Price(hit.Bar.Low)} C {Price(hit.Bar.Close)} · Volume {hit.Bar.Volume:N0}");
    }
    public void Update(ChartSnapshot value)
    {
        if(snapshot.Bars.Count>0&&value.Bars.Count>0) Viewport.Evicted(Math.Max(0,value.Bars[0].Index-snapshot.Bars[0].Index),value.Bars.Count);
        snapshot=value;Viewport.Update(value.Bars.Count);
        var preferences=(settings.Sma,settings.Ema,settings.Period);
        if(!ReferenceEquals(studyBars,value.Bars)||studySettings!=preferences)
        {
            var closes=settings.Sma||settings.Ema?value.Bars.Select(b=>(double)b.Close).ToArray():Array.Empty<double>();
            sma=settings.Sma?ChartStudies.Sma(closes,settings.Period):Array.Empty<double?>();
            ema=settings.Ema?ChartStudies.Ema(closes,settings.Period):Array.Empty<double?>();studyBars=value.Bars;studySettings=preferences;
        }
        PublishCursor();ViewChanged?.Invoke();InvalidateVisual();
    }
    public void Clear()
    {
        snapshot=new(Array.Empty<ChartBar>(),.25,"LOADING",null,0);linkedCursor=null;pointer=null;Viewport.Reset(0);
        PublishCursor();ViewChanged?.Invoke();InvalidateVisual();
    }
    void Navigate(Action action) {action();PublishCursor();ViewChanged?.Invoke();InvalidateVisual();}
    public void Recenter()=>Navigate(()=>Viewport.Recenter(snapshot.Bars.Count));
    public void GoLive()=>Navigate(()=>Viewport.Live(snapshot.Bars.Count));
    public void ResetView()=>Navigate(()=>Viewport.Reset(snapshot.Bars.Count));
    public void AutoScale()=>Navigate(Viewport.AutoScale);
    string Price(double ticks)=>ChartAxis.Format(ticks*snapshot.TickSize,snapshot.TickSize);
    static int LowerBound(IReadOnlyList<ChartBar> bars,DateTimeOffset time)
    {int a=0,b=bars.Count;while(a<b) {int mid=(a+b)/2;if(bars[mid].Time<time) a=mid+1;else b=mid;}return a;}
}
