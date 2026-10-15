namespace BookDepthIQ.Core;

public sealed record AdxReading(double Value,double PlusDi,double MinusDi,int Bars);

/// <summary>Selected minute bars, independent of the four-hour scalp/swing cache.</summary>
public sealed class AdxBars
{
    public static readonly int[] Timeframes = {1,3,5,10,15,30};
    private sealed class Candle {public long Hi,Lo,Close,LastMs;}
    private readonly SortedDictionary<long,Candle> bars=new();
    private readonly long intervalMs;
    private readonly int period;
    private long calculatedBefore=long.MinValue;
    private bool dirty=true;
    private AdxReading? cached;
    public AdxBars(int minutes=3,int period=14)
    {
        if(!Timeframes.Contains(minutes)) throw new ArgumentOutOfRangeException(nameof(minutes));
        if(period<1 || period>100) throw new ArgumentOutOfRangeException(nameof(period));
        intervalMs=minutes*60_000L; this.period=period;
    }
    public void Add(long timeMs,long priceTicks)
    {
        if(timeMs<0 || priceTicks<=0) return;
        var key=timeMs/intervalMs;
        if(bars.TryGetValue(key,out var b))
        {
            b.Hi=Math.Max(b.Hi,priceTicks); b.Lo=Math.Min(b.Lo,priceTicks);
            if(timeMs>=b.LastMs) {b.Close=priceTicks; b.LastMs=timeMs;}
        }
        else bars[key]=new Candle {Hi=priceTicks,Lo=priceTicks,Close=priceTicks,LastMs=timeMs};
        if(key<calculatedBefore) dirty=true;
        // A few thousand aggregated bars, never a per-trade history. Bounded even after weeks online.
        while(bars.Count>4096) {bars.Remove(bars.First().Key); dirty=true;}
    }
    public AdxReading? Snapshot(long nowMs)
    {
        var before=nowMs/intervalMs;
        if(before==calculatedBefore && !dirty) return cached;
        calculatedBefore=before; dirty=false;
        var closed=bars.Where(kv=>kv.Key<before).Select(kv=>kv.Value).ToList();
        cached=null;
        // Do not classify the initial seed or a forming candle as a measured trend.
        if(closed.Count<2*period+1) return null;
        double tr=closed[0].Hi-closed[0].Lo,plus=0,minus=0,adx=50,diPlus=0,diMinus=0;
        for(var i=1;i<closed.Count;i++)
        {
            var b=closed[i]; var prev=closed[i-1];
            double up=b.Hi-prev.Hi,down=prev.Lo-b.Lo;
            var p=up>down?Math.Max(up,0):0; var m=down>up?Math.Max(down,0):0;
            var range=Math.Max(b.Hi-b.Lo,Math.Max(Math.Abs(b.Hi-prev.Close),Math.Abs(b.Lo-prev.Close)));
            if(i<period) {tr+=range; plus+=p; minus+=m;}
            else {tr=tr-tr/period+range; plus=plus-plus/period+p; minus=minus-minus/period+m;}
            diPlus=tr==0?0:100*plus/tr; diMinus=tr==0?0:100*minus/tr;
            var sum=diPlus+diMinus;
            // Match NT8 @ADX's initial value, accumulation, Wilder smoothing, and zero-DI fallback.
            adx=sum==0?50:((period-1)*adx+100*Math.Abs(diPlus-diMinus)/sum)/period;
        }
        return cached=new(adx,diPlus,diMinus,closed.Count);
    }
}
