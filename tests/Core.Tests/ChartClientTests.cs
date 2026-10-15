using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BookDepthIQ.Dom.Charts;
using Xunit;

public class ChartClientTests
{
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
