using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using BookDepthIQ.Dom;
using System.Reflection;
using System.Runtime.CompilerServices;
using Xunit;

public class LadderInputSafetyTests
{
    static (Window Window,LadderControl Ladder,Settings Settings,List<(bool Buy,long Row,bool Stop)> Orders,DomState State) Fixture(bool orderColumns=true)
    {
        var cfg=new Settings();cfg.Columns.Orders=orderColumns;
        var state=new DomState(cfg,Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json")) {LastTick=120000,TickSize=.25};
        var ladder=new LadderControl(state,cfg);var orders=new List<(bool,long,bool)>();ladder.PlaceRequested+=(b,r,s)=>orders.Add((b,r,s));
        var window=new Window {Width=1100,Height=600,SystemDecorations=SystemDecorations.None,Content=ladder};window.Show();
        using var frame=window.CaptureRenderedFrame();
        return(window,ladder,cfg,orders,state);
    }
    static Dictionary<string,double> Columns(LadderControl ladder)=>((Array)typeof(LadderControl).GetField("cols",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(ladder)!)
        .Cast<object>().Select(o=>(ITuple)o).ToDictionary(t=>t[0]!.ToString()!,t=>(double)t[1]!+(double)t[2]!/2);
    [AvaloniaFact] public void Only_explicit_buy_sell_columns_can_submit_and_only_on_left_release()
    {
        var f=Fixture();try
        {
            foreach(var c in Columns(f.Ladder))
            {
                f.Orders.Clear();var p=new Point(c.Value,200);f.Window.MouseDown(p,MouseButton.Left);Assert.Empty(f.Orders);
                f.Window.MouseUp(p,MouseButton.Left);Assert.Equal(c.Key is "Buy" or "Sell"?1:0,f.Orders.Count);
                if(f.Orders.Count>0) Assert.Equal(c.Key=="Buy",f.Orders[0].Buy);
                f.Orders.Clear();f.Window.MouseDown(p,MouseButton.Middle);f.Window.MouseUp(p,MouseButton.Middle);Assert.Empty(f.Orders);
            }
        } finally {f.Window.Close();}
    }
    [AvaloniaFact] public void Scroll_drag_and_hidden_order_columns_cannot_submit()
    {
        var f=Fixture();try
        {
            var p=new Point(Columns(f.Ladder)["Buy"],200);
            f.Window.MouseDown(p,MouseButton.Left);f.Window.MouseWheel(p,new Vector(0,1),RawInputModifiers.LeftMouseButton);f.Window.MouseUp(p,MouseButton.Left);Assert.Empty(f.Orders);
            f.Window.MouseDown(p,MouseButton.Left);f.Window.MouseMove(p+new Vector(0,30),RawInputModifiers.LeftMouseButton);f.Window.MouseUp(p+new Vector(0,30),MouseButton.Left);Assert.Empty(f.Orders);
            f.Window.MouseDown(p,MouseButton.Left);f.Window.MouseDown(p,MouseButton.Middle,RawInputModifiers.LeftMouseButton);f.Window.MouseUp(p,MouseButton.Middle,RawInputModifiers.LeftMouseButton);f.Window.MouseUp(p,MouseButton.Left);Assert.Empty(f.Orders);
            f.Window.MouseDown(p,MouseButton.Left,RawInputModifiers.Shift);f.Window.MouseUp(p,MouseButton.Left,RawInputModifiers.Shift);Assert.True(Assert.Single(f.Orders).Stop);
        } finally {f.Window.Close();}
        var hidden=Fixture(false);try
        {
            foreach(var c in Columns(hidden.Ladder).Where(c=>c.Key is "Bid" or "Ask")) {var p=new Point(c.Value,200);hidden.Window.MouseDown(p,MouseButton.Left);hidden.Window.MouseUp(p,MouseButton.Left);}
            Assert.Empty(hidden.Orders);
        } finally {hidden.Window.Close();}
    }
    [AvaloniaFact] public void Hover_explains_read_only_columns_and_does_not_send_orders()
    {
        var f=Fixture();try
        {
            f.Window.MouseMove(new Point(Columns(f.Ladder)["Bid"],200));Assert.Contains("read-only",ToolTip.GetTip(f.Ladder)?.ToString());Assert.Empty(f.Orders);
        } finally {f.Window.Close();}
    }
    [AvaloniaFact] public void Existing_order_drag_and_right_cancel_remain_management_not_new_entries()
    {
        var f=Fixture();try
        {
            f.State.Orders.Add(new WorkingOrder("one",f.State.Instrument,"Buy","Limit",1,0,30000,0,"Working","","manual"));
            // Click the center row containing the native order.
            var method=typeof(LadderControl).GetMethod("RowAt",BindingFlags.Instance|BindingFlags.NonPublic)!;
            double y=Enumerable.Range(30,540).First(v=>(long)method.Invoke(f.Ladder,new object[]{(double)v})! ==120000)+3;
            var p=new Point(Columns(f.Ladder)["Buy"],y);int moved=0,cancelled=0;
            f.Ladder.MoveRequested+=(_,_)=>moved++;f.Ladder.CancelRequested+=_=>cancelled++;
            f.Window.MouseDown(p,MouseButton.Left);f.Window.MouseMove(p+new Vector(0,30),RawInputModifiers.LeftMouseButton);f.Window.MouseUp(p+new Vector(0,30),MouseButton.Left);
            Assert.Equal(1,moved);Assert.Empty(f.Orders);
            f.Window.MouseDown(p,MouseButton.Right);f.Window.MouseUp(p,MouseButton.Right);Assert.Equal(1,cancelled);Assert.Empty(f.Orders);
        } finally {f.Window.Close();}
    }
    [AvaloniaFact] public void Ticket_help_toggle_clears_current_help_and_persists_without_trading()
    {
        var f=Fixture();try
        {
            int saves=0;var toggle=f.Ladder.CreateColumnHelpToggle(()=>saves++);
            var p=new Point(Columns(f.Ladder)["Buy"],200);f.Window.MouseMove(p);Assert.Contains("Shift",ToolTip.GetTip(f.Ladder)?.ToString());
            toggle.IsChecked=false;Assert.False(f.Settings.ColumnHelp);Assert.Null(ToolTip.GetTip(f.Ladder));
            f.Window.MouseMove(p+new Vector(0,5));Assert.Null(ToolTip.GetTip(f.Ladder));Assert.Equal(1,saves);Assert.Empty(f.Orders);
            var saved=System.Text.Json.JsonSerializer.Deserialize<Settings>(System.Text.Json.JsonSerializer.Serialize(f.Settings))!;Assert.False(saved.ColumnHelp);
            toggle.IsChecked=true;f.Window.MouseMove(p);Assert.Contains("BUY",ToolTip.GetTip(f.Ladder)?.ToString());
        } finally {f.Window.Close();}
    }
    [AvaloniaFact] public void Order_columns_have_distinct_subtle_tints_and_render_actual_preview()
    {
        var f=Fixture();try
        {
            using var frame=f.Window.CaptureRenderedFrame();Assert.NotNull(frame);
            using var stream=new MemoryStream();frame!.Save(stream);using var pixels=SkiaSharp.SKBitmap.Decode(stream.ToArray());
            var cols=Columns(f.Ladder);var buy=pixels.GetPixel((int)cols["Buy"],100);var sell=pixels.GetPixel((int)cols["Sell"],100);var data=pixels.GetPixel((int)cols["Sold"],100);
            Assert.NotEqual(buy,sell);Assert.NotEqual(data,buy);Assert.NotEqual(data,sell);
            frame.Save(Path.Combine(Path.GetTempPath(),"bdiq-explicit-order-columns.png"));
        } finally {f.Window.Close();}
    }
}
