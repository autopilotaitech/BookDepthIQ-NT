namespace BookDepthIQ.Core.Charts;

/// <summary>Worker-owned native-bar cache. Revision and generation prevent stale callback overwrite.</summary>
public sealed class ChartStore
{
    readonly SortedDictionary<int,ChartBar> bars=new();
    readonly int capacity;
    long generation;
    public ChartStore(int capacity,int limit=10000) {this.capacity=Math.Clamp(capacity,250,Math.Clamp(limit,10000,75000));}
    public void Reset(long value) {generation=value; bars.Clear();}
    public bool Apply(long value,ChartBar bar)
    {
        if(value!=generation || bar.Volume<0 || bar.High<Math.Max(bar.Open,bar.Close) || bar.Low>Math.Min(bar.Open,bar.Close) || bar.Low>bar.High) return false;
        if(bars.TryGetValue(bar.Index,out var old) && old.Revision>=bar.Revision) return false;
        bars[bar.Index]=bar;
        while(bars.Count>capacity) bars.Remove(bars.First().Key);
        return true;
    }
    public IReadOnlyList<ChartBar> Snapshot()=>bars.Values.ToArray();
}
