using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace BookDepthIQ.Dom.Charts;

/// <summary>Independent read-only chart connection, started only when charts are visible.</summary>
public sealed class ChartClient : IDisposable
{
    public sealed record Request(int Slot,string Instrument,int Frame,int Count);
    readonly ChartCoordinator model;
    readonly CancellationTokenSource stop=new();
    readonly object gate=new();
    readonly Dictionary<int,Request> requests=new();
    Channel<string>? outgoing;
    TcpClient? socket;
    Task? run;
    readonly int port;
    public ChartClient(ChartCoordinator model,int port=36972) {this.model=model;this.port=port;}
    public void SetRequests(IEnumerable<Request> desired)
    {
        lock(gate)
        {
            var next=new Dictionary<int,Request>();
            foreach(var slot in Enumerable.Range(0,3)) model.Unshare(slot);
            foreach(var group in desired.GroupBy(r=>(r.Instrument,r.Frame,r.Count)))
            {
                var first=group.OrderBy(r=>r.Slot).First();next[first.Slot]=first;
                foreach(var alias in group.Where(r=>r.Slot!=first.Slot)) model.Share(alias.Slot,first.Slot);
            }
            foreach(var slot in requests.Keys.Except(next.Keys).ToArray()) {outgoing?.Writer.TryWrite(JsonSerializer.Serialize(new {t="unsubscribe",slot}));requests.Remove(slot);model.Reset(slot,3,250);}
            foreach(var item in next)
            {
                if(requests.TryGetValue(item.Key,out var old)&&old==item.Value) continue;
                requests[item.Key]=item.Value; SendSubscription(item.Value);
            }
            if(run==null && requests.Count>0) run=Task.Run(Run);
        }
    }
    void SendSubscription(Request r)
    {
        long generation=model.Reset(r.Slot,r.Frame,r.Count);
        if(outgoing!=null && !outgoing.Writer.TryWrite(JsonSerializer.Serialize(new {t="subscribe",slot=r.Slot,generation,instrument=r.Instrument,frame=r.Frame,count=r.Count}))) socket?.Dispose();
    }
    public void Reload() {lock(gate) foreach(var request in requests.Values) SendSubscription(request);}
    async Task Run()
    {
        while(!stop.IsCancellationRequested)
        {
            try
            {
                using var tcp=new TcpClient {NoDelay=true};socket=tcp;
                await tcp.ConnectAsync("127.0.0.1",port,stop.Token);
                var queue=Channel.CreateBounded<string>(32);
                lock(gate) {outgoing=queue;foreach(var request in requests.Values) SendSubscription(request);}
                using var stream=tcp.GetStream();
                using var connection=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var writer=Task.Run(async ()=> {
                    using var w=new StreamWriter(stream,new UTF8Encoding(false),4096,true) {AutoFlush=true};
                    try {await foreach(var command in queue.Reader.ReadAllAsync(connection.Token)) await w.WriteLineAsync(command);}
                    catch(OperationCanceledException) {} catch(IOException) {tcp.Dispose();} catch(ObjectDisposedException) {}
                });
                try
                {
                    using var reader=new StreamReader(stream,Encoding.UTF8,false,4096,true);
                    while(await reader.ReadLineAsync(stop.Token) is {} line)
                    {
                        if(line.Length>1_048_576) throw new IOException("Oversized chart packet");
                        model.Apply(line);
                    }
                }
                finally {connection.Cancel();queue.Writer.TryComplete();await writer;}
            }
            catch(OperationCanceledException) {break;}
            catch(Exception e) when(e is IOException or SocketException or ObjectDisposedException or JsonException or FormatException or InvalidOperationException or ArgumentException or KeyNotFoundException or OverflowException) {}
            lock(gate) outgoing=null;
            model.Disconnected();
            try {await Task.Delay(2000,stop.Token);} catch(OperationCanceledException) {break;}
        }
    }
    public void Dispose() {stop.Cancel();lock(gate) {outgoing?.Writer.TryComplete();socket?.Dispose();}model.Disconnected();}
}
