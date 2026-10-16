namespace BookDepthIQ.Core.Charts;

public sealed record ChartBubble(ChartPrint Print,double BarPosition,double Diameter);
public sealed record ChartBubbleProjection(IReadOnlyList<ChartBubble> Markers,int Omitted);
public static class ChartTradeProjection
{
    public static ChartBubbleProjection LimitDensity(ChartBubbleProjection source,ChartGeometry plot,double maxDiameter)
    {
        var cell=Math.Max(24,maxDiameter);var budget=Math.Clamp((int)(plot.PlotWidth*plot.PlotHeight/(cell*cell)),1,500);
        var occupied=new HashSet<(int,int)>();var selected=new List<ChartBubble>();int eligible=0;
        foreach(var marker in source.Markers.OrderByDescending(m=>m.Print.Quantity).ThenBy(m=>m.Print.Id))
        {
            var x=plot.X(marker.BarPosition);var y=plot.Y(marker.Print.Tick);
            if(x<0||x>=plot.PlotWidth||y<0||y>plot.PlotHeight) continue;eligible++;
            if(selected.Count>=budget||!occupied.Add(((int)(x/cell),(int)(y/cell)))) continue;
            selected.Add(marker);
        }
        return new(Array.AsReadOnly(selected.OrderBy(m=>m.Print.TimeMs).ThenBy(m=>m.Print.Id).ToArray()),source.Omitted+eligible-selected.Count);
    }
    public static ChartBubbleProjection Build(IReadOnlyList<ChartPrint> prints,IReadOnlyList<ChartBar> bars,int frame,
        long fromMs,long toMs,long minContracts,double minDiameter,double maxDiameter,double lowTicks=double.NegativeInfinity,double highTicks=double.PositiveInfinity)
    {
        if(frame<=0||frame>=1440||bars.Count<2) return new(Array.Empty<ChartBubble>(),0);
        var candidates=new PriorityQueue<(ChartPrint Print,double Position,double Diameter),(long Quantity,long ReverseId)>();
        int eligible=0;long duration=frame*60000L;
        foreach(var p in prints)
        {
            if(p.Quantity<minContracts||p.TimeMs<fromMs||p.TimeMs>toMs||p.Tick<lowTicks||p.Tick>highTicks) continue;
            int lo=0,hi=bars.Count;
            while(lo<hi) {int mid=(lo+hi)/2;if(bars[mid].Time.ToUnixTimeMilliseconds()<p.TimeMs) lo=mid+1;else hi=mid;}
            if(lo==0||lo>=bars.Count) continue;
            var end=bars[lo].Time.ToUnixTimeMilliseconds();var start=bars[lo-1].Time.ToUnixTimeMilliseconds();
            // Without session-start metadata, do not guess the first interval after a gap.
            if(end<=start||end-start>duration||p.TimeMs<=start) continue;
            var position=lo-.4+.8*(p.TimeMs-start)/(double)(end-start);
            var diameter=Math.Clamp(minDiameter*Math.Sqrt(p.Quantity/(double)Math.Max(1,minContracts)),minDiameter,maxDiameter);
            eligible++;var priority=(p.Quantity,-p.Id);
            if(candidates.Count<500) candidates.Enqueue((p,position,diameter),priority);
            else if(candidates.TryPeek(out _,out var smallest)&&priority.CompareTo(smallest)>0)
                candidates.DequeueEnqueue((p,position,diameter),priority);
        }
        var selected=candidates.UnorderedItems.Select(m=>new ChartBubble(m.Element.Print,m.Element.Position,m.Element.Diameter)).OrderBy(m=>m.Print.TimeMs).ThenBy(m=>m.Print.Id).ToArray();
        return new(Array.AsReadOnly(selected),Math.Max(0,eligible-selected.Length));
    }
}
