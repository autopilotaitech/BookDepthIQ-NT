using System.Reflection;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript.AddOns;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization=true)]
public class BridgeOrderTests : IDisposable
{
    readonly object server;
    readonly Type type;
    readonly Account account=new Account();
    readonly Instrument instrument=Instrument.GetInstrument("MNQ 12-26",false);
    public BridgeOrderTests()
    {
        Account.All.Clear(); Account.All.Add(account);
        type=typeof(BookDepthIQBridge).GetNestedType("BridgeServer",BindingFlags.NonPublic);
        server=Activator.CreateInstance(type,true); Call("SelectAccount","Sim101");
    }
    void Call(string method,params object[] args) => type.GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(server,args);
    void Command(string json) => Call("Handle",new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json));
    Order Entry => account.Orders.Single(o=>o.Name=="BDIQ entry");
    List<Order> Legs => account.Orders.Where(o=>o.Name!="BDIQ entry" && Account.Working(o)).ToList();
    [Theory]
    [InlineData("buy",30008,29996)] [InlineData("sell",29992,30004)]
    public void Real_bridge_attaches_correct_tp_sl_per_partial_fill(string side,double tp,double sl)
    {
        Command("{\"t\":\"order\",\"cid\":\"1\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\""+side+"\",\"type\":\"market\",\"qty\":4,\"targets\":[[32,2],[64,2]],\"sl\":16}");
        account.Fill(Entry,1,30000);
        Assert.Equal(2,Legs.Count); Assert.All(Legs,o=>Assert.Equal(1,o.Quantity));
        Assert.Equal(tp,Legs.Single(o=>o.Name=="BDIQ TP1").LimitPrice);
        Assert.Equal(sl,Legs.Single(o=>o.Name=="BDIQ SL1").StopPrice);
        Assert.Equal(Legs[0].Oco,Legs[1].Oco); Assert.NotEmpty(Legs[0].Oco);
        account.Fill(Entry,3,30000.5);
        Assert.Equal(4,Legs.Where(o=>o.OrderType==OrderType.StopMarket).Sum(o=>o.Quantity));
        Assert.Equal(4,Legs.Where(o=>o.OrderType==OrderType.Limit).Sum(o=>o.Quantity));
        Assert.Equal(3,Legs.Select(o=>o.Oco).Distinct().Count());
    }
    [Fact]
    public void Scale_child_retains_global_tp2_name()
    {
        Command("{\"t\":\"order\",\"cid\":\"1\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":1,\"targets\":[[64,1,2]],\"sl\":16}");
        account.Fill(Entry,1,30000);
        Assert.Contains(Legs,o=>o.Name=="BDIQ TP2"); Assert.Contains(Legs,o=>o.Name=="BDIQ SL2");
    }

    [Fact]
    public void Pending_entry_still_gets_brackets_after_account_selection_changes()
    {
        Command("{\"t\":\"order\",\"cid\":\"1\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"limit\",\"price\":29990,\"qty\":1,\"targets\":[[32,1]],\"sl\":16}");
        Account.All.Add(new Account {Name="Sim102"}); Call("SelectAccount","Sim102");
        account.Fill(Entry,1,29990); Assert.Equal(2,Legs.Count);
    }

    [Fact]
    public void Duplicate_partial_execution_does_not_create_extra_protection()
    {
        Command("{\"t\":\"order\",\"cid\":\"1\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":4,\"targets\":[[32,2],[64,2]],\"sl\":16}");
        account.Fill(Entry,1,30000,"same-fill"); account.Replay(Entry,1,30000,"same-fill");
        Assert.Equal(1,Legs.Where(o=>o.OrderType==OrderType.StopMarket).Sum(o=>o.Quantity));
        account.Fill(Entry,3,30000,"remaining");
        Assert.Equal(4,Legs.Where(o=>o.OrderType==OrderType.StopMarket).Sum(o=>o.Quantity));
    }

    [Fact]
    public void Close_half_trims_furthest_target_and_matching_stop()
    {
        Command("{\"t\":\"order\",\"cid\":\"1\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":4,\"targets\":[[32,2],[64,2]],\"sl\":16}");
        account.Fill(Entry,4,30000);
        Command("{\"t\":\"closePart\",\"cid\":\"2\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"qty\":2}");
        Assert.Equal(2,Legs.Where(o=>o.OrderType==OrderType.StopMarket).Sum(o=>o.Quantity));
        Assert.DoesNotContain(Legs,o=>o.Name=="BDIQ TP2"); Assert.Contains(Legs,o=>o.Name=="BDIQ TP1");
    }

    [Theory]
    [InlineData("flatten")] [InlineData("kill")]
    public void Emergency_exit_cancels_all_protective_legs_and_flattens(string command)
    {
        Command("{\"t\":\"order\",\"cid\":\"1\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":1,\"targets\":[[32,1]],\"sl\":16}");
        account.Fill(Entry,1,30000);
        Command("{\"t\":\""+command+"\",\"cid\":\"2\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\"}");
        Assert.Empty(Legs); Assert.DoesNotContain(account.Positions,p=>p.Quantity>0);
    }

    [Theory]
    [InlineData("[]",16,1,0)] [InlineData("[[32,1]]",0,0,1)] [InlineData("[]",0,0,0)]
    public void Stop_only_target_only_and_unbracketed_entries(string targets,int sl,int stops,int limits)
    {
        Command("{\"t\":\"order\",\"cid\":\"1\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":1,\"targets\":"+targets+",\"sl\":"+sl+"}");
        account.Fill(Entry,1,30000);
        Assert.Equal(stops,Legs.Count(o=>o.OrderType==OrderType.StopMarket)); Assert.Equal(limits,Legs.Count(o=>o.OrderType==OrderType.Limit));
    }
    public void Dispose() { Call("SelectAccount",""); Account.All.Clear(); }
}
