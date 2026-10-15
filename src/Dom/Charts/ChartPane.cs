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
    public ChartPane(ChartPaneSettings settings,Action changed,Action hide,Action maximize)
    {
        Chart=new(settings);
        var frames=new ComboBox {ItemsSource=ChartFrame.Choices.Select(ChartFrame.Label).ToArray(),SelectedIndex=Array.IndexOf(ChartFrame.Choices,settings.Frame),Width=66,MinHeight=26,FontFamily=Flight.Sans,FontSize=11};
        frames.SelectionChanged+=(_,_)=> {if(frames.SelectedIndex<0) return;settings.Frame=ChartFrame.Choices[frames.SelectedIndex];Chart.Clear();changed();};
        var menu=new ContextMenu();
        var options=new MenuItem {Header="Chart settings"};options.Click+=(_,_)=> {if(TopLevel.GetTopLevel(this) is Window owner) new ChartSettingsWindow(settings,changed,lastSnapshot).ShowDialog(owner);};
        var max=new MenuItem {Header="Maximize / restore"};max.Click+=(_,_)=>maximize();
        var hidden=new MenuItem {Header="Hide chart"};hidden.Click+=(_,_)=>hide();
        menu.ItemsSource=new[]{options,max,hidden};
        var more=Flight.HeadBtn("⋯",()=>menu.Open(this),"chart settings, maximize, hide");
        var header=new Grid {ColumnDefinitions=new("*,Auto,Auto,Auto,Auto"),Margin=new Thickness(8,4)};
        header.Children.Add(symbol);Grid.SetColumn(frames,1);header.Children.Add(frames);
        var live=Flight.HeadBtn("LIVE",()=> {Chart.Viewport.Live(CurrentCount);Chart.InvalidateVisual();},"follow the latest candle (End)");Grid.SetColumn(live,2);header.Children.Add(live);
        var auto=Flight.HeadBtn("AUTO",()=> {Chart.Viewport.AutoScale();Chart.InvalidateVisual();},"fit visible candles; double-click price axis to reset");Grid.SetColumn(auto,3);header.Children.Add(auto);
        Grid.SetColumn(more,4);header.Children.Add(more);
        var top=new Border {Background=Pal.B(Pal.Panel),BorderBrush=Pal.B(Pal.Line),BorderThickness=new Thickness(0,0,0,1),Child=header};
        DockPanel.SetDock(top,Dock.Top);Children.Add(top);DockPanel.SetDock(status,Dock.Bottom);Children.Add(status);Children.Add(Chart);
    }
    int CurrentCount;
    ChartSnapshot? lastSnapshot;
    public void Update(string instrument,ChartSnapshot snapshot)
    {
        symbol.Text=instrument;CurrentCount=snapshot.Bars.Count;lastSnapshot=snapshot;
        status.Text=$"{snapshot.Status} · {CurrentCount:N0} bars";Chart.Update(snapshot);
    }
}
