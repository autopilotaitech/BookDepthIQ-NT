namespace BookDepthIQ.Core.Charts;

/// <summary>One geometry for drawing and pointer hit-testing; axes are never mistaken for candles.</summary>
public readonly record struct ChartGeometry(double Width,double Height,double Start,double Count,double Low,double High)
{
    public const double AxisWidth=92;
    public const double TimeAxisHeight=28;
    public double PlotWidth=>Math.Max(1,Width-AxisWidth);
    public double PlotHeight=>Math.Max(1,Height-TimeAxisHeight);
    public double X(double index)=>(index-Start+.5)/Math.Max(1,Count)*PlotWidth;
    public double Y(double ticks)=>PlotHeight-(ticks-Low)/Math.Max(1e-8,High-Low)*PlotHeight;
    public long TicksAt(double y)=>(long)Math.Round(High-Math.Clamp(y/PlotHeight,0,1)*(High-Low));
    public int BarAt(double x,int total)
    {
        if(!double.IsFinite(x)||x<0||x>=PlotWidth) return -1;
        var index=Math.Floor(Start+x/PlotWidth*Count);
        return index>=0&&index<total?(int)index:-1;
    }
}
