using BookDepthIQ.Core;
using Xunit;

public class UiBatchPumpTests
{
    [Fact]
    public async Task Burst_preserves_order_with_one_pending_callback_and_bounded_chunks()
    {
        var callbacks=new Queue<Action>(); var received=new List<int>(); var releases=new List<int>(); int notifications=0;
        using var pump=new UiBatchPump<int>(callbacks.Enqueue,received.Add,()=>notifications++,releases.Add,512);
        for(var i=0;i<500;i++) await pump.Enqueue(i);
        Assert.Single(callbacks);
        while(callbacks.TryDequeue(out var callback)) { callback(); Assert.True(callbacks.Count<=1); }
        Assert.Equal(Enumerable.Range(0,500),received);
        Assert.Equal(received,releases);
        Assert.InRange(notifications,4,500);
    }
    [Fact]
    public async Task Capacity_waits_without_dropping_and_dispose_releases_pending_resources()
    {
        var callbacks=new Queue<Action>(); var received=new List<int>(); var releases=new List<int>();
        var pump=new UiBatchPump<int>(callbacks.Enqueue,received.Add,()=>{},releases.Add,2);
        await pump.Enqueue(1); await pump.Enqueue(2);
        var waiting=pump.Enqueue(3); Assert.False(waiting.IsCompleted);
        callbacks.Dequeue()(); await waiting;
        pump.Dispose(); while(callbacks.TryDequeue(out var cb)) cb();
        Assert.Equal(new[]{1,2},received); Assert.Equal(new[]{1,2,3},releases);
    }
}
