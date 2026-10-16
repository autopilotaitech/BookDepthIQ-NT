using BookDepthIQ.Dom.Charts;
using Xunit;
public class LargeTradeSettingsTests
{
    [Fact] public void Profiles_follow_known_rollovers_but_do_not_mix_micro_or_unknown_symbols()
    {
        var s=new ChartWorkspaceSettings();s.TradeProfile("NQ 12-26").MinContracts=85;
        Assert.Equal(85,s.TradeProfile("NQ 03-27").MinContracts);
        Assert.Equal(50,s.TradeProfile("MNQ 12-26").MinContracts);
        s.TradeProfile("CUSTOM A").MinContracts=123;Assert.Equal(50,s.TradeProfile("CUSTOM B").MinContracts);
        var restored=System.Text.Json.JsonSerializer.Deserialize<ChartWorkspaceSettings>(System.Text.Json.JsonSerializer.Serialize(s))!;
        restored.Normalize();Assert.Equal(85,restored.TradeProfile("NQ 12-26").MinContracts);
    }
    [Fact] public void Invalid_saved_preferences_normalize_and_size_is_fixed_not_view_dependent()
    {
        var p=new LargeTradeProfile {MinContracts=0,BuyColor="bad",Opacity=double.NaN,MinDiameter=80,MaxDiameter=6};p.Normalize();
        Assert.Equal(1,p.MinContracts);Assert.Equal("#00C8F4",p.BuyColor);Assert.Equal(.2,p.Opacity);Assert.Equal(80,p.MaxDiameter);
        var fixedSize=new LargeTradeProfile();Assert.Equal(12,fixedSize.Diameter(50));Assert.Equal(24,fixedSize.Diameter(200));Assert.Equal(42,fixedSize.Diameter(100000));
    }
}
