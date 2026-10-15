using System.Collections.Concurrent;
using System.Diagnostics;

namespace BookDepthIQ.Core;

/// <summary>Ordered, bounded producer queue with one scheduled UI drain and cooperative time slicing.</summary>
public sealed class UiBatchPump<T> : IDisposable
{
    private readonly ConcurrentQueue<T> queue = new();
    private readonly SemaphoreSlim slots;
    private readonly CancellationTokenSource stopped = new();
    private readonly Action<Action> schedule;
    private readonly Action<T> apply;
    private readonly Action<T>? release;
    private readonly Action completed;
    private int scheduled;
    public UiBatchPump(Action<Action> schedule, Action<T> apply, Action completed, Action<T>? release=null, int capacity=2048)
    { this.schedule=schedule; this.apply=apply; this.completed=completed; this.release=release; slots=new(capacity,capacity); }
    public async Task Enqueue(T item,CancellationToken ct=default)
    {
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,stopped.Token);
        await slots.WaitAsync(linked.Token).ConfigureAwait(false);
        lock(queue)
        {
            if(stopped.IsCancellationRequested) { slots.Release(); throw new OperationCanceledException(); }
            queue.Enqueue(item);
        }
        Schedule();
    }
    private void Schedule()
    {
        if(Interlocked.CompareExchange(ref scheduled,1,0)==0) schedule(Drain);
    }
    private void Drain()
    {
        var timer=Stopwatch.StartNew(); int count=0;
        try
        {
            while(count<128 && timer.ElapsedMilliseconds<4 && queue.TryDequeue(out var item))
            {
                slots.Release();
                try { if(!stopped.IsCancellationRequested) apply(item); }
                finally { release?.Invoke(item); }
                count++;
            }
            if(count>0 && !stopped.IsCancellationRequested) completed();
        }
        finally
        {
            Volatile.Write(ref scheduled,0);
            if(!queue.IsEmpty && !stopped.IsCancellationRequested) Schedule();
        }
    }
    public void Dispose()
    {
        lock(queue)
        {
            stopped.Cancel();
            while(queue.TryDequeue(out var item)) { slots.Release(); release?.Invoke(item); }
        }
    }
}
