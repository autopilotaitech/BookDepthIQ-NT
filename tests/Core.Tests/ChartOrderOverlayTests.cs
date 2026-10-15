using System.Text.Json;
using BookDepthIQ.Dom;
using BookDepthIQ.Dom.Charts;
using Xunit;

public class ChartOrderOverlayTests
{
    static DomState State()
    {
        var state=new DomState(new Settings(),Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json")) {Instrument="NQ 12-26"};
        Apply(state,"{\"t\":\"hello\",\"accounts\":[]}");
        Apply(state,"{\"t\":\"account\",\"name\":\"Sim101\",\"cash\":100000}");
        Apply(state,"{\"t\":\"pos\",\"acct\":\"Sim101\",\"inst\":\"NQ 12-26\",\"qty\":3,\"avg\":30000}");
        Apply(state,"{\"t\":\"orders\",\"acct\":\"Sim101\",\"list\":[]}");return state;
    }
    static void Apply(DomState state,string json) {using var doc=JsonDocument.Parse(json);state.Apply(doc.RootElement);}
    static WorkingOrder Order(string id,string name,string type,double limit,double stop=0,int qty=3,int filled=0,string status="Working",string instrument="NQ 12-26")=>
        new(id,instrument,"Sell",type,qty,filled,limit,stop,status,"",name);
    [Fact] public void Displays_remaining_targets_stops_entries_and_position_without_changing_state()
    {
        var s=State();s.Orders.Add(Order("tp","BDIQ TP1","Limit",30020,qty:3,filled:1));
        s.Orders.Add(Order("sl","BDIQ SL","StopMarket",0,29990));
        s.Orders.Add(Order("entry","BDIQ entry","Limit",30005) with {Action="Buy"});
        var lines=ChartOrderOverlay.Build(s,"Sim101","NQ 12-26");
        Assert.Contains(lines,l=>l.Id=="tp"&&l.Quantity==2&&l.Price==30020&&l.Kind==ChartTradeKind.Target);
        Assert.Contains(lines,l=>l.Id=="sl"&&l.Price==29990&&l.Kind==ChartTradeKind.Stop);
        Assert.Contains(lines,l=>l.Id=="entry"&&l.Quantity==3&&l.Kind==ChartTradeKind.Entry);
        Assert.Contains(lines,l=>l.Kind==ChartTradeKind.Position&&l.Price==30000&&l.Quantity==3);
        Assert.Equal(3,s.Orders.Count);Assert.Equal(3,s.PosQty);
    }
    [Fact] public void Filters_other_contract_and_completed_orders_and_clears_on_account_switch_disconnect()
    {
        var s=State();s.Orders.Add(Order("other","BDIQ entry","Limit",30020,instrument:"ES 12-26"));
        s.Orders.Add(Order("filled","BDIQ TP1","Limit",30020,filled:3));
        s.Orders.Add(Order("cancelled","BDIQ TP1","Limit",30020,status:"Cancelled"));
        Assert.Single(ChartOrderOverlay.Build(s,"Sim101","NQ 12-26"));
        Assert.Empty(ChartOrderOverlay.Build(s,"Sim101","ES 12-26"));
        Assert.Empty(ChartOrderOverlay.Build(s,"OTHER","NQ 12-26"));
        s.Connected=false;Assert.Empty(ChartOrderOverlay.Build(s,"Sim101","NQ 12-26"));
        s.Account="OTHER";Assert.Empty(ChartOrderOverlay.Build(s,"OTHER","NQ 12-26"));
    }
    [Fact] public void Order_moves_partial_fills_cancels_and_flat_are_reflected_in_next_snapshot()
    {
        var s=State();s.Orders.Add(Order("tp","BDIQ TP1","Limit",30020));
        Assert.Contains(ChartOrderOverlay.Build(s,"Sim101",s.Instrument),l=>l.Id=="tp"&&l.Price==30020);
        s.Orders[0]=s.Orders[0] with {Limit=30025,Filled=2};
        Assert.Contains(ChartOrderOverlay.Build(s,"Sim101",s.Instrument),l=>l.Id=="tp"&&l.Price==30025&&l.Quantity==1);
        s.Orders.Clear();Apply(s,"{\"t\":\"pos\",\"acct\":\"Sim101\",\"inst\":\"NQ 12-26\",\"qty\":0,\"avg\":0}");
        Assert.Empty(ChartOrderOverlay.Build(s,"Sim101",s.Instrument));
    }
    [Fact] public void Stop_limit_has_distinct_trigger_and_limit_and_invalid_prices_are_excluded()
    {
        var s=State();s.Orders.Add(Order("stoplimit","BDIQ entry","StopLimit",29989,29990));
        s.Orders.Add(Order("invalid","BDIQ TP1","Limit",double.NaN));
        var lines=ChartOrderOverlay.Build(s,"Sim101",s.Instrument);
        Assert.Contains(lines,l=>l.Id=="stoplimit"&&l.Price==29990);
        Assert.Contains(lines,l=>l.Id=="stoplimit-limit"&&l.Price==29989);
        Assert.DoesNotContain(lines,l=>l.Id=="invalid");
    }
    [Fact] public void Market_if_touched_uses_native_stop_trigger_not_zero_limit_field()
    {
        var s=State();s.Orders.Add(Order("mit","manual","MIT",0,29995));
        var line=Assert.Single(ChartOrderOverlay.Build(s,"Sim101",s.Instrument),l=>l.Id=="mit");
        Assert.Equal(29995,line.Price);
    }
}
