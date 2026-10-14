using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

public sealed partial class MainWindow
{
    private readonly TextBlock outlookSummary = Ui.Label("Collecting live flow", Pal.Text, size: 11, wrap: true);
    private readonly TextBlock outlookEvidence = Ui.Label("", Pal.Muted, size: 11, wrap: true);
    private Border outlookPanel = null!;
    private StackPanel outlookDetails = null!;

    private Control BuildOutlook()
    {
        outlookDetails = new StackPanel { Spacing = 6 };
        var windows = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        foreach (var seconds in new[] { 5, 15, 60 })
            windows.Children.Add(Ui.Btn($"{seconds}s", () => {
                cfg.FlowSeconds = seconds;
                s.Liquidity.SetFlowWindow(seconds);
                cfg.Save(); RefreshOutlook();
            }));
        outlookDetails.Children.Add(windows);
        outlookDetails.Children.Add(outlookEvidence);
        var body = new StackPanel { Spacing = 6, Margin = new Thickness(10) };
        body.Children.Add(Ui.Btn("OUTLOOK · expand / collapse", () => {
            cfg.OutlookCollapsed = !cfg.OutlookCollapsed;
            cfg.Save(); RefreshOutlook();
        }));
        body.Children.Add(outlookSummary);
        body.Children.Add(outlookDetails);
        outlookPanel = new Border { Child = body, BorderBrush = Pal.B(Pal.Edge), BorderThickness = new Thickness(0,0,0,1) };
        RefreshOutlook();
        return outlookPanel;
    }

    private void RefreshOutlook()
    {
        if (outlookPanel == null) return;
        outlookPanel.IsVisible = cfg.ShowOutlook;
        outlookDetails.IsVisible = !cfg.OutlookCollapsed;
        s.Liquidity.SetFlowWindow(cfg.FlowSeconds);
        if (!s.Connected || s.FeedAgeMs > 2000 || s.Book.BestBid is not long bid || s.Book.BestAsk is not long ask)
        {
            s.Outlook.Reset();
            outlookSummary.Text = "Waiting for fresh depth and prints";
            outlookEvidence.Text = "Probability: collecting observations";
            return;
        }
        var bf = s.Liquidity.FlowAt(bid);
        var af = s.Liquidity.FlowAt(ask);
        var snap = s.TrendSnap;
        var regime = Structure.Market(s.Bars.RegimeAt(DomState.NowMs()), lastState, snap.T1.Dir,
            s.LastTick, s.Vwap.Value, s.Vwap.Sigma, snap.T1.LineTicks, s.Trend.T1.AtrTicks);
        var heldBid = bf.Absorb == "bid";
        var heldAsk = af.Absorb == "ask";
        var recent = s.Tape.FirstOrDefault();
        if (recent != null && Math.Abs(DomState.NowMs() - recent.TimeMs) < 2000 && heldBid != heldAsk)
            s.Outlook.Start(heldBid ? bid : ask, heldBid, recent.TimeMs);
        outlookSummary.Text = heldBid && !heldAsk ? $"Watching support hold · {Px(bid * s.TickSize)}"
            : heldAsk && !heldBid ? $"Watching resistance hold · {Px(ask * s.TickSize)}"
            : "Watching · no confirmed hold";
        outlookSummary.Foreground = Pal.B(heldBid ? Pal.Bid : heldAsk ? Pal.Ask : Pal.Text);
        outlookEvidence.Text = $"FLOW {cfg.FlowSeconds}s · HEAT {cfg.HeatMinutes}m\n" +
            $"Bid: +{bf.Stacked} replenished / −{bf.Pulled} pulled\n" +
            $"Ask: +{af.Stacked} replenished / −{af.Pulled} pulled\n" +
            $"ST1 {snap.T1.Dir} / ST2 {snap.T2.Dir} · {regime.State}" +
            (regime.Extended != null ? " · extended" : "") + "\n" +
            (heldBid ? $"Invalidation: trade below {Px(bid * s.TickSize)}\n" : heldAsk ? $"Invalidation: trade above {Px(ask * s.TickSize)}\n" : "") +
            $"Probability: {s.OutlookRecordStatus} · no calibrated estimate";
    }
}
