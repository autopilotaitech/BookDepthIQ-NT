using System.Text.Json;
using BookDepthIQ.Dom;
using Xunit;

public class AccountDiscoveryTests
{
    [Fact]
    public void Prop_account_connected_after_start_appears_without_resetting_market_or_selection()
    {
        var state=new DomState(new Settings(),Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"))
            { Account="Sim101",Instrument="MNQ 12-26",LastTick=120000 };
        using var hello=JsonDocument.Parse("{\"t\":\"hello\",\"accounts\":[{\"name\":\"Sim101\",\"conn\":\"Connected\"}]}");
        state.Apply(hello.RootElement);
        using var update=JsonDocument.Parse("{\"t\":\"accounts\",\"accounts\":[{\"name\":\"Sim101\",\"conn\":\"Connected\"},{\"name\":\"APEX-123\",\"conn\":\"Connected\"}]}");
        state.Apply(update.RootElement);
        Assert.Contains(state.Accounts,a=>a.Name=="APEX-123" && a.Conn=="Connected");
        Assert.Equal("Sim101",state.Account);
        Assert.Equal(120000,state.LastTick);
        Assert.Equal("MNQ 12-26",state.Instrument);
        using var disconnected=JsonDocument.Parse("{\"t\":\"accounts\",\"accounts\":[{\"name\":\"APEX-123\",\"conn\":\"Disconnected\"}]}");
        state.Apply(disconnected.RootElement);
        Assert.Equal("Disconnected",Assert.Single(state.Accounts).Conn);
    }
}
