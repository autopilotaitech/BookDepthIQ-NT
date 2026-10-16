using Avalonia.Threading;
using BookDepthIQ.Core.Charts;
namespace BookDepthIQ.Dom.Charts;
public sealed record SessionZoneJob(string Key,string Instrument,ChartSnapshot Source,SessionZoneSettings Settings,DateTimeOffset Now,bool SourceLive);

/// <summary>One running calculation and one replaceable latest request. Dispatch is generation-guarded.</summary>
public sealed class SessionZoneWorker : IDisposable
{
    readonly object gate=new();readonly Func<SessionZoneJob,SessionZoneSnapshot> calculate;
    readonly Action<SessionZoneJob,SessionZoneSnapshot> publish;readonly Action<Action> dispatch;
    SessionZoneJob? desired;long generation;bool running,disposed;
    public SessionZoneWorker(Func<SessionZoneJob,SessionZoneSnapshot>? calculate,Action<SessionZoneJob,SessionZoneSnapshot> publish,Action<Action>? dispatch=null)
    {this.calculate=calculate??(job=>ChartSessionZones.Build(job.Source.Bars,job.Settings.Definitions,job.Settings.Previous,job.Now,job.SourceLive));this.publish=publish;this.dispatch=dispatch??(a=>Dispatcher.UIThread.Post(a));}
    public void Request(SessionZoneJob job)
    {
        lock(gate)
        {
            if(disposed||desired?.Key==job.Key) return;desired=job;generation++;
            if(!running) {running=true;Task.Run(Run);}
        }
    }
    void Run()
    {
        while(true)
        {
            SessionZoneJob job;long token;
            lock(gate) {if(disposed||desired==null) {running=false;return;}job=desired;token=generation;}
            SessionZoneSnapshot result;
            try {result=calculate(job);} catch(Exception e) {result=new(Array.Empty<SessionZone>(),new[]{"Session calculation failed: "+e.Message});}
            dispatch(()=> {lock(gate) {if(!disposed&&token==generation) publish(job,result);}});
            lock(gate) {if(token==generation) {running=false;return;}}
        }
    }
    public void Invalidate() {lock(gate) {generation++;desired=null;}}
    public void Dispose() {lock(gate) {disposed=true;generation++;desired=null;}}
}
