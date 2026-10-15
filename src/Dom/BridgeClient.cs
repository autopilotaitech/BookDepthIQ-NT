using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Threading;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

/// <summary>Connects to the NT8 bridge on 127.0.0.1, reconnects every 2 s, and applies each message
/// to the state on the UI thread. Sends commands as single lines.</summary>
public sealed class BridgeClient : IDisposable
{
    private readonly DomState state;
    private readonly CancellationTokenSource cts = new();
    private TcpClient? tcp;
    private StreamWriter? writer;
    private readonly object writeGate = new();
    private sealed record Pending(Action Apply, JsonDocument? Document = null);
    private readonly UiBatchPump<Pending> incoming;

    /// <summary>Called on the UI thread after each (re)connect, to re-subscribe.</summary>
    public event Action? Connected;

    public BridgeClient(DomState state)
    {
        this.state = state;
        incoming = new UiBatchPump<Pending>(action => Dispatcher.UIThread.Post(action), item => {
            try { item.Apply(); }
            catch(Exception e) { state.Note(OrderLogStatus.Error,"bridge message failed: " + e.Message); }
        },state.Raise,item=>item.Document?.Dispose());
    }

    public void Start() => _ = Task.Run(RunAsync);

    private async Task RunAsync()
    {
        while (!cts.IsCancellationRequested)
        {
            try
            {
                using var c = new TcpClient { NoDelay = true };
                await c.ConnectAsync("127.0.0.1", Protocol.Port, cts.Token);
                tcp = c;
                var stream = c.GetStream();
                lock (writeGate) writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                await incoming.Enqueue(new Pending(() => { state.Note(OrderLogStatus.Ok, "connected to the NinjaTrader bridge"); Connected?.Invoke(); }),cts.Token);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                string? line;
                while ((line = await reader.ReadLineAsync(cts.Token)) != null)
                {
                    if (line.Length == 0) continue;
                    JsonDocument doc;
                    try { doc = JsonDocument.Parse(line); }
                    catch (JsonException) { continue; }
                    try { await incoming.Enqueue(new Pending(()=>state.Apply(doc.RootElement),doc),cts.Token); }
                    catch { doc.Dispose(); throw; }
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception) { /* not running yet / dropped: retry below */ }
            lock (writeGate) writer = null;
            try { await incoming.Enqueue(new Pending(() =>
            {
                if (state.Connected) state.Note(BookDepthIQ.Core.OrderLogStatus.Error, "lost the NinjaTrader bridge — reconnecting");
                state.Connected = false;
            }),cts.Token); } catch(OperationCanceledException) { return; }
            try { await Task.Delay(2000, cts.Token); } catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Sends one command line. False when not connected (the caller says so on screen).</summary>
    public bool Send(string line)
    {
        lock (writeGate)
        {
            if (writer is null) return false;
            try { writer.WriteLine(line); return true; }
            catch (Exception) { writer = null; return false; }
        }
    }

    public void Dispose()
    {
        cts.Cancel();
        incoming.Dispose();
        tcp?.Dispose();
    }
}
