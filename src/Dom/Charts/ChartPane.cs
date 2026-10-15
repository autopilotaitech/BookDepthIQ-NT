using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BookDepthIQ.Core.Charts;

namespace BookDepthIQ.Dom.Charts;

public sealed class ChartPane : DockPanel
{
    readonly TextBlock symbol=new() {FontFamily=Flight.Sans,FontSize=11,Foreground=Pal.B(Pal.Text),VerticalAlignment=VerticalAlignment.Center,TextTrimming=Avalonia.Media.TextTrimming.CharacterEllipsis};
    readonly TextBlock status=new() {FontFamily=Flight.Sans,FontSize=10,Foreground=Pal.B(Pal.Muted),Margin=new Thickness(8,3)};
    public CandleChartControl Chart {get;}
    readonly Button live,auto,center;
    public ChartPane(ChartPaneSettings settings,Action changed,Action hide,Action maximize,ChartWorkspaceSettings? workspaceSettings=null,Action? reload=null)
    {
        Chart=new(settings);
        var frames=new ComboBox {ItemsSource=ChartFrame.Choices.Select(ChartFrame.Label).ToArray(),SelectedIndex=Array.IndexOf(ChartFrame.Choices,settings.Frame),Width=60,Height=28,MinHeight=28,FontFamily=Flight.Sans,FontSize=11};
        frames.SelectionChanged+=(_,_)=> {if(frames.SelectedIndex<0) return;settings.Frame=ChartFrame.Choices[frames.SelectedIndex];Chart.Clear();changed();};
        var menu=new ContextMenu();
        var options=new MenuItem {Header="Chart settings"};options.Click+=(_,_)=> {if(TopLevel.GetTopLevel(this) is Window owner) new ChartSettingsWindow(settings,changed,lastSnapshot,workspaceSettings).ShowDialog(owner);};
        var reset=new MenuItem {Header="Reset view"};reset.Click+=(_,_)=>Chart.ResetView();
        var reloadItem=new MenuItem {Header="Reload history"};reloadItem.Click+=(_,_)=> (reload??changed)();
        var max=new MenuItem {Header="Maximize / restore"};max.Click+=(_,_)=>maximize();
        var hidden=new MenuItem {Header="Hide chart"};hidden.Click+=(_,_)=>hide();
        menu.ItemsSource=new[]{options,reset,reloadItem,max,hidden};ContextMenu=menu;
        var more=Flight.HeadBtn("⋯",()=>menu.Open(this),"chart settings, maximize, hide");
        var header=new Grid {ColumnDefinitions=new("*,Auto,Auto,Auto,Auto,Auto"),Margin=new Thickness(8,4)};
        header.Children.Add(symbol);Grid.SetColumn(frames,1);header.Children.Add(frames);
        live=Flight.HeadBtn("LIVE",Chart.GoLive,"follow the latest candle without changing price scale (End)");Grid.SetColumn(live,2);header.Children.Add(live);
        auto=Flight.HeadBtn("AUTO",Chart.AutoScale,"fit visible candles; double-click price axis to reset");Grid.SetColumn(auto,3);header.Children.Add(auto);
        center=Flight.HeadBtn("⌖",Chart.Recenter,"center latest price and follow live, preserving candle spacing (Space)");center.MinWidth=28;center.Padding=new Thickness(7,3);Grid.SetColumn(center,4);header.Children.Add(center);
        Grid.SetColumn(more,5);header.Children.Add(more);
        foreach(var button in new[]{live,auto,center,more})
        {button.Height=28;button.MinHeight=28;button.FontSize=11;button.Padding=new Thickness(8,0);button.Margin=new Thickness(3,0,0,0);button.CornerRadius=new CornerRadius(4);}
        center.Width=28;center.Padding=new Thickness(4,0);more.Width=28;more.Padding=new Thickness(4,0);
        var top=new Border {Background=Pal.B(Pal.Panel),BorderBrush=Pal.B(Pal.Line),BorderThickness=new Thickness(0,0,0,1),Child=header};
        DockPanel.SetDock(top,Dock.Top);Children.Add(top);DockPanel.SetDock(status,Dock.Bottom);Children.Add(status);Children.Add(Chart);
        Chart.ViewChanged+=PaintNavigation;PaintNavigation();
    }
    void PaintNavigation()
    {
        void Paint(Button button,bool active) {button.BorderBrush=Pal.B(active?Pal.Accent:Pal.Edge);button.Background=Pal.B(active?Flight.SegOn:Flight.Field);button.Foreground=Pal.B(active?Pal.Accent:Pal.Text);}
        Paint(live,Chart.Viewport.Follow);Paint(auto,Chart.Viewport.Auto);Paint(center,Chart.Viewport.CenterOnLast);
    }
    int CurrentCount;
    ChartSnapshot? lastSnapshot;
    public void Update(string instrument,ChartSnapshot snapshot)
    {
        symbol.Text=instrument;ToolTip.SetTip(symbol,instrument);CurrentCount=snapshot.Bars.Count;lastSnapshot=snapshot;
        status.Text=$"{snapshot.Status} · {CurrentCount:N0} bars";Chart.Update(snapshot);
    }
}
