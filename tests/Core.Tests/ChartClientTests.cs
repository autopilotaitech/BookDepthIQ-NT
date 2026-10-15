using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using BookDepthIQ.Dom.Charts;
using Xunit;

public class ChartClientTests
{
    [Fact] public async Task Client_subscribes_only_on_chart_socket_and_unsubscribes_hidden_slots()
    {
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
        try
        {
            var model=new ChartCoordinator();using var client=new ChartClient(model,((IPEndPoint)listener.LocalEndpoint).Port);
            client.SetRequests(new[]{new ChartClient.Request(0,"NQ TEST",3,500)});
            using var tcp=await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
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
