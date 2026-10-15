using BookDepthIQ.Core;
using Xunit;

public class FlowPressureTests
{
    private static Book Depth(long bid,long ask) => new(
        new BookSide(new long[]{100,99,98,97,96,95},new long[]{bid,10,10,10,10,10}),
        new BookSide(new long[]{101,102,103,104,105,106},new long[]{ask,10,10,10,10,10}),0);
    [Theory]
    [InlineData(15,10,100,5,5)]
    [InlineData(5,10,100,-5,-5)]
    [InlineData(10,15,101,5,-5)]
    [InlineData(10,5,101,-5,5)]
    public void Direction_distinguishes_bid_and_ask_flow(long bid,long ask,long row,long net,long pressure)
    {
        var l=new LiquidityTracker(); l.OnBook(Depth(10,10),0); l.OnBook(Depth(bid,ask),1000);
        var f=l.FlowAt(row);
        Assert.Equal(net,f.Net); Assert.Equal(pressure,f.Pressure);
        Assert.Equal(1,f.PressureStrength);
    }
    [Fact]
    public void Direction_is_preserved_when_price_changes_book_side()
    {
        var l=new LiquidityTracker(); l.OnBook(Depth(10,10),0); l.OnBook(Depth(15,10),1000);
        var moved=new Book(new BookSide(new long[]{99,98,97,96,95,94},new long[]{10,10,10,10,10,10}),
            new BookSide(new long[]{100,101,102,103,104,105},new long[]{15,10,10,10,10,10}),0);
        l.OnBook(moved,2000);
        Assert.Equal(5,l.FlowAt(100).Pressure);
        l.SetFlowWindow(5); l.OnBook(moved,7000);
        Assert.Equal(0,l.FlowAt(100).Pressure);
    }
    [Fact]
    public void Grouped_bid_and_ask_stacking_cancel_direction_but_keep_raw_net()
    {
        var l=new LiquidityTracker(); l.OnBook(Depth(10,10),0); l.OnBook(Depth(15,15),1000);
        var f=l.FlowAt(100,2);
        Assert.Equal(10,f.Net); Assert.Equal(0,f.Pressure); Assert.Equal(0,f.PressureStrength);
    }
}
