using BookDepthIQ.Core.Charts;
using BookDepthIQ.Dom.Charts;
using Xunit;
public class SessionZoneIntegrationTests
{
    [Fact] public void Session_and_pax_consumers_share_one_source_and_do_not_unsubscribe_each_other()
    {
        var cfg=new ChartWorkspaceSettings();cfg.Sessions.Enabled=true;cfg.Top.ShowSessionZones=true;
        Assert.Single(ChartRequests.Build("NQ TEST",new[]{0,1},cfg),r=>r.Slot==2);
        cfg.Bottom.Pax=true;Assert.Single(ChartRequests.Build("NQ TEST",new[]{0,1},cfg),r=>r.Slot==2);
        cfg.Sessions.Enabled=false;Assert.Single(ChartRequests.Build("NQ TEST",new[]{0,1},cfg),r=>r.Slot==2);
        cfg.Bottom.Pax=false;Assert.DoesNotContain(ChartRequests.Build("NQ TEST",new[]{0,1},cfg),r=>r.Slot==2);
        Assert.Empty(ChartRequests.Build("NQ TEST",Array.Empty<int>(),cfg));
        cfg.Sessions.Enabled=true;cfg.Top.Frame=1440;Assert.DoesNotContain(ChartRequests.Build("NQ TEST",new[]{0},cfg),r=>r.Slot==2);
    }
    static SessionZoneJob Job(string key)=>new(key,"NQ TEST",new(Array.Empty<ChartBar>(),.25,"LIVE",null,1),new(),DateTimeOffset.UnixEpoch,true);
    [Fact] public async Task Old_result_is_rejected_after_invalidate_or_dispose_even_if_dispatch_was_queued()
    {
        var posted=new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);int published=0;
        using var worker=new SessionZoneWorker(_=>SessionZoneSnapshot.Empty,(_,_)=>published++,a=>posted.TrySetResult(a));
        worker.Request(Job("old"));var callback=await posted.Task.WaitAsync(TimeSpan.FromSeconds(5));worker.Invalidate();callback();Assert.Equal(0,published);
        var next=new TaskCompletionSource<Action>(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposed=new SessionZoneWorker(_=>SessionZoneSnapshot.Empty,(_,_)=>published++,a=>next.TrySetResult(a));
        disposed.Request(Job("new"));var after=await next.Task.WaitAsync(TimeSpan.FromSeconds(5));disposed.Dispose();after();Assert.Equal(0,published);
    }
    [Fact] public async Task Worker_coalesces_pending_jobs_and_idle_refreshes_without_parallel_calculations()
    {
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);using var release=new ManualResetEventSlim();
        var complete=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var calls=new List<string>();
        using var worker=new SessionZoneWorker(job=> {
            lock(calls) calls.Add(job.Key);if(job.Key=="A") {entered.SetResult();Assert.True(release.Wait(TimeSpan.FromSeconds(5)));}return SessionZoneSnapshot.Empty;
        },(job,_)=> {if(job.Key=="C") complete.TrySetResult();},a=>a());
        worker.Request(Job("A"));await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        worker.Request(Job("B"));worker.Request(Job("C"));release.Set();await complete.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for(int i=0;i<100;i++) worker.Request(Job("C"));lock(calls) Assert.Equal(new[]{"A","C"},calls);
    }
    [Fact] public async Task Calculation_fault_is_a_readable_status_not_an_unobserved_exception()
    {
        var completed=new TaskCompletionSource<SessionZoneSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var worker=new SessionZoneWorker(_=>throw new InvalidOperationException("fixture failure"),(_,result)=>completed.TrySetResult(result),a=>a());
        worker.Request(Job("fail"));var result=await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));Assert.Contains(result.Notes,n=>n.Contains("fixture failure"));Assert.Empty(result.Zones);
    }
}
