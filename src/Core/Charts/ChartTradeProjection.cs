namespace BookDepthIQ.Core.Charts;

public sealed record ChartBubble(ChartPrint Print,double BarPosition,double Diameter);
public sealed record ChartBubbleProjection(IReadOnlyList<ChartBubble> Markers,int Omitted);
public static class ChartTradeProjection
{
    public static ChartBubbleProjection Build(IReadOnlyList<ChartPrint> prints,IReadOnlyList<ChartBar> bars,int frame,
        long fromMs,long toMs,long minContracts,double minDiameter,double maxDiameter,double lowTicks=double.NegativeInfinity,double highTicks=double.PositiveInfinity)
    {
        if(frame<=0||frame>=1440||bars.Count<2) return new(Array.Empty<ChartBubble>(),0);
        var candidates=new List<ChartBubble>();long duration=frame*60000L;
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
            candidates.Add(new(p,position,diameter));
        }
        var selected=candidates.OrderByDescending(m=>m.Print.Quantity).ThenBy(m=>m.Print.Id).Take(500).OrderBy(m=>m.Print.TimeMs).ThenBy(m=>m.Print.Id).ToArray();
        return new(Array.AsReadOnly(selected),Math.Max(0,candidates.Count-selected.Length));
    }
}
