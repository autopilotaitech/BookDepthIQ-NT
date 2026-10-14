using BookDepthIQ.Core;
using Xunit;

public class FlowOutlookTests
{
    [Fact]
    public void Hold_observation_completes_after_thirty_seconds()
    {
        var r = new OutlookRecorder();
        r.Start(100, true, 0);
        Assert.Null(r.OnTrade(100, 29999));
        Assert.True(r.OnTrade(101, 30000)!.Held);
        Assert.False(r.Active);
    }

    [Fact]
    public void Flow_duration_is_independent_of_heat_and_can_expand()
    {
        var l = new LiquidityTracker(30 * 60_000);
        var b = new Book(new BookSide(new long[] {100,99,98,97}, new long[] {10,10,10,10}),
            new BookSide(new long[] {101,102,103,104}, new long[] {10,10,10,10}), 0);
        l.SetFlowWindow(5);
        l.OnBook(b, 0);
        l.OnTrade(100, 4, 's', 100);
        l.OnBook(b, 200);
        l.OnBook(b, 6000);
        Assert.Equal(0, l.FlowAt(100).Stacked);
        l.SetFlowWindow(60);
        Assert.Equal(4, l.FlowAt(100).Stacked);
        Assert.Equal(30 * 60_000, l.WindowMs);
    }

    [Fact]
    public void Hold_observation_fails_when_price_crosses_invalidation()
    {
        var r = new OutlookRecorder();
        r.Start(100, true, 0);
        var result = r.OnTrade(99, 1000);
        Assert.NotNull(result);
        Assert.False(result!.Held);
    }

    [Fact]
    public void Replenishment_counts_contracts_replaced_after_execution()
    {
        var l = new LiquidityTracker();
        var b = new Book(new BookSide(new long[] {100,99,98,97}, new long[] {10,10,10,10}),
            new BookSide(new long[] {101,102,103,104}, new long[] {10,10,10,10}), 0);
        l.OnBook(b, 0);
        l.OnTrade(100, 4, 's', 100);
        l.OnBook(b, 200);
        Assert.Equal(4, l.FlowAt(100).Stacked);
    }

    [Fact]
    public void Unknown_side_does_not_bias_trend_delta()
    {
        var t = new TrendEngine(new TrendConfig {IntervalSec = 1});
        t.OnTrade(0, 100, 10, (bool?)null);
        t.OnTrade(1000, 100, 1, true);
        Assert.Equal(0, t.LastDelta);
        Assert.Equal(10, t.LastVol);
    }
}
