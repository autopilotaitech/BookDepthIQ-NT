namespace BookDepthIQ.Core.Charts;

public sealed record ChartPrint(long Id,string Instrument,long Tick,long Quantity,char Side,long TimeMs);
public sealed record ChartTradeCoverage(long StartMs,long? EndMs);
public sealed record ChartTradeSnapshot(IReadOnlyList<ChartPrint> Prints,IReadOnlyList<ChartTradeCoverage> Coverage,long Evicted,long Version)
{
    public static readonly ChartTradeSnapshot Empty=new(Array.Empty<ChartPrint>(),Array.Empty<ChartTradeCoverage>(),0,0);
}

/// <summary>UI-thread-owned bounded capture. Never consumes account state or sends commands.</summary>
public sealed class ChartTradeStore
{
    readonly int capacity;readonly long retentionMs;
    readonly Queue<(ChartPrint Print,long Received)> prints=new();
    readonly List<ChartTradeCoverage> coverage=new();
    string instrument="";bool enabled,active;long id,version,evicted;ChartTradeSnapshot? cached;
    public ChartTradeStore(int capacity=50000,long retentionMs=3600000)
    {this.capacity=Math.Clamp(capacity,1,50000);this.retentionMs=Math.Clamp(retentionMs,1,3600000);}
    void Dirty() {version++;cached=null;}
    public void Reset(string value)
    {instrument=value;prints.Clear();coverage.Clear();evicted=0;active=false;Dirty();}
    public void SetCapture(bool value,long nowMs)
    {
        if(value==enabled) return;enabled=value;
        if(!value) {prints.Clear();coverage.Clear();active=false;evicted=0;Dirty();}
    }
    public void Disconnect(long nowMs)
    {
        if(!active) return;
        coverage[^1]=coverage[^1] with {EndMs=Math.Max(nowMs,coverage[^1].StartMs)};active=false;Dirty();
    }
    void Trim(long nowMs)
    {
        while(prints.Count>0&&(prints.Count>capacity||nowMs-prints.Peek().Received>retentionMs))
        {prints.Dequeue();evicted++;Dirty();}
        while(coverage.Count>1 && coverage[0].EndMs is {} end && nowMs-end>retentionMs) {coverage.RemoveAt(0);Dirty();}
    }
    public void Add(string source,long tick,long quantity,char side,long eventMs,long receivedMs)
    {
        if(!enabled||source!=instrument||quantity<=0) return;
        if(!active)
        {
            if(coverage.Count>=256) coverage.RemoveAt(0);
            coverage.Add(new(receivedMs,null));active=true;
        }
        prints.Enqueue((new(++id,source,tick,quantity,side is 'b' or 's'?side:'?',eventMs),receivedMs));
        Dirty();Trim(receivedMs);
    }
    public ChartTradeSnapshot Snapshot(long nowMs)
    {
        Trim(nowMs);
        return cached??=new(Array.AsReadOnly(prints.Select(p=>p.Print).OrderBy(p=>p.TimeMs).ThenBy(p=>p.Id).ToArray()),
            Array.AsReadOnly(coverage.ToArray()),evicted,version);
    }
}
