using BookDepthIQ.Core.Charts;
using Xunit;
public class ChartTradeStoreTests
{
    [Fact] public void Equal_prints_remain_distinct_and_receipt_bounds_evict_without_guessing_side()
    {
        var s=new ChartTradeStore(2,1000);s.Reset("NQ TEST");s.SetCapture(true,0);
        s.Add("NQ TEST",100,50,'b',100,100);s.Add("NQ TEST",100,50,'b',100,101);
        Assert.Equal(2,s.Snapshot(101).Prints.Count);
        Assert.NotEqual(s.Snapshot(101).Prints[0].Id,s.Snapshot(101).Prints[1].Id);
        s.Add("NQ TEST",101,60,'x',50,200);
        var snap=s.Snapshot(200);Assert.Equal(new long[]{60,50},snap.Prints.Select(p=>p.Quantity));
        Assert.Equal('?',snap.Prints[0].Side);Assert.Equal(1,snap.Evicted);
        Assert.Empty(s.Snapshot(1300).Prints);
    }
    [Fact] public void Capture_contract_and_disconnect_boundaries_preserve_honest_coverage()
    {
        var s=new ChartTradeStore();s.Reset("NQ TEST");s.SetCapture(true,10);
        s.Add("ES TEST",100,50,'b',20,20);s.Add("NQ TEST",100,0,'s',20,20);
        Assert.Empty(s.Snapshot(20).Prints);
        s.Add("NQ TEST",100,50,'b',30,30);s.Disconnect(40);
        Assert.Single(s.Snapshot(40).Prints);Assert.Equal(40,s.Snapshot(40).Coverage[0].EndMs);
        s.Add("NQ TEST",101,60,'s',50,50);
        Assert.Equal(2,s.Snapshot(50).Coverage.Count);
        s.SetCapture(false,60);Assert.Empty(s.Snapshot(60).Prints);
        s.SetCapture(true,70);s.Reset("ES TEST");Assert.Empty(s.Snapshot(70).Prints);
    }
    [Fact] public void Stress_keeps_bounded_storage_and_reuses_unchanged_snapshot()
    {
        var s=new ChartTradeStore();s.Reset("NQ TEST");s.SetCapture(true,0);
        for(int i=0;i<100000;i++) s.Add("NQ TEST",100,1,'?',i,i);
        var snapshot=s.Snapshot(100000);Assert.Equal(50000,snapshot.Prints.Count);
        Assert.Equal(50000,snapshot.Evicted);Assert.Same(snapshot,s.Snapshot(100000));
    }
}
