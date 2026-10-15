using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

/// <summary>Calendar and analytics workspace over recorded, fee-inclusive NT round trips.</summary>
public sealed class JournalWindow : Window
{
    private readonly DomState state;
    private readonly Seg environment = new(new[] { "SIM", "LIVE", "PLAYBACK" });
    private readonly Seg view = new(new[] { "CALENDAR", "ANALYTICS" });
    private readonly Seg range = new(new[] { "TODAY", "7D", "30D", "QTD", "YTD", "ALL", "CUSTOM" });
    private readonly DatePicker fromDate = new(), toDate = new();
    private readonly StackPanel customRange = new() { Orientation=Orientation.Horizontal, Spacing=10 };
    private readonly ComboBox accounts = new(), instruments = new();
    private readonly Button resetSim;
    private readonly StackPanel body = new() { Spacing = 10 };
    private readonly TextBlock context = Label("", Pal.Muted, 11);
    private readonly TextBlock status = Label("", Pal.Muted, 11);
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(2) };
    private DateOnly month, selected;
    private bool changing;
    private int lastCount = -1;

    public JournalWindow(DomState state)
    {
        this.state = state;
        resetSim = Flight.HeadBtn("RESET SIM", ConfirmResetSim, "Select a specific SIM account, then back up and clear its recorded journal", danger: true);
        Title = "BookDepthIQ — Trading Journal"; Icon = Brand.Icon();
        Width = 1180; Height = 860; MinWidth = 760; MinHeight = 580;
        Background = Pal.B(Pal.Bg); Foreground = Pal.B(Pal.Text); FontFamily = Flight.Sans;
        selected = JournalAnalytics.TradingDate(DomState.NowMs()); month = new(selected.Year, selected.Month, 1);
        environment.SelectedIndex = Array.IndexOf(new[] { "SIM", "LIVE", "PLAYBACK" }, JournalAnalytics.Environment(state.Account));
        range.SelectedIndex = 2;
        fromDate.SelectedDate = new DateTimeOffset(selected.AddDays(-29).ToDateTime(TimeOnly.MinValue));
        toDate.SelectedDate = new DateTimeOffset(selected.ToDateTime(TimeOnly.MinValue));
        customRange.Children.Add(fromDate); customRange.Children.Add(Label("TO",Pal.Muted)); customRange.Children.Add(toDate);
        fromDate.SelectedDateChanged += (_, _) => RenderBody();
        toDate.SelectedDateChanged += (_, _) => RenderBody();
        var toolbar = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var c in new Control[] { view, environment, accounts, instruments })
        { c.Margin = new Thickness(0,0,10,8); toolbar.Children.Add(c); }
        accounts.Width = 180; instruments.Width = 180;
        foreach (var c in new[] { accounts, instruments })
        {
            c.FontFamily = Flight.Sans; c.FontSize = 12; c.Background = Pal.B(Pal.Field);
            c.BorderBrush = Pal.B(Pal.Edge); c.SelectionChanged += (_, _) => { if (!changing) RenderBody(); };
        }
        toolbar.Children.Add(Flight.HeadBtn("EXPORT CSV", async () => {
            try { if (Clipboard != null) { await Clipboard.SetTextAsync(Journal.Csv(Filtered())); status.Text = "Filtered trades copied as CSV"; } }
            catch (Exception e) { status.Text = "Export failed: " + e.Message; }
        }, "copy the filtered trade history"));
        toolbar.Children.Add(resetSim);
        var header = new StackPanel { Spacing = 8, Margin = new Thickness(18,14) };
        header.Children.Add(Label("TRADING JOURNAL", Pal.Accent, 18, true));
        header.Children.Add(toolbar); header.Children.Add(context); header.Children.Add(status);
        var root = new DockPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = body, Margin = new Thickness(10,0,10,14), HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        Content = root;
        view.SelectionChanged += (_, _) => RenderBody();
        range.SelectionChanged += (_, _) => RenderBody();
        environment.SelectionChanged += (_, _) => { FillSelectors(); RenderBody(); };
        refresh.Tick += (_, _) => { if (lastCount != state.Journal.Count) { FillSelectors(); RenderBody(); } };
        Closed += (_, _) => refresh.Stop();
        FillSelectors(); RenderBody(); refresh.Start();
    }

    private static TextBlock Label(string text, Color color, double size = 12, bool bold = false) => new()
    { Text = text, FontFamily = Flight.Sans, FontSize = size, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        Foreground = Pal.B(color), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };

    private void FillSelectors()
    {
        changing = true;
        var source = state.Journal.Where(t => JournalAnalytics.Environment(t.Account) == environment.SelectedItem).ToArray();
        void Fill(ComboBox box, string[] items)
        {
            var old = box.SelectedItem as string;
            box.ItemsSource = items; box.SelectedItem = old != null && items.Contains(old) ? old : items[0];
        }
        Fill(accounts, new[] { "All accounts" }.Concat(source.Select(t => t.Account)
            .Concat(new[] { state.Account }.Where(a => JournalAnalytics.Environment(a) == environment.SelectedItem))
            .Distinct().Order()).ToArray());
        Fill(instruments, new[] { "All instruments" }.Concat(source.Select(t => t.Inst).Distinct().Order()).ToArray());
        changing = false;
    }

    private JournalTrade[] Scope() => state.Journal.Where(t => JournalAnalytics.Environment(t.Account) == environment.SelectedItem
        && (accounts.SelectedIndex <= 0 || t.Account == accounts.SelectedItem as string)
        && (instruments.SelectedIndex <= 0 || t.Inst == instruments.SelectedItem as string)).OrderBy(t => t.Close).ToArray();

    private JournalTrade[] Filtered()
    {
        var today = JournalAnalytics.TradingDate(DomState.NowMs());
        var from = range.SelectedIndex switch { 0 => today, 1 => today.AddDays(-6), 2 => today.AddDays(-29),
            3 => new DateOnly(today.Year, (today.Month - 1) / 3 * 3 + 1, 1), 4 => new DateOnly(today.Year,1,1), _ => DateOnly.MinValue };
        var to = today;
        if(range.SelectedIndex==6)
        {
            if(fromDate.SelectedDate is not DateTimeOffset f || toDate.SelectedDate is not DateTimeOffset e) return Array.Empty<JournalTrade>();
            from=DateOnly.FromDateTime(f.DateTime); to=DateOnly.FromDateTime(e.DateTime);
        }
        return Scope().Where(t => view.SelectedIndex == 0 ? JournalAnalytics.TradingDate(t.Close) == selected
            : JournalAnalytics.TradingDate(t.Close) >= from && JournalAnalytics.TradingDate(t.Close) <= to).ToArray();
    }

    private Control Metrics(JournalPerformance a)
    {
        var tiles = new WrapPanel { Orientation = Orientation.Horizontal };
        void Tile(string name, string value, Color tone)
        {
            var (box, number) = Flight.Stat(name); number.Text = value; number.Foreground = Pal.B(tone);
            tiles.Children.Add(Flight.Card("", box));
        }
        Tile("NET P&L", LadderControl.Usd(a.Net), a.Net >= 0 ? Pal.Pos : Pal.Neg);
        Tile("TRADES", a.Count.ToString(), Pal.Text);
        Tile("WIN RATE", a.WinRate is double w ? w.ToString("P0") : "—", Pal.Text);
        Tile("PROFIT FACTOR", a.ProfitFactor?.ToString("0.00") ?? "—", Pal.Text);
        Tile("EXPECTANCY", a.Expectancy is double e ? LadderControl.Usd(e) : "—", Pal.Text);
        Tile("MAX DRAWDOWN", "$" + a.MaxDrawdown.ToString("N2"), Pal.Neg);
        Tile("FEES", "$" + a.Fees.ToString("N2"), Pal.Muted);
        return tiles;
    }

    private void RenderBody()
    {
        if (changing) return;
        resetSim.IsVisible = environment.SelectedItem == "SIM";
        resetSim.IsEnabled = resetSim.IsVisible && accounts.SelectedIndex > 0;
        lastCount = state.Journal.Count;
        body.Children.Clear();
        var trades = Filtered(); var a = JournalAnalytics.Compute(trades);
        context.Text = $"{environment.SelectedItem} · {accounts.SelectedItem} · {instruments.SelectedItem} · CME trading dates: 17:00 Chicago rollover · Net P&L after recorded fees";
        if (view.SelectedIndex == 0) body.Children.Add(Calendar());
        else
        {
            body.Children.Add(range);
            if(range.SelectedIndex==6)
            {
                body.Children.Add(customRange);
            }
        }
        body.Children.Add(Metrics(a));
        if (view.SelectedIndex == 1)
        {
            body.Children.Add(Flight.Card("CUMULATIVE NET P&L & DRAWDOWN", new JournalCurve(a.Equity) { Height = 210 }));
            var splits = new WrapPanel();
            splits.Children.Add(Flight.Card("WIN / LOSS / FLAT", new JournalSplit(a.Wins,a.Losses,a.Count-a.Wins-a.Losses,Pal.Pos,Pal.Neg,"W","L") { Width=230,Height=100 }));
            splits.Children.Add(Flight.Card("LONG / SHORT", new JournalSplit(a.Longs,a.Shorts,0,Pal.Bid,Pal.Ask,"LONG","SHORT") { Width=230,Height=100 }));
            splits.Children.Add(Flight.Card("STREAKS", Label($"{a.WinStreak} wins  ·  {a.LossStreak} losses", Pal.Text)));
            splits.Children.Add(Flight.Card("PER-TRADE RATIOS", Label($"Sharpe {a.Sharpe?.ToString("0.00") ?? "—"}  ·  Sortino {a.Sortino?.ToString("0.00") ?? "—"}\nMinimum 20 trades; not annualized", Pal.Muted)));
            body.Children.Add(splits);
            var extremes = new WrapPanel();
            extremes.Children.Add(Flight.Card("LARGEST WIN",Label(LadderControl.Usd(a.LargestWin),Pal.Pos,16,true)));
            extremes.Children.Add(Flight.Card("LARGEST LOSS",Label(LadderControl.Usd(a.LargestLoss),Pal.Neg,16,true)));
            extremes.Children.Add(Flight.Card("AVG DURATION",Label(a.AverageMinutes.ToString("0.0")+" min",Pal.Text,16,true)));
            body.Children.Add(extremes);
            var groups = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
            int col = 0;
            foreach (var (name, grouping) in new[] { ("ACCOUNT", trades.GroupBy(t => t.Account)), ("INSTRUMENT", trades.GroupBy(t => t.Inst)), ("SETUP", trades.GroupBy(t => t.Setup)) })
            {
                var rows = new StackPanel { Spacing = 6 };
                var max = Math.Max(1,grouping.Select(g=>Math.Abs(g.Sum(t=>t.Pnl))).DefaultIfEmpty(0).Max());
                foreach (var g in grouping.OrderByDescending(g => g.Sum(t => t.Pnl)))
                {
                    var net = g.Sum(t=>t.Pnl);
                    rows.Children.Add(Label($"{g.Key} · {g.Count()} trades · {LadderControl.Usd(net)}", net >= 0 ? Pal.Pos : Pal.Neg));
                    rows.Children.Add(new ProgressBar { Minimum=0,Maximum=max,Value=Math.Abs(net),Height=5,
                        Foreground=Pal.B(net>=0 ? Pal.Pos : Pal.Neg),Background=Pal.B(Pal.Field) });
                }
                var card = Flight.Card("P&L BY " + name, rows); Grid.SetColumn(card, col++); groups.Children.Add(card);
            }
            body.Children.Add(groups);
        }
        var timeline = new StackPanel { Spacing = 6 };
        if (trades.Length == 0) timeline.Children.Add(Label("No completed trades in this selection. Recorded executions will appear here automatically.", Pal.Muted));
        foreach (var t in trades.Reverse())
        {
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(t.Close), Session.Zone("America/Chicago"));
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("1.3*,*,*,*,*") };
            var values = new[] { ($"{t.Inst} · {(t.Long ? "LONG" : "SHORT")} {t.Qty}\n{local:MMM dd HH:mm:ss}", t.Long ? Pal.Bid : Pal.Ask),
                ($"ENTRY\n{t.Entry:0.########}", Pal.Text), ($"EXIT\n{t.Exit:0.########}", Pal.Text),
                ($"STOP\n{t.Stop?.ToString("0.########") ?? "—"}", Pal.Warn), ($"NET\n{LadderControl.Usd(t.Pnl)}", t.Pnl >= 0 ? Pal.Pos : Pal.Neg) };
            for (var i=0; i<values.Length; i++) { var text=Label(values[i].Item1,values[i].Item2,12,true); Grid.SetColumn(text,i); grid.Children.Add(text); }
            ToolTip.SetTip(grid, $"{t.Account} · {t.Setup} · fees ${t.FeesUsd:N2} · R {t.R?.ToString("0.00") ?? "—"} · slip {t.SlipTicks?.ToString("0.##") ?? "—"} ticks");
            timeline.Children.Add(Flight.Card(t.Account + " · " + t.Setup, grid));
            timeline.Children.Add(Flight.HeadBtn("REVIEW / NOTES", () => EditTrade(t)));
        }
        body.Children.Add(Flight.Card(view.SelectedIndex == 0 ? selected.ToString("MMMM dd, yyyy") + " · TRADE TIMELINE" : "TRADE TIMELINE",
            new ScrollViewer { Content = timeline, MaxHeight = 410, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }));
    }

    private Control Calendar()
    {
        var days = Scope().GroupBy(t => JournalAnalytics.TradingDate(t.Close)).ToDictionary(g => g.Key, g => g.ToArray());
        var panel = new StackPanel { Spacing = 8 };
        var navigation = new StackPanel { Orientation = Orientation.Horizontal };
        navigation.Children.Add(Flight.HeadBtn("‹", () => { month = month.AddMonths(-1); RenderBody(); }));
        navigation.Children.Add(Label(month.ToString("MMMM yyyy"), Pal.Text,16,true));
        navigation.Children.Add(Flight.HeadBtn("›", () => { month = month.AddMonths(1); RenderBody(); }));
        panel.Children.Add(navigation);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,Auto") };
        var labels = new[] { "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
        for (var i=0; i<7; i++) { var label=Label(labels[i],Pal.Muted,10); Grid.SetColumn(label,i); grid.Children.Add(label); }
        var offset = ((int)month.DayOfWeek + 6) % 7;
        for (var d=1; d<=DateTime.DaysInMonth(month.Year,month.Month); d++)
        {
            var day = new DateOnly(month.Year,month.Month,d); days.TryGetValue(day,out var rows);
            var pnl = rows?.Sum(t=>t.Pnl) ?? 0;
            var content = new StackPanel { Spacing = 3 };
            content.Children.Add(Label(d.ToString(),Pal.Muted,11));
            content.Children.Add(Label(rows == null ? "—" : LadderControl.Usd(pnl),rows == null ? Pal.Muted : pnl >= 0 ? Pal.Pos : Pal.Neg,13,true));
            content.Children.Add(Label(rows == null ? "" : $"{rows.Length} trades",Pal.Muted,10));
            var button = Flight.HeadBtn("", () => { selected=day; RenderBody(); });
            button.Content=content; button.Height=76; button.HorizontalAlignment=HorizontalAlignment.Stretch;
            button.Margin=new Thickness(2); button.BorderBrush=Pal.B(day == selected ? Pal.Accent : Pal.Edge);
            button.Background=Pal.B(day == selected ? Pal.SegOn : Pal.Card);
            var index=offset+d-1; Grid.SetColumn(button,index%7); Grid.SetRow(button,index/7+1); grid.Children.Add(button);
        }
        panel.Children.Add(grid); return Flight.Card("MONTHLY PERFORMANCE",panel);
    }

    private void ConfirmResetSim()
    {
        if (accounts.SelectedIndex <= 0 || environment.SelectedItem != "SIM") return;
        var account = (string)accounts.SelectedItem!;
        var dialog = new Window { Title = "Reset SIM journal", Width = 520, SizeToContent = SizeToContent.Height,
            Background = Pal.B(Pal.Bg), Foreground = Pal.B(Pal.Text), FontFamily = Flight.Sans,
            WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Children.Add(Label("RESET JOURNAL · " + account, Pal.Warn, 18, true));
        panel.Children.Add(Label("Clear recorded trades for this SIM account across ALL dates and instruments? A backup is saved first. Other accounts and LIVE history are untouched.", Pal.Text));
        panel.Children.Add(Label("This does not reset NinjaTrader, balances, drawdown limits or open positions. Open trades will still be recorded when they close.", Pal.Muted));
        panel.Children.Add(Flight.Primary("RESET JOURNAL", () => {
            try
            {
                var backup = state.ResetSimJournal(account);
                FillSelectors(); RenderBody();
                status.Text = account + " journal reset. Backup: " + backup;
                dialog.Close();
            }
            catch (Exception e) { status.Text = "Reset failed: " + e.Message; dialog.Close(); }
        }));
        panel.Children.Add(Flight.HeadBtn("CANCEL", () => dialog.Close()));
        dialog.Content = panel; dialog.ShowDialog(this);
    }

    private void EditTrade(JournalTrade trade)
    {
        var editor = new Window { Title = "Trade review · " + trade.Inst, Width = 520, Height = 420,
            Background = Pal.B(Pal.Bg), FontFamily = Flight.Sans, Foreground = Pal.B(Pal.Text) };
        var setup = Flight.Text(400); setup.Text = trade.Setup;
        var notes = new TextBox { Text = trade.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            Height = 180, FontFamily = Flight.Sans, FontSize = 13, Background = Pal.B(Pal.Field), BorderBrush = Pal.B(Pal.Edge) };
        var panel = new StackPanel { Margin = new Thickness(16), Spacing = 12 };
        panel.Children.Add(Label($"{trade.Account} · {trade.Inst} · {LadderControl.Usd(trade.Pnl)} net",Pal.Text,14,true));
        panel.Children.Add(Flight.Labeled("SETUP",setup)); panel.Children.Add(Flight.Labeled("REVIEW NOTES",notes));
        panel.Children.Add(Flight.Primary("SAVE REVIEW", () => {
            var i = state.Journal.IndexOf(trade);
            if (i >= 0) { state.Journal[i] = trade with { Setup = setup.Text ?? "", Notes = notes.Text ?? "" }; state.SaveJournal(); RenderBody(); }
            editor.Close();
        }));
        editor.Content=panel; editor.ShowDialog(this);
    }
}

public sealed class JournalCurve(double[] equity) : Control
{
    public override void Render(DrawingContext context)
    {
        if (equity.Length < 2) return;
        var low=Math.Min(0,equity.Min()); var high=Math.Max(0,equity.Max()); var span=Math.Max(1,high-low);
        Point At(int i,double v) => new(16+i*(Bounds.Width-32)/Math.Max(1,equity.Length-1), Bounds.Height-16-(v-low)/span*(Bounds.Height-32));
        context.DrawLine(new Pen(Pal.B(Pal.Edge),1),At(0,0),At(equity.Length-1,0));
        void Text(string value, double x, double y) => context.DrawText(new FormattedText(value,CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,new Typeface(Pal.Mono),10,Pal.B(Pal.Muted)),new Point(x,y));
        Text("$"+high.ToString("N2"),16,0);
        Text("$"+low.ToString("N2"),16,Bounds.Height-14);
        Text("Cumulative realized P&L · trades in close order",150,0);
        double peak=0;
        for (var i=1;i<equity.Length;i++)
        {
            peak=Math.Max(peak,equity[i-1]); var nextPeak=Math.Max(peak,equity[i]);
            context.DrawLine(new Pen(Pal.B(Pal.Neg,0.35),1),At(i,equity[i]),At(i,nextPeak));
            context.DrawLine(new Pen(Pal.B(equity[^1]>=0 ? Pal.Pos : Pal.Neg),2),At(i-1,equity[i-1]),At(i,equity[i]));
        }
    }
}

public sealed class JournalSplit(int first, int second, int flat, Color firstColor, Color secondColor, string firstLabel, string secondLabel) : Control
{
    public override void Render(DrawingContext context)
    {
        var total=first+second+flat;
        context.DrawEllipse(null,new Pen(Pal.B(Pal.Edge),12),new Point(44,48),30,30);
        if (total>0)
        {
            double start=-Math.PI/2;
            foreach (var (count,color) in new[] { (first,firstColor),(second,secondColor),(flat,Pal.Muted) })
            {
                var end=start+2*Math.PI*count/total;
                var steps=Math.Max(1,(int)(100.0*count/total));
                if(count>0)
                {
                    var geometry = new StreamGeometry();
                    using(var path=geometry.Open())
                    {
                        path.BeginFigure(new Point(44+30*Math.Cos(start),48+30*Math.Sin(start)),false);
                        for(var i=1;i<=steps;i++)
                        {
                            var angle=start+(end-start)*i/steps;
                            path.LineTo(new Point(44+30*Math.Cos(angle),48+30*Math.Sin(angle)));
                        }
                        path.EndFigure(false);
                    }
                    context.DrawGeometry(null,new Pen(Pal.B(color),12),geometry);
                }
                start=end;
            }
        }
        void Text(string text,double y,Color color) => context.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,new Typeface(Flight.Sans),12,Pal.B(color)),new Point(92,y));
        Text($"{firstLabel}  {first}",20,firstColor); Text($"{secondLabel}  {second}",43,secondColor);
        if(flat>0) Text($"FLAT  {flat}",66,Pal.Muted);
    }
}
