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
    void Manual(string side,string kind,int qty,double price)
    {Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\""+side+"\",\"type\":\""+kind+"\",\"price\":"+price.ToString(System.Globalization.CultureInfo.InvariantCulture)+",\"qty\":"+qty+"}");}
    [Theory]
    [InlineData("buy","sell",true)] [InlineData("buy","sell",false)]
    [InlineData("sell","buy",true)] [InlineData("sell","buy",false)]
    public void Manual_stop_and_targets_have_independent_coverage_and_shrink_on_actual_fills(string opening,string closing,bool stopFirst)
    {
        Manual(opening,"market",3,0);account.Fill(Entry,3,30000);
        if(stopFirst) Manual(closing,"stop",3,opening=="buy"?29990:30010);
        Manual(closing,"limit",1,opening=="buy"?30010:29990);
        Manual(closing,"limit",2,opening=="buy"?30020:29980);
        if(!stopFirst) Manual(closing,"stop",3,opening=="buy"?29990:30010);
        var exits=account.Orders.Skip(1).ToArray();Assert.Equal(3,exits.Length);
        Assert.All(exits,o=>Assert.True(Account.Working(o)));
        var stop=exits.Single(o=>o.OrderType==OrderType.StopMarket);
        var targets=exits.Where(o=>o.OrderType==OrderType.Limit).ToArray();
        account.Fill(targets[1],1,30000);Assert.Equal(2,stop.Quantity-stop.Filled);
        Assert.True(Account.Working(targets[0]));Assert.True(Account.Working(stop));
        account.Fill(stop,1,30000);
        Assert.Equal(1,account.Orders.Where(o=>Account.Working(o)&&o.OrderType==OrderType.Limit).Sum(o=>o.Quantity-o.Filled));
        account.Fill(stop,1,30000);Assert.Equal(MarketPosition.Flat,account.Positions.Single().MarketPosition);
        Assert.DoesNotContain(account.Orders,Account.Working);Assert.Equal(4,account.Orders.Count);
    }
    [Fact] public void Independent_exit_families_still_reject_duplicate_overcoverage()
    {
        Manual("buy","market",3,0);account.Fill(Entry,3,30000);
        Manual("sell","stop",3,29990);Manual("sell","limit",3,30010);
        Manual("sell","stop",1,29980);Manual("sell","limit",1,30020);
        Assert.Equal(3,account.Orders.Count);
        Assert.Equal(3,account.Orders.Where(o=>Account.Working(o)&&o.OrderType==OrderType.StopMarket).Sum(o=>o.Quantity-o.Filled));
        Assert.Equal(3,account.Orders.Where(o=>Account.Working(o)&&o.OrderType==OrderType.Limit).Sum(o=>o.Quantity-o.Filled));
    }
    [Fact] public void Manual_stop_remains_until_close_half_fills_and_then_resizes()
    {
        Manual("buy","market",4,0);account.Fill(Entry,4,30000);Manual("sell","stop",4,29990);
        var stop=account.Orders.Last();
        Command("{\"t\":\"closePart\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"qty\":2}");
        var close=account.Orders.Last();Assert.Equal("BDIQ close",close.Name);Assert.Equal(4,stop.Quantity);
        account.Fill(close,2,30000);Assert.Equal(2,stop.Quantity-stop.Filled);
    }
    [Fact] public void Manual_exit_reconciliation_recovers_after_delayed_change_confirmation()
    {
        Manual("buy","market",3,0);account.Fill(Entry,3,30000);
        Manual("sell","stop",3,29990);Manual("sell","limit",3,30010);
        var stop=account.Orders.Single(o=>o.OrderType==OrderType.StopMarket);var target=account.Orders.Single(o=>o.OrderType==OrderType.Limit);
        account.DeferChanges=true;account.Fill(target,1,30010);account.Fill(target,1,30010);
        account.ConfirmChanges();Assert.Equal(1,stop.Quantity-stop.Filled);
        account.DeferCancels=true;account.Fill(target,1,30010);Assert.Equal(OrderState.CancelPending,stop.OrderState);
        account.ConfirmCancels();Assert.DoesNotContain(account.Orders,Account.Working);
    }
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
        account.Fill(account.Orders.Single(o=>o.Name=="BDIQ close"),2,30000);
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
    [Fact]
    public void Native_tp_fill_to_flat_cancels_its_stop_and_creates_no_new_orders()
    {
        Command("{\"t\":\"order\",\"cid\":\"open\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":2,\"targets\":[[32,2]],\"sl\":16}");
        account.Fill(Entry,2,30000);
        var tp=Legs.Single(o=>o.Name=="BDIQ TP1"); var stop=Legs.Single(o=>o.Name=="BDIQ SL1");
        var before=account.Orders.Count;
        account.Fill(tp,2,30008);
        Assert.Equal(0,account.Positions.Single().Quantity);
        Assert.Equal(OrderState.Cancelled,stop.OrderState);
        Assert.Equal(before,account.Orders.Count);
        Assert.DoesNotContain(account.Orders,Account.Working);
    }

    [Fact]
    public void Long_ten_ladder_sell_four_does_not_create_short_side_brackets()
    {
        OpenLongAndReduceFour();
        Assert.Equal(6,account.Positions.Single().Quantity);
        Assert.Equal(MarketPosition.Long,account.Positions.Single().MarketPosition);
        Assert.DoesNotContain(account.Orders,o=>o.Name.StartsWith("BDIQ SL") && o.OrderAction==OrderAction.BuyToCover);
        Assert.DoesNotContain(account.Orders,o=>o.Name.StartsWith("BDIQ TP") && o.OrderAction==OrderAction.BuyToCover);
    }

    [Fact]
    public void Long_ten_ladder_sell_four_leaves_exactly_six_contracts_of_long_protection()
    {
        OpenLongAndReduceFour();
        Assert.Equal(6,account.Orders.Where(o=>Account.Working(o) && o.Name.StartsWith("BDIQ SL") && o.OrderAction==OrderAction.Sell)
            .Sum(o=>o.Quantity-o.Filled));
    }

    void OpenLongAndReduceFour()
    {
        Command("{\"t\":\"order\",\"cid\":\"open\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":10,\"targets\":[[32,10]],\"sl\":16}");
        account.Fill(Entry,10,30000);
        Command("{\"t\":\"order\",\"cid\":\"reduce\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"sell\",\"type\":\"limit\",\"price\":30002,\"qty\":4,\"targets\":[[32,4]],\"sl\":16}");
        account.Fill(account.Orders.Last(o=>o.Name=="BDIQ entry"),4,30002);
    }

    [Theory]
    [InlineData("buy","sell",MarketPosition.Long)]
    [InlineData("sell","buy",MarketPosition.Short)]
    public void Reducing_limit_partial_fills_keep_only_remaining_position_protected(string opening,string closing,MarketPosition side)
    {
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\""+opening+"\",\"type\":\"market\",\"qty\":10,\"targets\":[[32,10]],\"sl\":16}");
        account.Fill(Entry,10,30000);
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\""+closing+"\",\"type\":\"limit\",\"price\":30002,\"qty\":10,\"targets\":[[32,10]],\"sl\":16}");
        var reduce=account.Orders.Last(o=>o.Name=="BDIQ entry");
        foreach(var qty in new[]{4,2,3,1})
        {
            account.Fill(reduce,qty,30002);
            var remaining=account.Positions.Single().Quantity;
            Assert.Equal(remaining,account.Orders.Where(o=>Account.Working(o) && o.Name.StartsWith("BDIQ SL")).Sum(o=>o.Quantity-o.Filled));
            Assert.DoesNotContain(account.Orders,o=>o.Name.StartsWith("BDIQ SL") && (side==MarketPosition.Long?o.OrderAction==OrderAction.BuyToCover:o.OrderAction==OrderAction.Sell));
        }
        Assert.DoesNotContain(account.Orders,Account.Working);
    }

    [Fact]
    public void Queued_opening_brackets_are_not_submitted_after_position_already_flat()
    {
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":2,\"targets\":[[32,2]],\"sl\":16}");
        var app=new System.Windows.Application(); app.Dispatcher.Deferred=true;
        System.Windows.Application.Current=app;
        try
        {
            account.Fill(Entry,2,30000); account.Flatten(new[]{instrument});
            app.Dispatcher.Drain();
            Assert.DoesNotContain(account.Orders,o=>o.Name.StartsWith("BDIQ SL"));
            Assert.DoesNotContain(account.Orders,o=>o.Name.StartsWith("BDIQ TP"));
        }
        finally {System.Windows.Application.Current=null;}
    }

    [Fact]
    public void Close_half_does_not_remove_protection_before_the_close_fills()
    {
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":4,\"targets\":[[32,4]],\"sl\":16}");
        account.Fill(Entry,4,30000);
        Command("{\"t\":\"closePart\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"qty\":2}");
        Assert.Equal(4,Legs.Where(o=>o.OrderType==OrderType.StopMarket).Sum(o=>o.Quantity-o.Filled));
    }

    [Fact]
    public void Stale_reduce_only_command_cannot_open_from_flat()
    {
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"sell\",\"type\":\"limit\",\"price\":30002,\"qty\":4,\"reduceOnly\":true,\"targets\":[[32,4]],\"sl\":16}");
        Assert.Empty(account.Orders);
    }

    [Fact]
    public void Pending_reduction_shrinks_when_a_target_fills_first()
    {
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":4,\"targets\":[[32,2],[64,2]],\"sl\":16}");
        account.Fill(Entry,4,30000);
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"sell\",\"type\":\"limit\",\"price\":30002,\"qty\":4,\"reduceOnly\":true}");
        var reduce=account.Orders.Last(o=>o.Name=="BDIQ entry");
        account.Fill(Legs.Single(o=>o.Name=="BDIQ TP1"),2,30008);
        Assert.Equal(2,reduce.Quantity-reduce.Filled);
        account.Fill(reduce,2,30002);
        Assert.Equal(MarketPosition.Flat,account.Positions.Single().MarketPosition);
        Assert.DoesNotContain(account.Orders,Account.Working);
    }

    [Fact]
    public void Queued_old_brackets_do_not_attach_to_a_reopened_position()
    {
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":2,\"targets\":[[32,2]],\"sl\":16}");
        var app=new System.Windows.Application(); app.Dispatcher.Deferred=true; System.Windows.Application.Current=app;
        account.Fill(Entry,2,30000); account.Flatten(new[]{instrument});
        var reopen=account.CreateOrder(instrument,OrderAction.Buy,OrderType.Market,OrderEntry.Manual,TimeInForce.Gtc,2,0,0,"","manual",DateTime.MaxValue,null);
        account.Submit(new[]{reopen}); account.Fill(reopen,2,30100);
        app.Dispatcher.Drain();
        Assert.DoesNotContain(account.Orders,o=>o.Name.StartsWith("BDIQ SL") || o.Name.StartsWith("BDIQ TP"));
    }

    [Fact]
    public void New_fill_protection_is_retained_until_old_quantity_change_confirms()
    {
        OpenLongAndReduceFour();
        // OpenLongAndReduceFour has already reconciled to six; defer a further reduction's changes.
        account.DeferChanges=true;
        Command("{\"t\":\"closePart\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"qty\":2}");
        account.Fill(account.Orders.Last(o=>o.Name=="BDIQ close"),2,30002);
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":2,\"targets\":[[32,2]],\"sl\":16}");
        account.Fill(account.Orders.Last(o=>o.Name=="BDIQ entry"),2,30100);
        account.ConfirmChanges();
        Assert.Equal(6,Legs.Where(o=>o.Name.StartsWith("BDIQ SL")).Sum(o=>o.Quantity-o.Filled));
        Assert.Contains(Legs,o=>o.Name.StartsWith("BDIQ SL") && o.StopPrice==30096);
    }

    [Fact]
    public void Nonselected_account_remains_watched_during_queued_bracket_submission()
    {
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":2,\"targets\":[[32,2]],\"sl\":16}");
        Account.All.Add(new Account {Name="Sim102"}); Call("SelectAccount","Sim102");
        var app=new System.Windows.Application(); app.Dispatcher.Deferred=true; System.Windows.Application.Current=app;
        account.Fill(Entry,2,30000); app.Dispatcher.Drain();
        var manual=account.CreateOrder(instrument,OrderAction.Sell,OrderType.Market,OrderEntry.Manual,TimeInForce.Gtc,2,0,0,"","manual",DateTime.MaxValue,null);
        account.Submit(new[]{manual}); account.Fill(manual,2,30001); app.Dispatcher.Drain();
        Assert.DoesNotContain(account.Orders,Account.Working);
    }

    [Fact]
    public void New_position_protection_waits_for_old_cancel_confirmation_without_being_lost()
    {
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":2,\"targets\":[[32,2]],\"sl\":16}");
        account.Fill(Entry,2,30000); account.DeferCancels=true; account.Flatten(new[]{instrument});
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":2,\"targets\":[[32,2]],\"sl\":16}");
        account.Fill(account.Orders.Last(o=>o.Name=="BDIQ entry"),2,30100);
        account.ConfirmCancels();
        Assert.Equal(2,Legs.Where(o=>o.Name.StartsWith("BDIQ SL")).Sum(o=>o.Quantity-o.Filled));
        Assert.All(Legs.Where(o=>o.Name.StartsWith("BDIQ SL")),o=>Assert.Equal(30096,o.StopPrice));
    }

    [Theory]
    [InlineData("BDIQ TP1")] [InlineData("BDIQ SL1")]
    public void Partially_filled_protective_leg_shrinks_its_oco_sibling(string leg)
    {
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":4,\"targets\":[[32,4]],\"sl\":16}");
        account.Fill(Entry,4,30000);
        account.Fill(Legs.Single(o=>o.Name==leg),1,30000);
        Assert.Equal(3,Legs.Where(o=>o.Name.StartsWith("BDIQ SL")).Sum(o=>o.Quantity-o.Filled));
        Assert.Equal(3,Legs.Where(o=>o.Name.StartsWith("BDIQ TP")).Sum(o=>o.Quantity-o.Filled));
    }

    [Theory]
    [InlineData("[[32,1],[64,2]]")] [InlineData("[[32,3]]")]
    public void Partially_submitted_batch_resumes_without_losing_slices_or_sharing_oco_fragments(string targets)
    {
        OpenLongAndReduceFour(); account.DeferChanges=true;
        Command("{\"t\":\"closePart\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"qty\":2}");
        account.Fill(account.Orders.Last(o=>o.Name=="BDIQ close"),2,30002);
        Command("{\"t\":\"order\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"side\":\"buy\",\"type\":\"market\",\"qty\":3,\"targets\":"+targets+",\"sl\":16}");
        account.Fill(account.Orders.Last(o=>o.Name=="BDIQ entry"),3,30100);
        account.ConfirmChanges();
        Assert.Equal(7,Legs.Where(o=>o.Name.StartsWith("BDIQ SL")).Sum(o=>o.Quantity-o.Filled));
        Assert.All(Legs.GroupBy(o=>o.Oco),g=>Assert.Equal(2,g.Count()));
        Command("{\"t\":\"closePart\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"qty\":2}");
        account.Fill(account.Orders.Last(o=>o.Name=="BDIQ close"),2,30100);
        Assert.Equal(5,Legs.Where(o=>o.Name.StartsWith("BDIQ SL")).Sum(o=>o.Quantity-o.Filled));
    }

    public void Dispose() { Call("SelectAccount",""); Account.All.Clear(); System.Windows.Application.Current=null; }
}
