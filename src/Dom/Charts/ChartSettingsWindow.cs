using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace BookDepthIQ.Dom.Charts;

public sealed class ChartSettingsWindow : Window
{
    public ChartSettingsWindow(ChartPaneSettings settings,Action changed,ChartSnapshot? snapshot=null,ChartWorkspaceSettings? workspaceSettings=null,string instrument="",string tradeCoverage="Capture begins when enabled on a visible chart.")
    {
        Title="Chart settings";Width=390;Height=590;CanResize=false;Background=Pal.B(Pal.Bg);FontFamily=Flight.Sans;Foreground=Pal.B(Pal.Text);
        var panel=new StackPanel {Margin=new Thickness(18),Spacing=10};
        panel.Children.Add(new TextBlock {Text=$"{BookDepthIQ.Core.Charts.ChartFrame.Label(settings.Frame)} · HISTORY",Foreground=Pal.B(Pal.Accent)});
        var bars=new NumericUpDown {Minimum=250,Maximum=10000,Increment=250,Value=settings.Lookback(settings.Frame),FormatString="0"};panel.Children.Add(bars);
        panel.Children.Add(new TextBlock {Text="250–10,000 native bars · bounded memory cache",FontSize=11,Foreground=Pal.B(Pal.Muted)});
        if(snapshot!=null) panel.Children.Add(new TextBlock {Text=snapshot.Bars.Count==0?"No history loaded":$"Loaded {snapshot.Bars.Count:N0} · {snapshot.Bars[0].Time:yyyy-MM-dd} → {snapshot.Bars[^1].Time:yyyy-MM-dd}",FontSize=11,Foreground=Pal.B(Pal.Muted)});
        var volume=new CheckBox {Content="Volume",IsChecked=settings.Volume};panel.Children.Add(volume);
        var orders=new CheckBox {Content="Orders & position (display only)",IsChecked=settings.ShowOrders};panel.Children.Add(orders);
        var lastPrice=new CheckBox {Content="Last-price line & tag",IsChecked=settings.ShowLastPrice};panel.Children.Add(lastPrice);
        var crosshair=new CheckBox {Content="Crosshair",IsChecked=settings.ShowCrosshair};panel.Children.Add(crosshair);
        var snap=new CheckBox {Content="Snap crosshair to candle centers",IsChecked=settings.SnapCrosshair};panel.Children.Add(snap);
        var link=new CheckBox {Content="Link crosshair between charts",IsChecked=workspaceSettings?.LinkCrosshair??false,IsVisible=workspaceSettings!=null};panel.Children.Add(link);
        var sma=new CheckBox {Content="SMA",IsChecked=settings.Sma};panel.Children.Add(sma);
        var ema=new CheckBox {Content="EMA",IsChecked=settings.Ema};panel.Children.Add(ema);
        panel.Children.Add(new TextBlock {Text="Study period",FontSize=11,Foreground=Pal.B(Pal.Muted)});
        var period=new NumericUpDown {Minimum=1,Maximum=500,Value=settings.Period,FormatString="0"};panel.Children.Add(period);
        var pax=new CheckBox {Content="PAX-style opening range",IsChecked=settings.Pax,IsEnabled=BookDepthIQ.Core.Charts.ChartFrame.SupportsPax(settings.Frame)};
        ToolTip.SetTip(pax,"Intraday only (1–30m). Uses shared DOM opening-range settings; disabled on 1h, 4h and Day.");panel.Children.Add(pax);
        panel.Children.Add(new TextBlock {Text="PAX historical sessions",FontSize=11,Foreground=Pal.B(Pal.Muted)});
        var days=new NumericUpDown {Minimum=1,Maximum=8,Value=settings.PaxDays,FormatString="0"};panel.Children.Add(days);
        var trades=workspaceSettings!=null?new LargeTradeSettingsPanel(workspaceSettings,settings,instrument,tradeCoverage):null;
        if(trades!=null) panel.Children.Add(trades);
        var actions=new StackPanel {Orientation=Orientation.Horizontal,Spacing=8};
        actions.Children.Add(Flight.HeadBtn("APPLY",()=> {
            if(trades!=null&&!trades.TryApply()) return;
            settings.SetLookback(settings.Frame,(int)(bars.Value??250));settings.Volume=volume.IsChecked==true;settings.ShowOrders=orders.IsChecked==true;
            settings.ShowLastPrice=lastPrice.IsChecked==true;settings.ShowCrosshair=crosshair.IsChecked==true;settings.SnapCrosshair=snap.IsChecked==true;
            if(workspaceSettings!=null) workspaceSettings.LinkCrosshair=link.IsChecked==true;
            settings.Sma=sma.IsChecked==true;settings.Ema=ema.IsChecked==true;settings.Period=(int)(period.Value??20);settings.Pax=pax.IsChecked==true;settings.PaxDays=(int)(days.Value??8);changed();Close();}));
        actions.Children.Add(Flight.HeadBtn("CANCEL",Close));panel.Children.Add(actions);Content=new ScrollViewer {Content=panel};
    }
}
