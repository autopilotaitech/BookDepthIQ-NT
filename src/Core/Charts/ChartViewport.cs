namespace BookDepthIQ.Core.Charts;

public sealed class ChartViewport
{
    public double Start {get;private set;}
    public double Count {get;private set;}=120;
    public bool Follow {get;private set;}=true;
    public bool Auto {get;private set;}=true;
    public bool CenterOnLast {get;private set;}
    double low,high;
    public void Reset(int total) {Count=120; Follow=true; Auto=true;CenterOnLast=false; Update(total);}
    public void Update(int total) {if(Follow) Start=Math.Max(0,total-Count+8); else Start=Math.Clamp(Start,0,Math.Max(0,total-1));}
    public void Live(int total) {Follow=true; Update(total);}
    public void AutoScale() {Auto=true;CenterOnLast=false;}
    public void Recenter(int total) {Follow=true;Auto=true;CenterOnLast=true;Update(total);}
    public void Zoom(double factor,double pointer,int total)
    {
        if(!double.IsFinite(factor)||factor<=0) return;
        pointer=Math.Clamp(pointer,0,1); var anchor=Start+Count*pointer;
        Count=Math.Clamp(Count*factor,10,10000); Start=Math.Clamp(anchor-Count*pointer,0,Math.Max(0,total-1)); Follow=false;CenterOnLast=false;
    }
    public void Pan(double delta,int total) {Follow=false;CenterOnLast=false; Start=Math.Clamp(Start+delta,0,Math.Max(0,total-1));}
    public void Evicted(int count,int total) {if(!Follow) Start=Math.Clamp(Start-Math.Max(0,count),0,Math.Max(0,total-1));else Update(total);}
    public void SetPriceRange(double min,double max) {if(double.IsFinite(min)&&double.IsFinite(max)&&max>min) {low=min;high=max;Auto=false;CenterOnLast=false;}}
    public (double Low,double High) PriceRange(IReadOnlyList<ChartBar> visible,double? lastPrice=null)
    {
        if(!Auto) return (low,high);
        if(visible.Count==0) return (0,4);
        double a=visible.Min(b=>b.Low),b=visible.Max(b=>b.High); var padding=Math.Max(2,(b-a)*.08);
        if(CenterOnLast)
        {
            var center=lastPrice is {} value&&double.IsFinite(value)?value:visible[^1].Close;
            var radius=Math.Max(2,Math.Max(Math.Abs(center-a),Math.Abs(b-center))+padding);
            return (center-radius,center+radius);
        }
        return (a-padding,b+padding);
    }
}
