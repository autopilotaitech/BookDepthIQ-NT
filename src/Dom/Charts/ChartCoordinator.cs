using System.Text.Json;
using BookDepthIQ.Core.Charts;

namespace BookDepthIQ.Dom.Charts;

public sealed record ChartSnapshot(IReadOnlyList<ChartBar> Bars,double TickSize,string Status,long? LastPrice,long Version,long ReceivedAtMs=0);

/// <summary>Chart worker state only. No account, order, DOM state or UI-thread dependency.</summary>
public sealed class ChartCoordinator
{
    sealed class Slot
    {
        public long Generation,Version,Sequence,ReceivedAtMs;
        public ChartStore Store=new(2000);
        public double TickSize=.25;
        public string Status="OFFLINE";
        public long? Last;
        public bool Dirty=true;
        public IReadOnlyList<ChartBar> Cached=Array.Empty<ChartBar>();
    }
    readonly object gate=new();
    readonly Slot[] slots={new(),new(),new()};
    readonly Dictionary<int,int> shared=new();
    public void Share(int slot,int source) {lock(gate) {if(slot!=source) shared[slot]=source;}}
    public void Unshare(int slot) {lock(gate) shared.Remove(slot);}
    public long Reset(int slot,int frame,int count)
    {
        lock(gate)
        {
            var s=slots[slot];s.Generation++;s.Store=new ChartStore(count,slot==2?75000:10000);s.Store.Reset(s.Generation);
            s.Cached=Array.Empty<ChartBar>();s.Last=null;s.Sequence=0;s.ReceivedAtMs=0;s.Dirty=true;s.Version++;s.Status="LOADING";return s.Generation;
        }
    }
    public void Disconnected(string reason="OFFLINE · chart bridge unavailable") {lock(gate) foreach(var s in slots) {s.Status=reason;s.Version++;}}
    public void Apply(string line)
    {
        using var doc=JsonDocument.Parse(line);var m=doc.RootElement;
        if(!m.TryGetProperty("slot",out var id) || id.GetInt32() is <0 or >2) return;
        lock(gate)
        {
            var s=slots[id.GetInt32()]; if(!m.TryGetProperty("generation",out var gen)||gen.GetInt64()!=s.Generation) return;
            switch(m.GetProperty("t").GetString())
            {
                case "bars":
                    s.TickSize=m.GetProperty("tickSize").GetDouble();
                    if(!double.IsFinite(s.TickSize)||s.TickSize<=0) throw new JsonException("Invalid chart tick size");
                    foreach(var b in m.GetProperty("bars").EnumerateArray())
                    {
                        var bar=new ChartBar(b.GetProperty("index").GetInt32(),DateTimeOffset.FromUnixTimeMilliseconds(b.GetProperty("ms").GetInt64()),DateOnly.ParseExact(b.GetProperty("date").GetString()!,"yyyy-MM-dd"),
                            b.GetProperty("open").GetInt64(),b.GetProperty("high").GetInt64(),b.GetProperty("low").GetInt64(),b.GetProperty("close").GetInt64(),b.GetProperty("volume").GetInt64(),b.GetProperty("revision").GetInt64(),b.GetProperty("forming").GetBoolean());
                        s.Dirty|=s.Store.Apply(s.Generation,bar);
                    }
                    break;
                case "last":
                    var sequence=m.GetProperty("sequence").GetInt64();
                    if(sequence<=s.Sequence) return;
                    if(s.Sequence!=0 && sequence!=s.Sequence+1) s.Status="GAP · reload required";
                    s.Sequence=sequence;s.Last=m.GetProperty("price").GetInt64();break;
                case "complete":s.Status="LIVE";break;
                case "error":s.Status=m.GetProperty("message").GetString()??"Chart error";break;
            }
            s.Version++;
            s.ReceivedAtMs=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
    }
    public ChartSnapshot Snapshot(int slot)
    {
        lock(gate) {var s=slots[shared.TryGetValue(slot,out var source)?source:slot];if(s.Dirty) {s.Cached=s.Store.Snapshot();s.Dirty=false;}return new(s.Cached,s.TickSize,s.Status,s.Last,s.Version,s.ReceivedAtMs);}
    }
    public long Version(int slot) {lock(gate) return slots[slot].Version;}
}
