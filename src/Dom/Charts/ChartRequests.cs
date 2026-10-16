namespace BookDepthIQ.Dom.Charts;
public static class ChartRequests
{
    public static IReadOnlyList<ChartClient.Request> Build(string instrument,IEnumerable<int> active,ChartWorkspaceSettings settings)
    {
        var slots=active.Where(i=>i is 0 or 1).Distinct().ToArray();
        var requests=slots.Select(i=> {var pane=i==0?settings.Top:settings.Bottom;return new ChartClient.Request(i,instrument,pane.Frame,pane.Lookback(pane.Frame));}).ToList();
        if(slots.Any(i=> {var p=i==0?settings.Top:settings.Bottom;return p.PaxActive||SessionsActive(p,settings);})) requests.Add(new(2,instrument,-30,75000));
        return requests;
    }
    public static bool SessionsActive(ChartPaneSettings pane,ChartWorkspaceSettings settings)=>settings.Sessions.Enabled&&pane.ShowSessionZones&&pane.Frame<1440&&settings.Sessions.Definitions.Any(d=>d.Enabled);
}
