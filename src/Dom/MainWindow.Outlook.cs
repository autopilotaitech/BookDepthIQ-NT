using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

public sealed partial class MainWindow
{
    private readonly TextBlock outlookBid = Ui.Label("—", Pal.Bid, size: 13);
    private readonly TextBlock outlookAsk = Ui.Label("—", Pal.Ask, size: 13);
    private readonly TextBlock outlookTrend = Ui.Label("—", Pal.Text, size: 12, wrap: true);
    private readonly TextBlock outlookInvalidation = Ui.Label("", Pal.Warn, size: 12, wrap: true);
    private readonly TextBlock outlookCollection = Ui.Label("Collecting observations", Pal.Muted, size: 11, wrap: true);
    private Button outlookCollapse = null!;
    private Seg outlookWindows = null!;
    private Border outlookPanel = null!;
    private StackPanel outlookDetails = null!;

    private Control BuildOutlook()
    {
        foreach (var value in new[] { outlookBid, outlookAsk })
        {
            value.FontFamily = Pal.Mono;
            value.FontWeight = FontWeight.SemiBold;
        }
        foreach (var value in new[] { outlookTrend, outlookInvalidation, outlookCollection }) value.FontFamily = Flight.Sans;
        outlookDetails = new StackPanel { Spacing = 8 };
        outlookWindows = new Seg(new[] { "5s", "15s", "60s" }, "pulling and stacking window; independent of the heatmap") { Width = 150 };
        outlookWindows.SelectedIndex = Array.IndexOf(new[] { 5, 15, 60 }, cfg.FlowSeconds);
        outlookWindows.SelectionChanged += (_, _) => {
            cfg.FlowSeconds = new[] { 5, 15, 60 }[outlookWindows.SelectedIndex];
            s.Liquidity.SetFlowWindow(cfg.FlowSeconds);
            cfg.Save(); RefreshOutlook();
        };
        Control DataRow(string label, TextBlock value)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("54,*") };
            row.Children.Add(new TextBlock { Text = label, FontFamily = Flight.Sans, FontSize = 9.5,
                LetterSpacing = 1, Foreground = Pal.B(Pal.Muted), VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(value, 1);
            row.Children.Add(value);
            return row;
        }
        outlookDetails.Children.Add(DataRow("BID", outlookBid));
        outlookDetails.Children.Add(DataRow("ASK", outlookAsk));
        outlookDetails.Children.Add(new Border { Height = 1, Background = Pal.B(Pal.Edge) });
        outlookDetails.Children.Add(DataRow("STATE", outlookTrend));
        outlookDetails.Children.Add(outlookInvalidation);
        outlookDetails.Children.Add(outlookCollection);
        var body = new StackPanel { Spacing = 10, Margin = new Thickness(12, 10) };
        var header = new DockPanel();
        outlookCollapse = Flight.HeadBtn("▾ OUTLOOK", () => {
            cfg.OutlookCollapsed = !cfg.OutlookCollapsed;
            cfg.Save(); RefreshOutlook();
        }, "collapse or expand outlook details");
        outlookCollapse.Height = 28;
        outlookCollapse.Padding = new Thickness(8, 0);
        DockPanel.SetDock(outlookCollapse, Dock.Left);
        header.Children.Add(outlookCollapse);
        outlookWindows.HorizontalAlignment = HorizontalAlignment.Right;
        header.Children.Add(outlookWindows);
        body.Children.Add(header);
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
        outlookWindows.IsVisible = !cfg.OutlookCollapsed;
        outlookCollapse.Content = cfg.OutlookCollapsed ? "▸ OUTLOOK" : "▾ OUTLOOK";
        s.Liquidity.SetFlowWindow(cfg.FlowSeconds);
        if (!s.Connected || s.FeedAgeMs > 2000 || s.Book.BestBid is not long bid || s.Book.BestAsk is not long ask)
        {
            s.Outlook.Reset();
            outlookBid.Text = outlookAsk.Text = "—";
            outlookTrend.Text = "Waiting for live data";
            outlookInvalidation.IsVisible = false;
            outlookCollection.Text = "Probability pending validation";
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
        outlookBid.Text = $"+{bf.Stacked:N0} STACK  /  −{bf.Pulled:N0} PULL";
        outlookAsk.Text = $"+{af.Stacked:N0} STACK  /  −{af.Pulled:N0} PULL";
        outlookTrend.Text = $"ST1 {snap.T1.Dir} · ST2 {snap.T2.Dir}\n{regime.State}" + (regime.Extended != null ? " · extended" : "");
        outlookInvalidation.IsVisible = heldBid != heldAsk;
        outlookInvalidation.Text = heldBid ? $"Invalid below {Px(bid * s.TickSize)}" : $"Invalid above {Px(ask * s.TickSize)}";
        outlookCollection.Text = s.OutlookRecordStatus;
        ToolTip.SetTip(outlookCollection, "Observations are being recorded. No calibrated probability is available yet.");
    }
}
