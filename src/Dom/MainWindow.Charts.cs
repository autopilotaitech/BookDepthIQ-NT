using Avalonia.Controls;
using BookDepthIQ.Dom.Charts;

namespace BookDepthIQ.Dom;

public sealed partial class MainWindow
{
    ChartWorkspace? chartWorkspace;
    ChartClient? chartClient;
    ChartCoordinator? chartModel;
    Control WrapCharts(Control existingDom)
    {
        cfg.Charts??=new();
        chartModel=new ChartCoordinator();
        chartWorkspace=new ChartWorkspace(existingDom,cfg.Charts,chartModel,cfg.Save) {Instrument=cfg.Instrument,OpeningRangeConfig=cfg.PaxOr,MinimumDomWidth=765+Math.Clamp(cfg.SideWidth,240,900)};
        chartWorkspace.RequestsChanged+=RefreshCharts;
        chartWorkspace.TradingLinesProvider=()=>ChartOrderOverlay.Build(s,cfg.Account,cfg.Instrument);
        chartWorkspace.ReloadRequested+=()=>chartClient?.Reload();
        Closed+=(_,_)=> {chartClient?.Dispose();chartWorkspace.Dispose();};
        return chartWorkspace;
    }
    void RefreshCharts()
    {
        if(chartWorkspace==null||chartModel==null) return;
        chartWorkspace.Instrument=cfg.Instrument;
        chartWorkspace.MinimumDomWidth=765+Math.Clamp(cfg.SideWidth,240,900);
        chartWorkspace.OpeningRangeConfig=cfg.PaxOr;
        var slots=chartWorkspace.ActiveSlots().ToArray();
        if(slots.Length==0) {chartClient?.Dispose();chartClient=null;return;}
        chartClient??=new ChartClient(chartModel);
        var requests=slots.Select(slot=> {
            var pane=slot==0?cfg.Charts.Top:cfg.Charts.Bottom;
            return new ChartClient.Request(slot,cfg.Instrument,pane.Frame,pane.Lookback(pane.Frame));
        }).ToList();
        if(slots.Any(slot=>(slot==0?cfg.Charts.Top:cfg.Charts.Bottom).PaxActive)) requests.Add(new ChartClient.Request(2,cfg.Instrument,-30,75000));
        chartClient.SetRequests(requests);
    }
}
