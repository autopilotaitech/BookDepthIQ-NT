using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BookDepthIQ.Dom.Charts;
using Xunit;

public class ChartClientTests
{
    [Fact] public async Task Reconnect_resubscribes_with_new_generation_and_rejects_old_data()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try
        {
            var model=new ChartCoordinator();using var client=new ChartClient(model,((IPEndPoint)listener.LocalEndpoint).Port);
            client.SetRequests(new[]{new ChartClient.Request(0,"NQ TEST",3,500),new ChartClient.Request(1,"NQ TEST",3,500)});
            using var first=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var w1=new StreamWriter(first.GetStream(),new UTF8Encoding(false),4096,true) {AutoFlush=true};
            using var r1=new StreamReader(first.GetStream(),Encoding.UTF8,false,4096,true);
            await w1.WriteLineAsync("{\"t\":\"hello\",\"chartVersion\":1}");
            using var initial=JsonDocument.Parse((await r1.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!);
            var old=initial.RootElement.GetProperty("generation").GetInt64();
            await w1.WriteLineAsync(Packet(old,100));await Until(()=>model.Snapshot(1).LastPrice==100);
            first.Dispose();
            await Until(()=>model.Snapshot(0).Status.Contains("OFFLINE"));
            using var second=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(6));
            using var w2=new StreamWriter(second.GetStream(),new UTF8Encoding(false),4096,true) {AutoFlush=true};
            using var r2=new StreamReader(second.GetStream(),Encoding.UTF8,false,4096,true);
            await w2.WriteLineAsync("{\"t\":\"hello\",\"chartVersion\":1}");
            using var renewed=JsonDocument.Parse((await r2.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!);
            var current=renewed.RootElement.GetProperty("generation").GetInt64();Assert.True(current>old);
            Assert.Null(model.Snapshot(0).LastPrice);Assert.Empty(model.Snapshot(1).Bars);
            await w2.WriteLineAsync(Packet(old,999));
            await w2.WriteLineAsync(Packet(current,200));
            await w2.WriteLineAsync($"{{\"t\":\"complete\",\"slot\":0,\"generation\":{current}}}");
            await Until(()=>model.Snapshot(1).Status=="LIVE");
            Assert.Equal(200,model.Snapshot(0).LastPrice);Assert.Equal(200,model.Snapshot(1).LastPrice);
            client.SetRequests(new[]{new ChartClient.Request(1,"ES TEST",15,250)});
            using var removed=JsonDocument.Parse((await r2.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!);
            Assert.Equal("unsubscribe",removed.RootElement.GetProperty("t").GetString());
            using var changed=JsonDocument.Parse((await r2.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)))!);
            Assert.Equal("ES TEST",changed.RootElement.GetProperty("instrument").GetString());
            Assert.Null(model.Snapshot(1).LastPrice);Assert.Equal("LOADING",model.Snapshot(1).Status);
        }
        finally {listener.Stop();}
        static string Packet(long generation,long price)=>$"{{\"t\":\"last\",\"slot\":0,\"generation\":{generation},\"sequence\":1,\"price\":{price}}}";
        static async Task Until(Func<bool> condition)
        {
            var deadline=DateTime.UtcNow.AddSeconds(5);
            while(!condition()&&DateTime.UtcNow<deadline) await Task.Delay(10);
            Assert.True(condition(),"Chart state transition did not arrive before deadline");
        }
    }
    [Fact] public async Task Non_chart_bridge_is_rejected_without_sending_any_commands()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try
        {
            var model=new ChartCoordinator();using var client=new ChartClient(model,((IPEndPoint)listener.LocalEndpoint).Port);
            client.SetRequests(new[]{new ChartClient.Request(0,"NQ TEST",3,2000)});
            using var tcp=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var writer=new StreamWriter(tcp.GetStream(),new UTF8Encoding(false),4096,true) {AutoFlush=true};
            using var reader=new StreamReader(tcp.GetStream(),Encoding.UTF8,false,4096,true);
            await writer.WriteLineAsync("{\"t\":\"hello\",\"v\":2,\"accounts\":[]}");
            Assert.Null(await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("incompatible",model.Snapshot(0).Status,StringComparison.OrdinalIgnoreCase);
        }
        finally {listener.Stop();}
    }
    [Fact] public async Task Client_subscribes_only_on_chart_socket_and_unsubscribes_hidden_slots()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try
        {
            var model=new ChartCoordinator();using var client=new ChartClient(model,((IPEndPoint)listener.LocalEndpoint).Port);
            client.SetRequests(new[]{new ChartClient.Request(0,"NQ TEST",3,500)});
            using var tcp=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var helloWriter=new StreamWriter(tcp.GetStream(),new UTF8Encoding(false),4096,true) {AutoFlush=true};
            await helloWriter.WriteLineAsync("{\"t\":\"hello\",\"chartVersion\":1,\"timezone\":\"UTC\"}");
            using var reader=new StreamReader(tcp.GetStream(),Encoding.UTF8,false,4096,true);
            var command=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var json=JsonDocument.Parse(command!);Assert.Equal("subscribe",json.RootElement.GetProperty("t").GetString());Assert.Equal("NQ TEST",json.RootElement.GetProperty("instrument").GetString());
            client.Reload();
            var reloaded=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            using var next=JsonDocument.Parse(reloaded!);
            Assert.True(next.RootElement.GetProperty("generation").GetInt64()>json.RootElement.GetProperty("generation").GetInt64());
            client.SetRequests(Array.Empty<ChartClient.Request>());
            var unsubscribe=await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains("unsubscribe",unsubscribe);
        }
        finally {listener.Stop();}
    }
}
