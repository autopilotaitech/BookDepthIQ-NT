using System.Text.Json;
using BookDepthIQ.Core;
using BookDepthIQ.Dom;
using Xunit;

public class JournalSimulationTests
{
    [Fact]
    public void Sim101_bridge_fills_save_and_reload_fee_inclusive_round_trip()
    {
        var folder = Path.Combine(Path.GetTempPath(), "bdiq-journal-test-" + Guid.NewGuid());
        var path = Path.Combine(folder, "journal.json");
        try
        {
            var cfg = new Settings();
            var s = new DomState(cfg, path) { Account = "Sim101", Instrument = "MNQ 12-26", TickSize = .25, PointValue = 2 };
            void Fill(string id, string side, int qty, double price)
            {
                using var json = JsonDocument.Parse(JsonSerializer.Serialize(new { t = "exec", acct = "Sim101", inst = "MNQ 12-26", id, side, qty, price, commission = 1.0 * qty, fee = 0.0, ms = 1_800_000_000_000L }));
                s.Apply(json.RootElement);
            }
            Fill("entry", "b", 2, 100);
            Fill("partial", "s", 1, 102);
            Fill("exit", "s", 1, 104);
            Fill("exit", "s", 1, 104); // repeated delivery cannot duplicate a trip
            var trade = Assert.Single(s.Journal);
            Assert.Equal(8, trade.Pnl); // $12 gross, $4 fees
            Assert.Equal(4, trade.FeesUsd);
            var loaded = new DomState(cfg, path);
            Assert.Equal(trade, Assert.Single(loaded.Journal));
            Assert.Equal(8, JournalAnalytics.Compute(loaded.Journal).Net);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(folder)) Directory.Delete(folder);
        }
    }
}
