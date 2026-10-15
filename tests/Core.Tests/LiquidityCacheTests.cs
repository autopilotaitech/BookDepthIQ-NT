using BookDepthIQ.Core;
using Xunit;

public class LiquidityCacheTests
{
    private sealed record Hit(long At,long Price,long Qty,char Side,long Shown);
    [Fact]
    public void Indexed_absorption_matches_original_rules_across_randomized_updates()
    {
        var rng=new Random(42); var l=new LiquidityTracker(); var hits=new List<Hit>();
        long now=0; int seconds=60;
        var sizes=new Dictionary<long,long>();
        string? Reference(long price,int group,long minimum)
        {
            var top=price+group-1;
            var recent=hits.Where(h=>now-h.At<=seconds*1000).ToList();
            var here=recent.Where(h=>h.Price>=price && h.Price<=top).ToList();
            if(here.Count==0) return null;
            var sells=here.Where(h=>h.Side=='s').Sum(h=>h.Qty); var buys=here.Where(h=>h.Side=='b').Sum(h=>h.Qty);
            var side=sells>=minimum && sells>=2*buys ? "bid" : buys>=minimum && buys>=2*sells ? "ask" : null;
            if(side==null) return null;
            var hitSide=side=="bid" ? 's' : 'b'; var first=here.First(h=>h.Side==hitSide);
            long shown=0,rest=0;
            for(var p=price;p<=top;p++) { shown=Math.Max(shown,here.FirstOrDefault(h=>h.Side==hitSide && h.Price==p)?.Shown ?? 0); rest+=sizes.GetValueOrDefault(p); }
            if((side=="bid" ? sells : buys)<1.5*shown) return null;
            if(recent.Any(h=>h.At>=first.At && (side=="bid" ? h.Price<price : h.Price>top))) return null;
            return rest<=0 || rest<0.5*shown ? null : side;
        }
        for(var update=0;update<200;update++)
        {
            now+=rng.Next(1,500); seconds=new[]{5,15,60}[rng.Next(3)]; l.SetFlowWindow(seconds);
            var b=Book(rng.Next(1,25)); l.OnBook(b,now);
            sizes.Clear(); for(var i=0;i<b.Bids.Ticks.Length;i++) sizes[b.Bids.Ticks[i]]=b.Bids.Sizes[i];
            for(var i=0;i<b.Asks.Ticks.Length;i++) sizes[b.Asks.Ticks[i]]=b.Asks.Sizes[i];
            var price=rng.Next(97,105); var qty=rng.Next(1,40); var side=rng.Next(2)==0 ? 's' : 'b';
            l.OnTrade(price,qty,side,now); hits.Add(new(now,price,qty,side,sizes.GetValueOrDefault(price)));
            foreach(var group in new[]{1,2,4}) foreach(var minimum in new long[]{1,5,20}) for(var row=96;row<=105;row++)
                Assert.Equal(Reference(row,group,minimum),l.Absorb(row,group,minimum));
        }
    }
    [Fact]
    public void Repeated_render_reads_do_not_allocate_trade_history_per_row()
    {
        var l=new LiquidityTracker(); l.SetFlowWindow(60); l.OnBook(Book(),0);
        for(var i=0;i<10000;i++) l.OnTrade(100+i%10,1,'s',i);
        for(var row=60;row<140;row++) { l.FlowAt(row); l.Absorb(row); }
        var before=GC.GetAllocatedBytesForCurrentThread();
        for(var frame=0;frame<10;frame++) for(var row=60;row<140;row++) { l.FlowAt(row); l.Absorb(row); }
        Assert.InRange(GC.GetAllocatedBytesForCurrentThread()-before,0,500_000);
    }
    private static Book Book(long bid=10) => new(new BookSide(new long[] {100,99,98,97},new long[] {bid,10,10,10}),
        new BookSide(new long[] {101,102,103,104},new long[] {10,10,10,10}),0);
    [Fact]
    public void Absorption_cache_invalidates_on_trade_book_window_and_reset()
    {
        var l=new LiquidityTracker(); l.OnBook(Book(),0);
        l.OnTrade(100,16,'s',100);
        Assert.Equal("bid",l.Absorb(100));
        Assert.Equal("bid",l.Absorb(100));
        l.OnTrade(99,1,'s',200);
        Assert.Null(l.Absorb(100));
        l.Reset(); l.OnBook(Book(),0); l.OnTrade(100,16,'s',100);
        Assert.Equal("bid",l.Absorb(100));
        l.OnBook(Book(0),200);
        Assert.Null(l.Absorb(100));
        l.Reset(); l.OnBook(Book(),0); l.OnTrade(100,16,'s',100);
        l.OnBook(Book(),6000); Assert.Equal("bid",l.Absorb(100));
        l.SetFlowWindow(5); Assert.Null(l.Absorb(100));
    }
    [Fact]
    public void Group_absorption_checks_trades_through_entire_row_and_threshold()
    {
        var l=new LiquidityTracker(); l.OnBook(Book(),0);
        l.OnTrade(99,10,'s',100); l.OnTrade(100,10,'s',110);
        Assert.Equal("bid",l.Absorb(99,2,5));
        Assert.Null(l.Absorb(99,2,25));
        l.OnTrade(98,1,'s',120); Assert.Null(l.Absorb(99,2,5));
    }
}
