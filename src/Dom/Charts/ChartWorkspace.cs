using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using BookDepthIQ.Core;
using BookDepthIQ.Core.Charts;

namespace BookDepthIQ.Dom.Charts;

public sealed class ChartWorkspace : Grid,IDisposable
{
    public Control Dom {get;}
    readonly ChartWorkspaceSettings settings;
    readonly Action save;
    readonly Grid charts=new();
    readonly GridSplitter horizontal,vertical;
    readonly ChartPane[] panes;
    readonly ChartCoordinator model;
    readonly SessionZoneWorker sessionWorker;
    string instrument="",sessionConfig="";long nextSessionPoll;bool disposed;
    IDisposable? timer;
    int maximized=-1;
    public event Action? RequestsChanged;
    public event Action? ReloadRequested;
    public bool ChartsVisible=>charts.IsVisible;
    public string Instrument {get=>instrument;set {if(instrument==value) return;instrument=value;ResetSessions();}}
    public double MinimumDomWidth {get;set;}=1060;
    public PaxOrCfg OpeningRangeConfig {get;set;}=new();
    public Func<IReadOnlyList<ChartTradeLine>>? TradingLinesProvider {get;set;}
    public Func<ChartTradeSnapshot>? TradePrintsProvider {get;set;}
    public void RefreshTradingLines()
    {
        var lines=TradingLinesProvider?.Invoke()??Array.Empty<ChartTradeLine>();
        foreach(var pane in panes) pane.Chart.TradeLines=lines;
    }
    record OverlayKey(long Version,PaxOrCfg Config,string Instrument,int TopFrame,int TopDays,bool TopOn,int BottomFrame,int BottomDays,bool BottomOn);
    OverlayKey? overlayKey;
    Task? overlayTask;
    public ChartWorkspace(Control dom,ChartWorkspaceSettings settings,ChartCoordinator model,Action save)
    {
        Dom=dom;this.settings=settings;this.model=model;this.save=save;settings.Normalize();
        ColumnDefinitions=new("0,0,*");charts.RowDefinitions=new("*,5,*");
        panes=new[]{new ChartPane(settings.Top,Changed,()=>Hide(0),()=>Maximize(0),settings,()=>ReloadRequested?.Invoke()),new ChartPane(settings.Bottom,Changed,()=>Hide(1),()=>Maximize(1),settings,()=>ReloadRequested?.Invoke())};
        sessionWorker=new(null,(_,result)=> {foreach(var pane in panes) {pane.Chart.SessionZones=result;pane.Chart.InvalidateVisual();}});
        panes[0].Chart.CursorChanged+=value=> {if(settings.LinkCrosshair) panes[1].Chart.SetLinkedCursor(value);};
        panes[1].Chart.CursorChanged+=value=> {if(settings.LinkCrosshair) panes[0].Chart.SetLinkedCursor(value);};
        charts.Children.Add(panes[0]);Grid.SetRow(panes[1],2);charts.Children.Add(panes[1]);
        horizontal=new GridSplitter {ResizeDirection=GridResizeDirection.Rows,Background=Pal.B(Pal.Line)};Grid.SetRow(horizontal,1);charts.Children.Add(horizontal);
        vertical=new GridSplitter {ResizeDirection=GridResizeDirection.Columns,Background=Pal.B(Pal.Line)};Grid.SetColumn(vertical,1);
        Children.Add(charts);Children.Add(vertical);Grid.SetColumn(dom,2);Children.Add(dom);
        vertical.DragCompleted+=(_,_)=> {var total=ColumnDefinitions[0].ActualWidth+ColumnDefinitions[2].ActualWidth;if(total>0) settings.WidthRatio=ColumnDefinitions[0].ActualWidth/total;save();};
        horizontal.DragCompleted+=(_,_)=> {var total=charts.RowDefinitions[0].ActualHeight+charts.RowDefinitions[2].ActualHeight;if(total>0) settings.HeightRatio=charts.RowDefinitions[0].ActualHeight/total;save();};
        SizeChanged+=(_,_)=>LayoutCharts();LayoutCharts();
        AttachedToVisualTree+=(_,_)=>StartTimer();
        DetachedFromVisualTree+=(_,_)=> {timer?.Dispose();timer=null;ResetSessions();};
    }
    public IEnumerable<int> ActiveSlots()=>ChartsVisible?Enumerable.Range(0,2).Where(i=>panes[i].IsVisible):Enumerable.Empty<int>();
    public void SetEnabled(bool enabled)
    {
        settings.Enabled=enabled;if(enabled&&!settings.Top.Visible&&!settings.Bottom.Visible) {settings.Top.Visible=true;settings.Bottom.Visible=true;}
        maximized=-1;LayoutCharts();save();
    }
    void Changed() {ResetSessions();save();RequestsChanged?.Invoke();overlayKey=null;InvalidateVisual();}
    void ResetSessions()
    {
        sessionWorker?.Invalidate();sessionConfig="";nextSessionPoll=0;
        if(panes!=null) foreach(var pane in panes) {pane.Chart.SessionZones=SessionZoneSnapshot.Empty;pane.Chart.InvalidateVisual();}
    }
    void RefreshSessions()
    {
        if(disposed||Environment.TickCount64<nextSessionPoll) return;nextSessionPoll=Environment.TickCount64+500;
        if(!ActiveSlots().Any(i=>ChartRequests.SessionsActive(i==0?settings.Top:settings.Bottom,settings))) {ResetSessions();return;}
        if(sessionConfig.Length==0) sessionConfig=System.Text.Json.JsonSerializer.Serialize(settings.Sessions);
        var source=model.Snapshot(2);var now=DateTimeOffset.UtcNow;
        var fresh=source.Status=="LIVE"&&source.ReceivedAtMs>0&&now.ToUnixTimeMilliseconds()-source.ReceivedAtMs<=90000;
        var key=$"{Instrument}|{source.Version}|{sessionConfig}|{now.ToUnixTimeSeconds()/60}|{fresh}";
        foreach(var pane in panes) pane.Chart.SessionStyle=settings.Sessions.Copy();
        sessionWorker.Request(new(key,Instrument,source,settings.Sessions.Copy(),now,fresh));
    }
    void StartTimer()
    {
        if(!ChartsVisible||timer!=null) return;
        timer=DispatcherTimer.Run(()=> {
            RefreshTradingLines();
            RefreshSessions();
            var prints=TradePrintsProvider?.Invoke()??ChartTradeSnapshot.Empty;
            for(int i=0;i<2;i++) if(panes[i].IsVisible)
            {
                var pref=i==0?settings.Top:settings.Bottom;
                panes[i].Chart.CapturedTrades=prints;panes[i].Chart.TradeProfile=settings.TradeProfile(Instrument) with{};
                if(!settings.LinkCrosshair) panes[i].Chart.SetLinkedCursor(null);
                if(!pref.PaxActive) panes[i].Chart.OpeningRanges=Array.Empty<ChartOrLine>();
                panes[i].Update(Instrument,model.Snapshot(i));
            }
            var key=new OverlayKey(model.Version(2),OpeningRangeConfig,Instrument,settings.Top.Frame,settings.Top.PaxDays,panes[0].IsVisible&&settings.Top.PaxActive,settings.Bottom.Frame,settings.Bottom.PaxDays,panes[1].IsVisible&&settings.Bottom.PaxActive);
            if(key!=overlayKey && (key.TopOn||key.BottomOn) && (overlayTask==null||overlayTask.IsCompleted))
            {
                overlayKey=key;
                overlayTask=Task.Run(()=> {
                    var opening=model.Snapshot(2);
                    var top=key.TopOn?ChartOpeningRanges.Build(opening.Bars,key.Config,key.Instrument,opening.TickSize,key.TopFrame,key.TopDays):Array.Empty<ChartOrLine>();
                    var bottom=key.BottomOn?ChartOpeningRanges.Build(opening.Bars,key.Config,key.Instrument,opening.TickSize,key.BottomFrame,key.BottomDays):Array.Empty<ChartOrLine>();
                    Dispatcher.UIThread.Post(()=> {if(overlayKey!=key) return;panes[0].Chart.OpeningRanges=top;panes[1].Chart.OpeningRanges=bottom;});
                });
            }
            return true;
        },TimeSpan.FromMilliseconds(33));
    }
    void Hide(int i) {if(i==0) settings.Top.Visible=false;else settings.Bottom.Visible=false;if(!settings.Top.Visible&&!settings.Bottom.Visible) settings.Enabled=false;maximized=-1;LayoutCharts();save();}
    void Maximize(int i) {maximized=maximized==i?-1:i;LayoutCharts();}
    void LayoutCharts()
    {
        ResetSessions();
        bool shown=settings.Enabled && Bounds.Width>=Math.Max(1390,MinimumDomWidth+325) && (settings.Top.Visible||settings.Bottom.Visible);
        charts.IsVisible=shown;vertical.IsVisible=shown;
        ColumnDefinitions[0].MinWidth=shown?320:0;ColumnDefinitions[2].MinWidth=shown?MinimumDomWidth:0;
        ColumnDefinitions[0].Width=shown?new GridLength(settings.WidthRatio,GridUnitType.Star):new GridLength(0);
        ColumnDefinitions[1].Width=new GridLength(shown?5:0);ColumnDefinitions[2].Width=new GridLength(shown?1-settings.WidthRatio:1,GridUnitType.Star);
        panes[0].IsVisible=shown&&settings.Top.Visible&&(maximized<0||maximized==0);panes[1].IsVisible=shown&&settings.Bottom.Visible&&(maximized<0||maximized==1);
        bool both=panes[0].IsVisible&&panes[1].IsVisible;horizontal.IsVisible=both;
        charts.RowDefinitions[0].Height=panes[0].IsVisible?new GridLength(both?settings.HeightRatio:1,GridUnitType.Star):new GridLength(0);
        charts.RowDefinitions[1].Height=new GridLength(both?5:0);
        charts.RowDefinitions[2].Height=panes[1].IsVisible?new GridLength(both?1-settings.HeightRatio:1,GridUnitType.Star):new GridLength(0);
        if(shown) StartTimer();else {timer?.Dispose();timer=null;}
        RequestsChanged?.Invoke();
    }
    public void Dispose() {disposed=true;timer?.Dispose();timer=null;sessionWorker.Dispose();ResetSessions();}
}
