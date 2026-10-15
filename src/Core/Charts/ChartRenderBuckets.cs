namespace BookDepthIQ.Core.Charts;

public readonly record struct ChartRenderBucket(int First,int Last,long Open,long High,long Low,long Close,long Volume);
public readonly record struct ChartStudySpan(int First,int Last,double Open,double High,double Low,double Close);

/// <summary>Visual-only pixel aggregation; original native bars and cursor identities stay untouched.</summary>
public static class ChartRenderBuckets
{
    public static IReadOnlyList<ChartStudySpan> Study(IReadOnlyList<double?> values,IReadOnlyList<ChartRenderBucket> buckets)
    {
        var result=new List<ChartStudySpan>();
        foreach(var bucket in buckets)
        {
            int first=-1,last=-1;double open=0,high=double.MinValue,low=double.MaxValue,close=0;
            for(int i=bucket.First;i<=bucket.Last&&i<values.Count;i++) if(values[i] is {} value&&double.IsFinite(value))
            {if(first<0) {first=i;open=value;}last=i;high=Math.Max(high,value);low=Math.Min(low,value);close=value;}
            if(first>=0) result.Add(new(first,last,open,high,low,close));
        }
        return result;
    }
    public static IReadOnlyList<ChartRenderBucket> Build(IReadOnlyList<ChartBar> bars,int start,int end,double visibleCount,double width)
    {
        start=Math.Clamp(start,0,bars.Count);end=Math.Clamp(end,start,bars.Count);
        var columns=Math.Max(1,(int)(Math.Max(1,width)/2));var stride=Math.Max(1,(int)Math.Ceiling(visibleCount/columns));
        var result=new List<ChartRenderBucket>();
        for(int first=start;first<end;first+=stride)
        {
            int last=Math.Min(end-1,first+stride-1);long high=bars[first].High,low=bars[first].Low,volume=0;
            for(int i=first;i<=last;i++) {high=Math.Max(high,bars[i].High);low=Math.Min(low,bars[i].Low);volume=bars[i].Volume>long.MaxValue-volume?long.MaxValue:volume+bars[i].Volume;}
            result.Add(new(first,last,bars[first].Open,high,low,bars[last].Close,volume));
        }
        return result;
    }
}
