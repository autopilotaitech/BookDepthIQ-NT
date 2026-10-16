using System.Text.Json;
using Avalonia.Headless.XUnit;
using Xunit;
public class ChartTradeIntegrationTests
{
    [AvaloniaFact] public void Real_ingress_records_only_live_prints_and_never_emits_a_command()
    {
        using var h=new TicketHarness();var s=h.State;s.ChartTrades.SetCapture(true,0);
        void Apply(string json) {using var d=JsonDocument.Parse(json);s.Apply(d.RootElement);}
        Apply("{\"t\":\"trade\",\"inst\":\"MNQ 12-26\",\"p\":30000,\"v\":85,\"side\":\"b\",\"ms\":3601000,\"chartMs\":1000}");
        var print=Assert.Single(s.ChartTrades.Snapshot(1000).Prints);
        Assert.Equal(120000,print.Tick);Assert.Equal(85,print.Quantity);Assert.Equal('b',print.Side);
        Assert.Equal(1000,print.TimeMs);
        var nativeBars=new[]{
            new BookDepthIQ.Core.Charts.ChartBar(0,DateTimeOffset.UnixEpoch,new(1970,1,1),120000,120004,119996,120000,1,1,false),
            new BookDepthIQ.Core.Charts.ChartBar(1,DateTimeOffset.UnixEpoch.AddMinutes(1),new(1970,1,1),120000,120004,119996,120000,1,1,false)};
        Assert.Single(BookDepthIQ.Core.Charts.ChartTradeProjection.Build(new[]{print},nativeBars,1,0,60000,50,12,42).Markers);
        Apply("{\"t\":\"hist\",\"inst\":\"MNQ 12-26\",\"trades\":[[500,29999,999,\"s\"]],\"done\":false}");
        Assert.Single(s.ChartTrades.Snapshot(1000).Prints);
        s.Connected=false;Assert.Single(s.ChartTrades.Snapshot(1000).Prints);
        Assert.NotNull(s.ChartTrades.Snapshot(1000).Coverage[0].EndMs);
        s.Instrument="ES TEST";Assert.Empty(s.ChartTrades.Snapshot(1000).Prints);Assert.Empty(h.Commands);
    }
    [AvaloniaFact] public void Legacy_or_other_contract_packets_do_not_guess_chart_time_or_identity()
    {
        using var h=new TicketHarness();var s=h.State;s.ChartTrades.SetCapture(true,0);
        using var old=JsonDocument.Parse("{\"t\":\"trade\",\"p\":30000,\"v\":85,\"side\":\"b\",\"ms\":1000}");s.Apply(old.RootElement);
        Assert.Empty(s.ChartTrades.Snapshot(1000).Prints);
        using var other=JsonDocument.Parse("{\"t\":\"trade\",\"inst\":\"ES TEST\",\"p\":30000,\"v\":85,\"side\":\"b\",\"ms\":1000,\"chartMs\":1000}");s.Apply(other.RootElement);
        Assert.Empty(s.ChartTrades.Snapshot(1000).Prints);Assert.Empty(h.Commands);
    }
}
