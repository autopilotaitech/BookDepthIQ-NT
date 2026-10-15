using BookDepthIQ.Dom.Charts;
using Xunit;

public class ChartTransportTests
{
    [Fact] public void Equal_requests_can_share_bars_without_copying_or_cross_reset()
    {
        var model=new ChartCoordinator();model.Reset(0,3,500);
        model.Apply("{\"t\":\"bars\",\"slot\":0,\"generation\":1,\"tickSize\":0.25,\"bars\":[{\"index\":0,\"ms\":0,\"date\":\"2026-10-15\",\"open\":100,\"high\":104,\"low\":98,\"close\":102,\"volume\":25,\"revision\":1,\"forming\":true}]}");
        model.Share(1,0);
        Assert.Same(model.Snapshot(0).Bars,model.Snapshot(1).Bars);
        model.Unshare(1);Assert.NotSame(model.Snapshot(0).Bars,model.Snapshot(1).Bars);
    }
    [Fact] public void Stale_packets_and_raw_ticks_cannot_change_authoritative_volume()
    {
        var model=new ChartCoordinator(); var g=model.Reset(0,3,500);
        string Packet(long generation)=>"{\"t\":\"bars\",\"slot\":0,\"generation\":"+generation+",\"frame\":3,\"tickSize\":0.25,\"bars\":[{\"index\":1,\"ms\":0,\"date\":\"2026-10-15\",\"open\":100,\"high\":104,\"low\":98,\"close\":102,\"volume\":25,\"revision\":1,\"forming\":true}]}";
        model.Apply(Packet(g-1)); Assert.Empty(model.Snapshot(0).Bars);
        model.Apply(Packet(g));
        for(int i=0;i<100000;i++) model.Apply("{\"t\":\"last\",\"slot\":0,\"generation\":"+g+",\"price\":103,\"volume\":9,\"sequence\":"+(i+1)+"}");
        Assert.Equal(25,Assert.Single(model.Snapshot(0).Bars).Volume);
        Assert.Equal(103,model.Snapshot(0).LastPrice);
        model.Reset(0,1440,500); model.Apply(Packet(g)); Assert.Empty(model.Snapshot(0).Bars);
    }
    [Fact] public void Disconnect_marks_charts_stale_instead_of_live()
    {
        var model=new ChartCoordinator();var g=model.Reset(0,3,500);
        model.Apply("{\"t\":\"complete\",\"slot\":0,\"generation\":"+g+"}");
        Assert.Equal("LIVE",model.Snapshot(0).Status);
        model.Disconnected(); Assert.NotEqual("LIVE",model.Snapshot(0).Status);
    }
}
