using System.Text.Json;
using Avalonia.Headless.XUnit;
using Xunit;
public class ChartTradeIntegrationTests
{
    [AvaloniaFact] public void Real_ingress_records_only_live_prints_and_never_emits_a_command()
    {
        using var h=new TicketHarness();var s=h.State;s.ChartTrades.SetCapture(true,0);
        void Apply(string json) {using var d=JsonDocument.Parse(json);s.Apply(d.RootElement);}
        Apply("{\"t\":\"trade\",\"p\":30000,\"v\":85,\"side\":\"b\",\"ms\":1000}");
        var print=Assert.Single(s.ChartTrades.Snapshot(1000).Prints);
        Assert.Equal(120000,print.Tick);Assert.Equal(85,print.Quantity);Assert.Equal('b',print.Side);
        Apply("{\"t\":\"hist\",\"inst\":\"NQ 12-26\",\"trades\":[[500,29999,999,\"s\"]],\"done\":false}");
        Assert.Single(s.ChartTrades.Snapshot(1000).Prints);
        s.Connected=false;Assert.Single(s.ChartTrades.Snapshot(1000).Prints);
        Assert.NotNull(s.ChartTrades.Snapshot(1000).Coverage[0].EndMs);
        s.Instrument="ES TEST";Assert.Empty(s.ChartTrades.Snapshot(1000).Prints);Assert.Empty(h.Commands);
    }
}
