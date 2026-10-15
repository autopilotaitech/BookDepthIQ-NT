using BookDepthIQ.Core;
using Xunit;

public class HeatShadeTests
{
    // A book like the owner's screenshot: most levels 40–100 lots, a few big ones.
    private static readonly double[] Book = { 42, 45, 37, 41, 82, 57, 38, 190, 51, 57, 56, 49, 41, 74, 53, 44, 60, 68, 48, 78, 71, 69, 60, 56, 47, 51, 50, 58, 86, 83, 80, 82, 75, 90, 75, 51, 88, 66, 85, 146, 87, 76, 79, 92, 107, 74, 77, 70, 303, 60, 58, -40, -49, -53 };

    [Fact]
    public void ScaleIsTypicalAndHigh()
    {
        var s = HeatShade.Measure(Book);
        Assert.InRange(s.Typical, 55, 70);
        Assert.True(s.High >= 146);
        Assert.Equal(new HeatShade.Scale(1, 2), HeatShade.Measure(new double[] { 0, 0 }));
    }

    [Fact]
    public void TypicalLevelsFadeAndBigOnesStandOut()
    {
        var s = HeatShade.Measure(Book);
        var typical = HeatShade.Shade(60, s).Alpha;
        var big = HeatShade.Shade(190, s).Alpha;
        Assert.True(typical <= 0.08, $"typical {typical}");
        Assert.True(big >= 0.6, $"big {big}");
        Assert.Equal(HeatShade.Floor, HeatShade.Shade(5, s).Alpha);       // thin is a faint trace, still visible
        Assert.True(HeatShade.Shade(303, s).Alpha >= 0.94);
    }

    [Fact]
    public void BrightnessOnlyGrowsWithSize()
    {
        var s = HeatShade.Measure(Book);
        double prev = 0;
        for (var v = 1; v <= 400; v += 3)
        {
            var a = HeatShade.Shade(v, s).Alpha;
            Assert.True(a >= prev);
            prev = a;
        }
    }

    [Fact]
    public void WallIsThreeTimesTypicalOnEitherSide()
    {
        var s = new HeatShade.Scale(60, 200);
        Assert.False(HeatShade.Shade(179, s).Wall);
        Assert.True(HeatShade.Shade(180, s).Wall);
        Assert.True(HeatShade.Shade(-180, s).Wall);
    }

    [Fact]
    public void SharpFadesMoreThanSoft()
    {
        var s = new HeatShade.Scale(60, 200);
        var soft = HeatShade.Shade(110, s, 0).Alpha;
        var normal = HeatShade.Shade(110, s, 1).Alpha;
        var sharp = HeatShade.Shade(110, s, 2).Alpha;
        Assert.True(soft > normal && normal > sharp);
        Assert.Equal(HeatShade.Shade(110, s, 2).Alpha, HeatShade.Shade(110, s, 9).Alpha);   // out of range clamps
    }
}

public class HeatEdgeTests
{
    // Rows top (high price) to bottom; each row's heat per slice (+ bid, − offer).
    private static List<double[]> Rows(params double[][] r) => r.ToList();

    [Fact]
    public void EdgeSitsBetweenOffersAndBids()
    {
        var e = HeatShade.Boundary(Rows(new[] { -10.0 }, new[] { -20.0 }, new[] { 30.0 }, new[] { 15.0 }));
        Assert.Equal(2.0, e[0]);                       // between row 1 (offer) and row 2 (bid)
    }

    [Fact]
    public void EdgeFollowsTheMarketSliceBySlice()
    {
        // Slice 0: offers rows 0-1, bids 2-3. Slice 1: price ticked down — offers rows 0-2, bid row 3.
        var e = HeatShade.Boundary(Rows(new[] { -5.0, -5 }, new[] { -5.0, -5 }, new[] { 5.0, -5 }, new[] { 5.0, 5 }));
        Assert.Equal(new double?[] { 2, 3 }, e);
    }

    [Fact]
    public void AGapIsSplitInTheMiddle()
    {
        // Empty rows (the spread / no size) between the offers and the bids.
        var e = HeatShade.Boundary(Rows(new[] { -9.0 }, new[] { 0.0 }, new[] { 0.0 }, new[] { 9.0 }));
        Assert.Equal(2.0, e[0]);                       // edges 1, 2 and 3 tie → the middle one
    }

    [Fact]
    public void AStrayCellDoesNotDragTheLine()
    {
        // A small bid remnant above the offers (price moved within the slice) must not pull the edge up.
        var e = HeatShade.Boundary(Rows(new[] { 2.0 }, new[] { -40.0 }, new[] { -40.0 }, new[] { 50.0 }, new[] { 50.0 }));
        Assert.Equal(3.0, e[0]);
    }

    [Fact]
    public void OneSidedOrEmptySlicesHaveNoLine()
    {
        var e = HeatShade.Boundary(Rows(new[] { 0.0, 5, -5 }, new[] { 0.0, 5, -5 }));
        Assert.Null(e[0]);
        Assert.Null(e[1]);
        Assert.Null(e[2]);
        Assert.Empty(HeatShade.Boundary(new List<double[]>()));
    }
}

public class HeatEdgeTrackerTests
{
    private static Book At(long bestBid) => new(
        new BookSide(Enumerable.Range(0, 20).Select(i => bestBid - i).ToArray(), Enumerable.Repeat(10L, 20).ToArray()),
        new BookSide(Enumerable.Range(1, 20).Select(i => bestBid + i).ToArray(), Enumerable.Repeat(10L, 20).ToArray()), 0);

    [Fact]
    public void LineFollowsTheBestBidAskThroughTheTracker()
    {
        var l = new LiquidityTracker();                 // 5 min window, 10 s slices
        long t = 0;
        foreach (var bid in new long[] { 100, 100, 103, 103, 97, 97 })
        {
            for (var k = 0; k < 10; k++) { l.OnBook(At(bid), t); t += 1000; }   // 10 s at each price = one slice
        }
        const long top = 115;
        var rows = Enumerable.Range(0, 30).Select(r => l.Heat(top - r)).ToList();
        var e = HeatShade.Boundary(rows);
        // Row r is price top − r; the edge under the best bid's row = top − bestBid. "Now" (last) = bid 97.
        Assert.Equal(top - 97, e[^1]);
        var slices = e.Where(x => x != null).Select(x => (double)x!).ToList();
        Assert.Contains(top - 100.0, slices);
        Assert.Contains(top - 103.0, slices);
    }
}
