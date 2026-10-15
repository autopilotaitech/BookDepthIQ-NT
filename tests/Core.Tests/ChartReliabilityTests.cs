using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using BookDepthIQ.Core.Charts;
using BookDepthIQ.Dom.Charts;
using Xunit;

public class ChartReliabilityTests
{
    [AvaloniaFact] public void Maximize_restore_hide_and_reenable_preserve_dom_and_saved_split_ratios()
    {
        var preferences=new ChartWorkspaceSettings {Enabled=true,WidthRatio=.35,HeightRatio=.6};
        var dom=new Border();int saves=0;
        using var workspace=new ChartWorkspace(dom,preferences,new(),()=>saves++);
        void Layout() {workspace.Measure(new Size(1920,1000));workspace.Arrange(new Rect(0,0,1920,1000));}
        Layout();var panes=workspace.GetVisualDescendants().OfType<ChartPane>().ToArray();
        void Click(ChartPane pane,string label)=>pane.ContextMenu!.Items.OfType<MenuItem>().Single(m=>(string?)m.Header==label).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Click(panes[0],"Maximize / restore");Layout();Assert.Equal(new[]{0},workspace.ActiveSlots());
        Click(panes[0],"Maximize / restore");Layout();Assert.Equal(new[]{0,1},workspace.ActiveSlots());
        Click(panes[0],"Hide chart");Layout();Assert.Equal(new[]{1},workspace.ActiveSlots());
        Click(panes[1],"Hide chart");Layout();Assert.Empty(workspace.ActiveSlots());Assert.False(preferences.Enabled);
        Assert.InRange(dom.Bounds.Width,1919,1920);
        workspace.SetEnabled(true);Layout();Assert.Equal(new[]{0,1},workspace.ActiveSlots());
        Assert.Equal(.35,preferences.WidthRatio);Assert.Equal(.6,preferences.HeightRatio);Assert.Same(dom,workspace.Dom);Assert.True(saves>=3);
    }

    [Fact] public void Saved_chart_preferences_round_trip_without_losing_independent_pane_controls()
    {
        var settings=new ChartWorkspaceSettings {Enabled=true,WidthRatio=.37,HeightRatio=.61,LinkCrosshair=true};
        settings.Top.Frame=5;settings.Top.ShowLastPrice=false;settings.Top.ShowCrosshair=false;
        settings.Bottom.Frame=1440;settings.Bottom.SnapCrosshair=false;settings.Bottom.ShowOrders=false;
        settings.Top.SetLookback(5,777);settings.Bottom.SetLookback(1440,333);
        var restored=System.Text.Json.JsonSerializer.Deserialize<ChartWorkspaceSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;
        restored.Normalize();
        Assert.True(restored.Enabled);Assert.True(restored.LinkCrosshair);Assert.Equal(.37,restored.WidthRatio);Assert.Equal(.61,restored.HeightRatio);
        Assert.Equal(5,restored.Top.Frame);Assert.False(restored.Top.ShowLastPrice);Assert.False(restored.Top.ShowCrosshair);
        Assert.Equal(1440,restored.Bottom.Frame);Assert.False(restored.Bottom.SnapCrosshair);Assert.False(restored.Bottom.ShowOrders);
        Assert.Equal(777,restored.Top.Lookback(5));Assert.Equal(333,restored.Bottom.Lookback(1440));
    }
    [Fact] public void Zooming_at_right_edge_cannot_leave_all_candles_behind_the_viewport()
    {
        var view=new ChartViewport();view.Reset(200);view.Pan(10000,200);
        view.Zoom(.5,1,200);
        Assert.InRange(view.Start,0,199);
    }

    static ChartSnapshot Bars(long price,long version=1)=>new(new[]{new ChartBar(0,DateTimeOffset.UnixEpoch,new(2026,10,15),price,price+4,price-4,price,10,1,false)},.25,"LIVE",price,version);

    [AvaloniaFact] public void Switching_contract_restores_visible_price_instead_of_retaining_previous_manual_scale()
    {
        var pane=new ChartPane(new(),()=>{},()=>{},()=>{});
        pane.Chart.Measure(new Size(600,400));pane.Chart.Arrange(new Rect(0,0,600,400));
        pane.Update("NQ TEST",Bars(120000));pane.Chart.Viewport.SetPriceRange(119900,120100);
        pane.Chart.SetLinkedCursor(new(DateTimeOffset.UnixEpoch,120000,new(2026,10,15)));
        Assert.NotNull(pane.Chart.CursorReadout);
        pane.Update("ES TEST",Bars(24000));
        Assert.True(pane.Chart.Viewport.Auto);
        Assert.InRange(24000d,pane.Chart.Geometry.Low,pane.Chart.Geometry.High);
        Assert.Null(pane.Chart.CursorReadout);
    }

    [AvaloniaFact] public void Same_contract_updates_preserve_manual_scale()
    {
        var pane=new ChartPane(new(),()=>{},()=>{},()=>{});
        pane.Update("NQ TEST",Bars(120000));pane.Chart.Viewport.SetPriceRange(119900,120100);
        pane.Update("NQ TEST",Bars(120010,2));
        Assert.False(pane.Chart.Viewport.Auto);
        Assert.Equal(119900,pane.Chart.Geometry.Low);Assert.Equal(120100,pane.Chart.Geometry.High);
    }
}
