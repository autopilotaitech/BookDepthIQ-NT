using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

/// <summary>Every user setting in one window: account rules, fees and warnings, protect my day, ladder and
/// columns, indicators (per instrument), SuperTrend and the opening range. Save applies and rebuilds the
/// analytics from NinjaTrader's history.</summary>
public sealed class SettingsWindow : Window
{
    private readonly Settings c;
    private readonly Action saved;
    private readonly List<Action> apply = new();

    public SettingsWindow(Settings cfg, Action onSaved)
    {
        c = cfg;
        saved = onSaved;
        Title = "BookDepthIQ DOM — settings";
        Width = 760;
        Height = 620;
        Background = Pal.B(Pal.Bg);
        FontFamily = Pal.Mono;
        FontSize = 12;

        var ind = c.IndicatorsFor(c.Instrument);
        var tabs = new TabControl { Padding = new Thickness(6) };
        tabs.Items.Add(Tab("Account", Grid(
            ("Start balance $", Num(c.Start, v => c.Start = v)),
            ("Max drawdown $", Num(c.MaxDrawdown, v => c.MaxDrawdown = v)),
            ("Drawdown type", Pick(new[] { "static", "trailing intraday", "trailing EOD" }, (int)c.DrawdownKind, i => c.DrawdownKind = (DrawdownKind)i)),
            ("Stops trailing at $ (0 = never)", Num(c.LockAt, v => c.LockAt = v)),
            ("Prop daily loss $ (0 = none)", Num(c.DailyLoss, v => c.DailyLoss = v)),
            ("Reset trailing peak", Ui.Btn("reset to start", () => { c.Peaks.Remove(c.Account); }, "forget the highest equity seen on this account")),
            ("", Ui.Label("Your prop firm's numbers. Room = equity − floor (or today's limit if tighter).", Pal.Muted, size: 11, wrap: true)))));
        tabs.Items.Add(Tab("Fees & warnings", Grid(
            ("Fee per contract per side $", Num(c.FeePerSide, v => c.FeePerSide = v)),
            ("Max contracts (0 = none)", Num(c.MaxContracts, v => c.MaxContracts = (int)v)),
            ("Warn at size × your normal", Num(c.SizeMult, v => c.SizeMult = v)),
            ("Warn when this far, ticks (0 = 4 × ATR)", Num(c.FarTicks, v => c.FarTicks = (int)v)),
            ("", Ui.Label("Warnings never block: one click always sends, the warning shows in the ticket and order log.", Pal.Muted, size: 11, wrap: true)))));
        tabs.Items.Add(Tab("Protect my day", Grid(
            ("Daily loss limit $ (0 = off)", Num(c.DayLossUsd, v => c.DayLossUsd = v)),
            ("Max trades a day (0 = off)", Num(c.DayMaxTrades, v => c.DayMaxTrades = (int)v)),
            ("Cooldown after a loss, s (0 = off)", Num(c.CooldownSec, v => c.CooldownSec = (int)v)),
            ("Close everything at the limit", Bool(c.CloseAtDayLimit, b => c.CloseAtDayLimit = b, "acts without a click once the daily loss limit is hit")),
            ("Close before the weekend halt", Bool(c.WeekendClose, b => c.WeekendClose = b, "Friday, 5 minutes before the close time below (Chicago)")),
            ("Friday close (Chicago)", Text(c.WeekendCloseTime, v => c.WeekendCloseTime = v)),
            ("", Ui.Label("Limits apply to NEW entries only. FLATTEN, CANCEL, CLOSE HALF, B/E and KILL always work. The day starts 17:00 Chicago.", Pal.Muted, size: 11, wrap: true)))));
        tabs.Items.Add(Tab("Ladder", Grid(
            ("Row height px", Num(c.RowHeight, v => c.RowHeight = v)),
            ("Ticks per row", Pick(LadderMath.GroupSteps.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray(), Math.Max(0, Array.IndexOf(LadderMath.GroupSteps, c.Group)), i => c.Group = LadderMath.GroupSteps[i])),
            ("BUY / SELL order columns", Bool(c.Columns.Orders, b => c.Columns.Orders = b)),
            ("SOLD / BOUGHT footprint", Bool(c.Columns.Footprint, b => c.Columns.Footprint = b)),
            ("P&L column", Bool(c.Columns.Pnl, b => c.Columns.Pnl = b)),
            ("HEAT 5M · PULL/STACK", Bool(c.Columns.Heat, b => c.Columns.Heat = b)),
            ("Volume PROFILE", Bool(c.Columns.Profile, b => c.Columns.Profile = b)),
            ("Δ delta column", Bool(c.Columns.Delta, b => c.Columns.Delta = b)),
            ("SuperTrend lines", Bool(c.ShowTrend, b => c.ShowTrend = b)),
            ("Heat · pull/stack window", Pick(new[] { "5 min", "15 min", "30 min", "60 min" }, Array.IndexOf(new[] { 5, 15, 30, 60 }, c.HeatMinutes) is var hi && hi >= 0 ? hi : 0, i => c.HeatMinutes = new[] { 5, 15, 30, 60 }[i])),
            ("Naked POC lines (nPOC)", Bool(c.NpocOn, b => c.NpocOn = b, "each period's POC as a line until price trades there again")),
            ("nPOC period", Pick(new[] { "5 min", "15 min", "30 min", "60 min" }, Array.IndexOf(new[] { 5, 15, 30, 60 }, c.NpocMinutes) is var ni && ni >= 0 ? ni : 1, i => c.NpocMinutes = new[] { 5, 15, 30, 60 }[i])),
            ("History to load, hours (1–48)", Num(c.HistoryHours, v => c.HistoryHours = Math.Clamp((int)v, 1, 48))))));
        tabs.Items.Add(Tab($"Indicators ({CmeSpecs.RootOf(c.Instrument)})", Grid(
            ("VWAP + σ bands", Bool(ind.VwapOn, b => ind = ind with { VwapOn = b })),
            ("VWAP session start", Text(ind.VwapStart, v => ind = ind with { VwapStart = v })),
            ("VWAP session end", Text(ind.VwapEnd, v => ind = ind with { VwapEnd = v })),
            ("VWAP time zone", Text(ind.VwapTz, v => ind = ind with { VwapTz = v })),
            ("Bands σ (e.g. 1, 2)", Text(string.Join(", ", ind.VwapBands.Select(Ui.F)), v => ind = ind with { VwapBands = IndicatorCfg.ParseBands(v) })),
            ("Profile · Δ · footprint start (HH:MM, blank = DOM open)", Text(ind.ProfileStart, v => ind = ind with { ProfileStart = v })),
            ("Profile uses history from the start", Bool(ind.ProfileHistory, b => ind = ind with { ProfileHistory = b }, "off = live prints only; on = backfill from the start time with NinjaTrader's history (History hours must reach back that far)")),
            ("Footprint imbalances", Bool(ind.FpOn, b => ind = ind with { FpOn = b })),
            ("Imbalance ratio (3 = 3:1)", Num(ind.FpRatio, v => ind = ind with { FpRatio = v })),
            ("Min row volume, % of busiest", Num(ind.FpMinPct, v => ind = ind with { FpMinPct = v })),
            ("Stack: rows in a row", Num(ind.FpStack, v => ind = ind with { FpStack = (int)v })),
            ("Stack zones as lines", Bool(ind.FpLines, b => ind = ind with { FpLines = b })),
            ("Big trades on the tape", Bool(ind.BigOn, b => ind = ind with { BigOn = b })),
            ("Big trade ≥ contracts", Num(ind.BigMin, v => ind = ind with { BigMin = (long)v })),
            ("RTH opening range (NQ/ES)", Bool(ind.OrOn, b => ind = ind with { OrOn = b })),
            ("OR mid line", Bool(ind.OrMid, b => ind = ind with { OrMid = b })))));
        apply.Add(() => c.SetIndicators(c.Instrument, ind));
        tabs.Items.Add(Tab("SuperTrend", new StackPanel { Children = { Trend("ST1", c.St1, t => c.St1 = t), Trend("ST2", c.St2, t => c.St2 = t) } }));
        var or = c.PaxOr;
        tabs.Items.Add(Tab("Opening range", Grid(
            ("Enabled", Bool(or.Enabled, b => or = or with { Enabled = b })),
            ("Bell (HH:MM:SS)", Text(or.StartTime, v => or = or with { StartTime = v })),
            ("Window seconds", Num(or.DurationSec, v => or = or with { DurationSec = (int)v })),
            ("Time zone", Text(or.Timezone, v => or = or with { Timezone = v })),
            ("NQ EXT step, points", Num(or.NqLevelPoints, v => or = or with { NqLevelPoints = v })),
            ("ES EXT step, points", Num(or.EsLevelPoints, v => or = or with { EsLevelPoints = v })),
            ("", Ui.Label("BookDepthIQ PAXOR: OR = high/low of the first 30 s after the bell; EXT rungs every step beyond it.", Pal.Muted, size: 11, wrap: true)))));
        apply.Add(() => c.PaxOr = or);

        var buttons = Ui.Row(Ui.Btn("Save", Save, "save and rebuild the indicators from history"), Ui.Btn("Cancel", Close));
        var dock = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);
        dock.Children.Add(tabs);
        Content = dock;
    }

    private void Save()
    {
        foreach (var a in apply) a();
        c.Save();
        saved();
        Close();
    }

    private static TabItem Tab(string header, Control content) => new() { Header = header, FontSize = 12, Content = new ScrollViewer { Content = content } };

    private static Control Grid(params (string Label, Control Field)[] rows)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*"), Margin = new Thickness(8) };
        for (var i = 0; i < rows.Length; i++)
        {
            g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = Ui.Label(rows[i].Label, Pal.Muted);
            l.Margin = new Thickness(0, 4);
            Avalonia.Controls.Grid.SetRow(l, i);
            Avalonia.Controls.Grid.SetRow(rows[i].Field, i);
            Avalonia.Controls.Grid.SetColumn(rows[i].Field, 1);
            rows[i].Field.Margin = new Thickness(0, 3);
            rows[i].Field.HorizontalAlignment = HorizontalAlignment.Left;
            g.Children.Add(l);
            g.Children.Add(rows[i].Field);
        }
        return g;
    }

    private Control Num(double v, Action<double> set)
    {
        var t = Ui.Input(110);
        t.Text = Ui.F(v);
        apply.Add(() => set(Ui.Num(t, v)));
        return t;
    }

    private Control Text(string v, Action<string> set)
    {
        var t = Ui.Input(200);
        t.Text = v;
        apply.Add(() => set(t.Text?.Trim() ?? v));
        return t;
    }

    private Control Bool(bool v, Action<bool> set, string? tip = null)
    {
        var k = Ui.Check("", tip);
        k.IsChecked = v;
        apply.Add(() => set(k.IsChecked == true));
        return k;
    }

    private Control Pick(string[] items, int index, Action<int> set)
    {
        var cb = Ui.Combo(200, items);
        cb.SelectedIndex = index;
        apply.Add(() => { if (cb.SelectedIndex >= 0) set(cb.SelectedIndex); });
        return cb;
    }

    private Control Trend(string name, TrendConfig t, Action<TrendConfig> set)
    {
        var cur = t;
        var g = Grid(
            ($"{name} on", Bool(t.Enabled, b => cur = cur with { Enabled = b })),
            ($"{name} candle seconds", Num(t.IntervalSec, v => cur = cur with { IntervalSec = Math.Max(1, (int)v) })),
            ($"{name} ATR candles", Num(t.NumCandles, v => cur = cur with { NumCandles = Math.Max(1, (int)v) })),
            ($"{name} multiplier", Num(t.Multiplier, v => cur = cur with { Multiplier = v })),
            ($"{name} flips on", Pick(Enum.GetNames<SwitchCond>(), (int)t.Switch, i => cur = cur with { Switch = (SwitchCond)i })),
            ($"{name} confirm candles", Num(t.Confirm, v => cur = cur with { Confirm = Math.Max(1, (int)v) })),
            ($"{name} volume ×", Num(t.VolMult, v => cur = cur with { VolMult = v })),
            ($"{name} min delta", Num(t.DeltaMin, v => cur = cur with { DeltaMin = v })));
        apply.Add(() => set(cur));
        return g;
    }
}
