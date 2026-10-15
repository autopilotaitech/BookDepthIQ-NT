using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using BookDepthIQ.Core;
using BookDepthIQ.Dom;
using Xunit;


// Executes the REAL Ticket/MainWindow command handlers into an in-memory writer.
// Does NOT construct MainWindow normally, load/save user settings, start timers,
// connect BridgeClient, open a TCP socket, or send anything to NinjaTrader.
sealed class TicketHarness : IDisposable
{
    public readonly Settings Config = new() { Account="Sim101", Instrument="MNQ 12-26", SizeInput=4,
        SlTicks=16, TpTicks=new[]{32,64,0}, TpSplit="50/50", FeePerSide=0 };
    public readonly DomState State;
    public readonly MainWindow Window;
    public readonly DrawdownTracker Drawdown;
    public readonly TrailBook Trail=new();
    public readonly BeWatch Be=new();
    readonly MemoryStream output=new();
    readonly BridgeClient bridge;
    readonly StreamWriter writer;
    public TicketHarness()
    {
        State=new DomState(Config,Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"))
        { Account="Sim101", Instrument="MNQ 12-26", Connected=true, TickSize=.25, PointValue=2,
            Cash=50000, LastTick=120000, LastFeedMs=Environment.TickCount64,
            Book=new Book(new BookSide(new long[]{120000},new long[]{100}),new BookSide(new long[]{120001},new long[]{100}),0) };
        Drawdown=new DrawdownTracker(Config.Rules);
        Window=(MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        bridge=new BridgeClient(State); // Never Start(): there is no connection.
        writer=new StreamWriter(output,new UTF8Encoding(false),1024,true){AutoFlush=true,NewLine="\n"};
        Set(bridge,"writer",writer);
        Set(Window,"cfg",Config); Set(Window,"s",State); Set(Window,"dd",Drawdown); Set(Window,"bridge",bridge);
        Set(Window,"notice",new TextBlock()); Set(Window,"sizeMode",new Seg(new[]{"CONTRACTS","$ RISK","% ROOM"}));
        Set(Window,"trailBox",new Seg(new[]{"OFF","R","ST1","ST2"}));
        Set(Window,"be",Be); Set(Window,"trail",Trail); Set(Window,"guard",new StopGuard());
        Set(Window,"managementAccount","Sim101");
        State.ExecutionReceived += e=>Call("OnExecutionReceived",e);
        State.CommandAcknowledged += (cid,ok)=>Call("OnCommandAcknowledged",cid,ok);
        State.TradingSessionReset += ()=>Call("ClearManagement");
        State.AccountChanged += ()=>Call("ClearManagement");
        Message("{\"t\":\"hello\",\"v\":2,\"orderVersion\":1,\"accounts\":[]}");
        Message("{\"t\":\"account\",\"name\":\"Sim101\",\"cash\":50000}");
        FlatSnapshot(); EmptyOrders();
    }
    public void Message(string json) { using var d=JsonDocument.Parse(json); State.Apply(d.RootElement); }
    public static void Set(object o,string name,object? value) => o.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(o,value);
    public object? Call(string name,params object[] args)
    {
        try { return typeof(MainWindow).GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(Window,args); }
        catch(TargetInvocationException e) { ExceptionDispatchInfo.Capture(e.InnerException!).Throw(); throw; }
    }
    public JsonElement[] Commands => Encoding.UTF8.GetString(output.ToArray()).Split('\n',StringSplitOptions.RemoveEmptyEntries)
        .Select(s=>JsonDocument.Parse(s).RootElement.Clone()).ToArray();
    public void Position(int qty,double price=30000) => State.Positions[State.Instrument]=(qty,price);
    public void Manage() { Set(Window,"lastMgmtMs",0L); Call("Manage"); }
    public void FlatSnapshot() => Message("{\"t\":\"pos\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"qty\":0,\"avg\":0}");
    public void EmptyOrders() => Message("{\"t\":\"orders\",\"acct\":\"Sim101\",\"list\":[]}");
    public void Ack(bool ok) => Message(JsonSerializer.Serialize(new {t="ack",cid=Commands[0].GetProperty("cid").GetString(),ok}));
    public void Stop(string id,int qty,double price=29996) => State.Orders.Add(new WorkingOrder(id,State.Instrument,"Sell","StopMarket",qty,0,0,price,"Working","o"+id,"BDIQ SL"+id));
    public void Dispose() { bridge.Dispose(); writer.Dispose(); output.Dispose(); }
}

public class TicketAuditTests
{
    [AvaloniaFact]
    public void Reverse_validates_captured_stop_risk_after_flatten_loss_not_a_new_smaller_swing()
    {
        using var h=new TicketHarness(); h.Config.StopMode=StopMode.Swing; h.Config.TpR=new double[]{2,0,0};
        h.Config.DayLossUsd=40; h.Config.Days["Sim101"]=new DayState(Protect.TradingDay(DomState.NowMs()),50000);
        h.State.Bars.Add(DomState.NowMs()-60000,119990); h.State.Bars.Add(DomState.NowMs(),120010);
        h.Position(4); h.Call("Reverse"); Assert.Single(h.Commands);
        h.State.Cash=49980; h.State.Bars.Clear(); h.State.Bars.Add(DomState.NowMs(),120000);
        h.Ack(true); h.FlatSnapshot(); h.EmptyOrders(); h.Manage(); Assert.Single(h.Commands);
        Assert.Contains("risks",h.State.LastResult);
    }

    [AvaloniaFact]
    public void Individual_ladder_cancel_aborts_waiting_reverse()
    {
        using var h=new TicketHarness(); h.Position(4); h.Call("Reverse");
        var order=new WorkingOrder("limit",h.State.Instrument,"Buy","Limit",1,0,29990,0,"Working","","BDIQ entry");
        h.Call("CancelWorkingOrder",order); h.Ack(true); h.FlatSnapshot(); h.EmptyOrders(); h.Manage();
        Assert.Equal(2,h.Commands.Length); Assert.Equal("cancel",h.Commands[1].GetProperty("t").GetString());
    }

    [AvaloniaFact]
    public void Be_watch_survives_flat_interval_when_other_scale_entries_remain()
    {
        using var h=new TicketHarness(); h.Config.BeAfterTp1=true; h.Be.Arm(h.State.Instrument,1);
        h.Position(2); h.State.Orders.Add(new WorkingOrder("limit",h.State.Instrument,"Buy","Limit",1,0,29990,0,"Working","","BDIQ entry"));
        h.Manage(); h.Position(0); h.Manage(); Assert.Equal(h.State.Instrument,h.Be.Inst);
    }

    [AvaloniaFact]
    public void Old_bridge_must_not_accept_entries_with_new_scale_semantics()
    {
        using var h=new TicketHarness();
        h.Message("{\"t\":\"hello\",\"v\":2,\"accounts\":[]}");
        h.Message("{\"t\":\"account\",\"name\":\"Sim101\",\"cash\":50000}");
        h.FlatSnapshot(); h.EmptyOrders();
        h.Call("Market",true); Assert.Empty(h.Commands); Assert.Contains("bridge",h.State.LastResult!,StringComparison.OrdinalIgnoreCase);
        h.Call("Flatten"); Assert.Single(h.Commands);
    }

    [AvaloniaFact]
    public void Reconnect_between_management_ticks_aborts_old_reverse()
    {
        using var h=new TicketHarness(); h.Position(4); h.Call("Reverse");
        h.State.Connected=false;
        h.Message("{\"t\":\"hello\",\"v\":2,\"orderVersion\":1,\"accounts\":[]}");
        h.Message("{\"t\":\"account\",\"name\":\"Sim101\",\"cash\":50000}");
        h.FlatSnapshot(); h.EmptyOrders(); h.Ack(true); h.Manage(); Assert.Single(h.Commands);
    }

    [AvaloniaFact]
    public void Balance_only_message_is_not_a_complete_trading_snapshot()
    {
        using var h=new TicketHarness();
        h.Message("{\"t\":\"hello\",\"v\":2,\"orderVersion\":1,\"accounts\":[]}");
        h.Message("{\"t\":\"account\",\"name\":\"Sim101\",\"cash\":50000}");
        h.Call("Market",true); Assert.Empty(h.Commands);
        h.FlatSnapshot(); h.EmptyOrders(); h.Call("Market",true); Assert.Single(h.Commands);
    }

    [AvaloniaFact]
    public void Selected_broker_disconnect_aborts_reverse_even_with_local_socket_up()
    {
        using var h=new TicketHarness(); h.Position(4); h.Call("Reverse");
        h.Message("{\"t\":\"accounts\",\"accounts\":[{\"name\":\"Sim101\",\"conn\":\"Disconnected\"}]}");
        h.FlatSnapshot(); h.EmptyOrders(); h.Ack(true); h.Manage(); Assert.Single(h.Commands);
    }

    [AvaloniaFact]
    public void Bracket_off_warning_remains_visible_when_ticket_is_hidden()
    {
        using var h=new TicketHarness(); h.Config.BracketOn=false; h.Config.ShowTicket=false;
        foreach(var name in new[]{"riskLine","checkLine","costLine","lockLine","resultBar","protectionLine"}) TicketHarness.Set(h.Window,name,new TextBlock());
        TicketHarness.Set(h.Window,"readout",new Dictionary<string,TextBlock>());
        TicketHarness.Set(h.Window,"acctBox",new ComboBox());
        TicketHarness.Set(h.Window,"armBtn",new Avalonia.Controls.Primitives.ToggleButton());
        TicketHarness.Set(h.Window,"bracketBtn",new Avalonia.Controls.Primitives.ToggleButton());
        h.Call("RefreshTicket");
        var warning=(TextBlock)typeof(MainWindow).GetField("protectionLine",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(h.Window)!;
        Assert.True(warning.IsVisible); Assert.Contains("OFF",warning.Text); Assert.Contains("no TP/SL",warning.Text);
    }
    [AvaloniaFact]
    public void Reverse_waits_for_ack_fresh_flat_and_empty_orders_then_sends_exact_protected_size_once()
    {
        using var h=new TicketHarness(); h.Position(4); h.Config.SizeInput=1; h.Call("Reverse");
        h.FlatSnapshot(); h.EmptyOrders(); h.Manage(); Assert.Single(h.Commands);
        h.Ack(true); h.Manage(); Assert.Equal(2,h.Commands.Length);
        var entry=h.Commands[1]; Assert.Equal(4,entry.GetProperty("qty").GetInt32());
        Assert.Equal("sell",entry.GetProperty("side").GetString()); Assert.Equal(16,entry.GetProperty("sl").GetInt32());
        Assert.Equal("[[32,2],[64,2]]",entry.GetProperty("targets").GetRawText());
        h.Manage(); Assert.Equal(2,h.Commands.Length);
    }

    [AvaloniaFact]
    public void Refused_flatten_and_user_cancel_never_reenter()
    {
        using var h=new TicketHarness(); h.Position(4); h.Call("Reverse"); h.Ack(false);
        h.FlatSnapshot(); h.EmptyOrders(); h.Manage(); Assert.Single(h.Commands);
        h.Position(4); h.Call("Reverse"); h.Call("CancelAll");
        h.FlatSnapshot(); h.EmptyOrders(); h.Manage(); Assert.Equal(3,h.Commands.Length);
        Assert.DoesNotContain(h.Commands,c=>c.GetProperty("t").GetString()=="order");
    }

    [AvaloniaFact]
    public void Reverse_does_not_reenter_while_old_orders_remain()
    {
        using var h=new TicketHarness(); h.Position(4); h.Call("Reverse"); h.Ack(true); h.FlatSnapshot();
        h.EmptyOrders(); h.Stop("1",4); h.Manage(); Assert.Single(h.Commands);
    }

    [AvaloniaFact]
    public void Account_switch_back_requires_new_snapshot_and_clears_old_balances()
    {
        using var h=new TicketHarness(); h.State.Account="Sim102"; h.State.Account="Sim101";
        Assert.False(h.State.HasAccountSnapshot); Assert.Equal(0,h.State.Cash);
    }

    [AvaloniaFact]
    public void Reverse_requires_flat_snapshot_for_its_own_instrument()
    {
        using var h=new TicketHarness(); h.Position(4); h.Call("Reverse"); h.Ack(true);
        h.State.Positions.Clear(); h.EmptyOrders();
        h.Message("{\"t\":\"pos\",\"acct\":\"Sim101\",\"inst\":\"NQ 12-26\",\"qty\":0}");
        h.Manage(); Assert.Single(h.Commands);
    }

    [AvaloniaFact]
    public void Expired_reverse_never_reenters_on_late_confirmation()
    {
        using var h=new TicketHarness(); h.Position(4); h.Call("Reverse"); h.Ack(true);
        var field=typeof(MainWindow).GetField("reverseIntent",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var intent=field.GetValue(h.Window)!;
        intent.GetType().GetProperty("Expires")!.SetValue(intent,DomState.NowMs()-1);
        h.FlatSnapshot(); h.EmptyOrders(); h.Manage(); Assert.Single(h.Commands);
    }

    [AvaloniaFact]
    public void Cancelled_pending_entry_does_not_leave_trailing_armed()
    {
        using var h=new TicketHarness(); h.Config.Trail=TrailMode.R; h.Call("Place",true,119980L,false);
        TicketHarness.Set(h.Window,"managementEntryUntil",DomState.NowMs()-1);
        h.EmptyOrders(); h.Manage(); Assert.False(h.Trail.Has(h.State.Instrument));
    }

    [AvaloniaFact]
    public void Pending_limit_trail_works_after_delayed_fill()
    {
        using var h=new TicketHarness(); h.Config.Trail=TrailMode.R; h.Call("Place",true,119980L,false);
        h.Manage(); Assert.True(h.Trail.Has(h.State.Instrument));
        h.Position(4); h.Stop("1",4); h.State.LastTick=120040; h.Manage();
        Assert.Equal("move",h.Commands[1].GetProperty("t").GetString());
        Assert.Equal(30006,h.Commands[1].GetProperty("price").GetDouble());
    }

    [AvaloniaFact]
    public void Tp1_execution_requires_complete_target_and_deduplicates_notifications()
    {
        var b=new BeWatch(); b.Arm("MNQ",1,2,true); b.Check(4,2);
        var fill=new Exec("first","MNQ","BDIQ TP1",false,1,30008,0,1);
        b.OnExecution(fill); b.OnExecution(fill); Assert.Equal("idle",b.Check(3,2));
        b.OnExecution(fill with {Id="wrong",Name="BDIQ TP2"}); Assert.Equal("idle",b.Check(2,1));
        b.OnExecution(fill with {Id="other",Inst="NQ"}); Assert.Equal("idle",b.Check(2,1));
        b.OnExecution(fill with {Id="last"}); Assert.Equal("fire",b.Check(2,1));
    }

    [AvaloniaFact]
    public void Identified_tp1_execution_moves_remaining_stop_once()
    {
        using var h=new TicketHarness(); h.Config.BeAfterTp1=true; h.Call("Market",true);
        h.Position(2); h.Stop("2",2);
        h.Message("{\"t\":\"exec\",\"acct\":\"Sim101\",\"inst\":\"MNQ 12-26\",\"id\":\"tp1\",\"name\":\"BDIQ TP1\",\"side\":\"s\",\"qty\":2,\"price\":30008,\"ms\":1}");
        h.Manage(); Assert.Equal("move",h.Commands[1].GetProperty("t").GetString());
        h.Manage(); Assert.Equal(2,h.Commands.Length);
    }
    [AvaloniaFact]
    public void Three_targets_preserve_total_size_and_fee_inclusive_risk()
    {
        using var h=new TicketHarness(); h.Config.SizeInput=10; h.Config.FeePerSide=1;
        h.Config.TpTicks=new[]{32,64,96}; h.Config.TpSplit="50/30/20";
        var ticket=Ticket.For(h.Config,h.State,h.Drawdown,true);
        Assert.Equal(100,ticket.Risk.Loss); Assert.Equal(140,ticket.Risk.Win);
        Assert.Equal(new[]{5,3,2},ticket.Slices(true).Select(p=>p.Qty));
    }

    [AvaloniaFact]
    public void Atr_after_history_sends_atr_stop_and_r_targets()
    {
        using var h=new TicketHarness(); h.Config.StopMode=StopMode.Atr; h.Config.AtrMult=2;
        h.Config.TpR=new double[]{2,0,0};
        for(var i=0;i<20;i++) h.State.Trend.OnTrade(i*60000L,120000+i*4,1,true);
        h.Call("Market",true); var c=Assert.Single(h.Commands);
        Assert.Equal(8,c.GetProperty("sl").GetInt32()); Assert.Equal("[[16,4]]",c.GetProperty("targets").GetRawText());
    }

    [AvaloniaTheory]
    [InlineData(true,13)] [InlineData(false,12)]
    public void Swing_stop_uses_correct_side_and_buffer(bool buy,int expected)
    {
        using var h=new TicketHarness(); h.Config.StopMode=StopMode.Swing;
        h.Config.TpR=new double[]{2,0,0};
        h.State.Bars.Add(DomState.NowMs()-60000,119990); h.State.Bars.Add(DomState.NowMs(),120010);
        h.Call("Market",buy); Assert.Equal(expected,Assert.Single(h.Commands).GetProperty("sl").GetInt32());
    }

    [AvaloniaFact]
    public void Swing_without_history_refuses_entry()
    {
        using var h=new TicketHarness(); h.Config.StopMode=StopMode.Swing; h.Call("Market",false); Assert.Empty(h.Commands);
    }

    [AvaloniaFact]
    public void Max_contract_cap_is_applied_before_sending()
    {
        using var h=new TicketHarness(); h.Config.SizeInput=10; h.Config.MaxContracts=2; h.Call("Market",true);
        Assert.Equal(2,Assert.Single(h.Commands).GetProperty("qty").GetInt32());
    }

    [AvaloniaTheory]
    [InlineData(1,16,1)] [InlineData(11,16,1)] [InlineData(4,1,1)] [InlineData(4,16,.5)]
    public void Invalid_scale_parameters_refuse_all_entries(int count,int span,double skew)
    {
        using var h=new TicketHarness(); h.Config.ScaleOn=true; h.Config.ScaleCount=count;
        h.Config.ScaleSpanTicks=span; h.Config.ScaleSkew=skew; h.Call("Place",true,120000L,false); Assert.Empty(h.Commands);
    }

    [AvaloniaTheory]
    [InlineData(SkewDir.Start)] [InlineData(SkewDir.Even)] [InlineData(SkewDir.End)]
    public void Scale_allocates_total_without_duplicate_prices(SkewDir dir)
    {
        using var h=new TicketHarness(); h.Config.SizeInput=20; h.Config.ScaleOn=true;
        h.Config.ScaleSkew=2; h.Config.ScaleDir=dir; h.Call("Place",true,120000L,false);
        Assert.Equal(20,h.Commands.Sum(c=>c.GetProperty("qty").GetInt32()));
        Assert.Equal(4,h.Commands.Select(c=>c.GetProperty("price").GetDouble()).Distinct().Count());
    }

    [AvaloniaFact]
    public void Disconnected_and_lost_writer_do_not_send()
    {
        using var h=new TicketHarness(); h.State.Connected=false; h.Call("Market",true); Assert.Empty(h.Commands);
        h.State.Connected=true;
        h.Message("{\"t\":\"account\",\"name\":\"Sim101\",\"cash\":50000}"); h.FlatSnapshot(); h.EmptyOrders();
        var bridge=typeof(MainWindow).GetField("bridge",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(h.Window)!;
        TicketHarness.Set(bridge,"writer",null); h.Call("Market",true); Assert.Empty(h.Commands);
        Assert.Contains("dropped",h.State.LastResult);
    }

    [AvaloniaFact]
    public void New_entry_must_wait_for_selected_account_snapshot()
    {
        using var h=new TicketHarness(); h.Message("{\"t\":\"hello\",\"v\":2,\"accounts\":[]}"); Assert.False(h.State.HasAccountSnapshot);
        h.Call("Market",true); Assert.Empty(h.Commands);
    }

    [AvaloniaFact]
    public void Break_even_adds_missing_stop_and_rejects_invalid_market_side()
    {
        using var h=new TicketHarness(); h.Position(2);
        h.Call("BreakEven"); Assert.Empty(h.Commands); // At entry, +1t is above bid.
        h.State.Book=new Book(new BookSide(new long[]{120020},new long[]{100}),new BookSide(new long[]{120021},new long[]{100}),0);
        h.Call("BreakEven"); var c=Assert.Single(h.Commands);
        Assert.Equal("addStop",c.GetProperty("t").GetString()); Assert.Equal(30000.25,c.GetProperty("price").GetDouble());
    }

    [AvaloniaFact]
    public void Short_break_even_is_below_entry()
    {
        using var h=new TicketHarness(); h.Position(-2);
        h.State.Book=new Book(new BookSide(new long[]{119980},new long[]{100}),new BookSide(new long[]{119981},new long[]{100}),0);
        h.Call("BreakEven"); Assert.Equal(29999.75,Assert.Single(h.Commands).GetProperty("price").GetDouble());
    }

    [AvaloniaFact]
    public void Turning_be_off_must_disarm_existing_watch()
    {
        using var h=new TicketHarness(); h.Config.BeAfterTp1=false; h.Be.Arm(h.State.Instrument,1); h.Be.Check(4,2);
        h.Position(2); h.Stop("1",2); h.State.Orders.Add(new WorkingOrder("tp",h.State.Instrument,"Sell","Limit",2,0,30016,0,"Working","o1","BDIQ TP2"));
        h.Call("Manage"); Assert.Empty(h.Commands);
    }

    [AvaloniaTheory]
    [InlineData(false,0)] [InlineData(true,1)]
    public void Hotkeys_require_arm_for_entry(bool armed,int count)
    {
        using var h=new TicketHarness(); h.Config.HotkeysArmed=armed;
        h.Call("OnKey",null!,new Avalonia.Input.KeyEventArgs { Key=Avalonia.Input.Key.B }); Assert.Equal(count,h.Commands.Length);
    }

    [Fact]
    public void Trail_math_handles_short_and_supertrend_without_loosening()
    {
        Assert.Equal(100,Manage.TrailTarget(false,100,10,90,1,110));
        Assert.Null(Manage.TrailTarget(false,100,10,91,1,110));
        Assert.Equal(95,Manage.LineTrailTarget(false,new TrendLine(95,TrendDir.Down),100));
        Assert.Null(Manage.LineTrailTarget(false,new TrendLine(105,TrendDir.Down),100));
        Assert.Null(Manage.LineTrailTarget(false,new TrendLine(95,TrendDir.Up),100));
    }

    [Fact]
    public void Stop_guard_retries_only_after_confirmation_and_forgets_closed_positions()
    {
        var g=new StopGuard(3000,10000);
        Assert.Empty(g.Check(new[]{("Sim101:MNQ",(double?)29990)},0));
        Assert.Empty(g.Check(new[]{("Sim101:MNQ",(double?)null)},1000));
        Assert.Single(g.Check(new[]{("Sim101:MNQ",(double?)null)},4000));
        Assert.Empty(g.Check(new[]{("Sim101:MNQ",(double?)null)},5000));
        g.Check(Array.Empty<(string,double?)>(),6000);
        Assert.Empty(g.Check(new[]{("Sim101:MNQ",(double?)null)},20000));
    }

    [AvaloniaTheory]
    [InlineData(true)] [InlineData(false)]
    public void Market_entry_sends_size_and_full_bracket(bool buy)
    {
        using var h=new TicketHarness(); h.Call("Market",buy);
        var c=Assert.Single(h.Commands);
        Assert.Equal(buy?"buy":"sell",c.GetProperty("side").GetString());
        Assert.Equal(4,c.GetProperty("qty").GetInt32()); Assert.Equal(16,c.GetProperty("sl").GetInt32());
        Assert.Equal("[[32,2],[64,2]]",c.GetProperty("targets").GetRawText());
    }

    [AvaloniaTheory]
    [InlineData(true,false)] [InlineData(false,false)] [InlineData(true,true)] [InlineData(false,true)]
    public void Ladder_limit_and_stop_entries_preserve_brackets(bool buy,bool stop)
    {
        using var h=new TicketHarness(); h.Call("Place",buy,120010L,stop);
        var c=Assert.Single(h.Commands); Assert.Equal(stop?"stop":"limit",c.GetProperty("type").GetString());
        Assert.Equal(30002.5,c.GetProperty("price").GetDouble());
        Assert.Equal(16,c.GetProperty("sl").GetInt32()); Assert.Equal(2,c.GetProperty("targets").GetArrayLength());
    }

    [AvaloniaFact]
    public void Bracket_off_explicitly_sends_no_legs()
    {
        using var h=new TicketHarness(); h.Config.BracketOn=false; h.Call("Market",true);
        var c=Assert.Single(h.Commands); Assert.Equal(0,c.GetProperty("sl").GetInt32()); Assert.Equal(0,c.GetProperty("targets").GetArrayLength());
    }

    [AvaloniaFact]
    public void Stop_only_entry_protects_full_size()
    {
        using var h=new TicketHarness(); h.Config.TpTicks=new[]{0,0,0}; h.Call("Market",true);
        var c=Assert.Single(h.Commands); Assert.Equal("[[0,4]]",c.GetProperty("targets").GetRawText()); Assert.Equal(16,c.GetProperty("sl").GetInt32());
    }

    [AvaloniaTheory]
    [InlineData(SizeMode.Contracts,4,4)] [InlineData(SizeMode.RiskUsd,40,5)] [InlineData(SizeMode.RoomPct,1,3)]
    public void Sizing_modes_match_independent_risk_math(SizeMode mode,double input,int expected)
    {
        using var h=new TicketHarness(); h.Config.SizeMode=mode; h.Config.SizeInput=input;
        h.Call("Market",true); Assert.Equal(expected,Assert.Single(h.Commands).GetProperty("qty").GetInt32());
    }

    [AvaloniaFact]
    public void Too_small_target_split_refuses_entry()
    {
        using var h=new TicketHarness(); h.Config.SizeInput=1; h.Call("Market",true);
        Assert.Empty(h.Commands); Assert.Contains("too small",h.State.LastResult);
    }

    [AvaloniaFact]
    public void Atr_without_history_refuses_entry()
    {
        using var h=new TicketHarness(); h.Config.StopMode=StopMode.Atr; h.Call("Market",true);
        Assert.Empty(h.Commands); Assert.Contains("ATR",h.State.LastResult);
    }

    [AvaloniaTheory]
    [InlineData(true)] [InlineData(false)]
    public void Scale_with_bracket_enabled_must_protect_each_child(bool buy)
    {
        using var h=new TicketHarness(); h.Config.ScaleOn=true; h.Call("Place",buy,120000L,false);
        Assert.Equal(4,h.Commands.Length); Assert.Equal(4,h.Commands.Sum(c=>c.GetProperty("qty").GetInt32()));
        Assert.All(h.Commands,c=>Assert.Equal(16,c.GetProperty("sl").GetInt32()));
    }

    [AvaloniaFact]
    public void Reverse_must_wait_for_flat_confirmation_before_new_entry()
    {
        using var h=new TicketHarness(); h.Position(4); h.Call("Reverse");
        var c=Assert.Single(h.Commands); Assert.Equal("flatten",c.GetProperty("t").GetString());
    }

    [AvaloniaFact]
    public void Limit_entry_trailing_must_survive_while_waiting_for_fill()
    {
        using var h=new TicketHarness(); h.Config.Trail=TrailMode.R;
        h.Call("Place",true,119980L,false); Assert.True(h.Trail.Has(h.State.Instrument));
        h.Call("Manage"); Assert.True(h.Trail.Has(h.State.Instrument));
    }

    [AvaloniaFact]
    public void Confirmed_tp1_fill_fires_be_once_not_on_order_count_changes()
    {
        var b=new BeWatch(); b.Arm("MNQ",1); b.Check(4,2);
        b.OnExecution(new Exec("tp1","MNQ","BDIQ TP1",false,2,30008,0,1));
        Assert.Equal("fire",b.Check(2,1)); Assert.Equal("idle",b.Check(2,1));
    }

    [AvaloniaFact]
    public void Scale_preserves_global_target_identity_and_total_allocation()
    {
        using var h=new TicketHarness(); h.Config.ScaleOn=true; h.Call("Place",true,120000L,false);
        var targets=h.Commands.SelectMany(c=>c.GetProperty("targets").EnumerateArray()).ToArray();
        Assert.Equal(4,targets.Length);
        Assert.Equal(new[]{32,32,64,64},targets.Select(t=>t[0].GetInt32()));
        Assert.Equal(new[]{1,1,2,2},targets.Select(t=>t[2].GetInt32()));
    }

    [AvaloniaFact]
    public void Turning_trail_off_must_stop_existing_trail()
    {
        using var h=new TicketHarness(); h.Config.Trail=TrailMode.Off; h.Trail.Arm(h.State.Instrument,16,1);
        h.Position(4); h.Stop("1",4); h.State.LastTick=120040;
        h.Call("Manage"); Assert.Empty(h.Commands);
    }

    [AvaloniaFact]
    public void Management_must_not_follow_instrument_to_different_account()
    {
        using var h=new TicketHarness(); h.Trail.Arm(h.State.Instrument,16,1);
        h.State.Account="Sim102"; h.Position(4); h.Stop("1",4); h.State.LastTick=120040;
        h.Call("Manage"); Assert.Empty(h.Commands);
    }

    [AvaloniaTheory]
    [InlineData("Flatten","flatten")] [InlineData("CancelAll","cancelAll")] [InlineData("Kill","kill")]
    public void Emergency_exits_work_even_when_entry_size_invalid(string method,string command)
    {
        using var h=new TicketHarness(); h.Config.SizeInput=0; h.Call(method);
        Assert.Equal(command,Assert.Single(h.Commands).GetProperty("t").GetString());
    }

    [AvaloniaTheory]
    [InlineData(1,0)] [InlineData(3,1)] [InlineData(-5,2)]
    public void Close_half_rounds_down_and_refuses_one_contract(int position,int expected)
    {
        using var h=new TicketHarness(); h.Position(position); h.Call("CloseHalf");
        if(expected==0) Assert.Empty(h.Commands);
        else Assert.Equal(expected,Assert.Single(h.Commands).GetProperty("qty").GetInt32());
    }

    [AvaloniaFact]
    public void Manual_break_even_moves_every_protective_leg()
    {
        using var h=new TicketHarness(); h.Position(4); h.Stop("1",2); h.Stop("2",2);
        h.State.Book=new Book(new BookSide(new long[]{120020},new long[]{100}),new BookSide(new long[]{120021},new long[]{100}),0);
        h.Call("BreakEven"); Assert.Equal(2,h.Commands.Length);
        Assert.All(h.Commands,c=>Assert.Equal(30000.25,c.GetProperty("price").GetDouble()));
    }

    [AvaloniaFact]
    public void Preview_produces_legs_without_sending_orders()
    {
        using var h=new TicketHarness(); h.Config.Preview=PreviewMode.Long;
        var legs=(IReadOnlyList<(int,long,bool,string)>)h.Call("PreviewLegs")!;
        Assert.Equal(3,legs.Count); Assert.Equal(119985,legs[0].Item2); Assert.Equal(120033,legs[1].Item2); Assert.Empty(h.Commands);
    }

    [AvaloniaFact]
    public void Be_after_tp1_must_not_fire_on_manual_partial_close_with_target_cancel()
    {
        var b=new BeWatch(); b.Arm("MNQ",1); b.Check(4,2);
        // ClosePart trims a far target; that is NOT evidence of a TP1 execution.
        Assert.Equal("idle",b.Check(2,1));
    }
}
