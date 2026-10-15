using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

/// <summary>
/// Every user setting in one window, flight-panel style: a section rail on the left, one section at a
/// time on the right as cards of rows (name and a one-line hint on the left, the control on the right),
/// SAVE / CANCEL fixed at the bottom. Colours preview live; Cancel (or closing) puts them back. Save
/// applies everything and rebuilds the analytics from NinjaTrader's history.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly Settings c;
    private readonly Action saved;
    private readonly List<Action> apply = new();
    private readonly Dictionary<string, string> colorsAtOpen;
    private bool done;

    private readonly List<(string Name, string Blurb, Control Body)> sections = new();
    private readonly List<Button> nav = new();
    private readonly ContentControl body = new();
    private readonly TextBlock title = new() { FontFamily = Flight.Sans, FontSize = 20, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock blurb = new() { FontFamily = Flight.Sans, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    private readonly List<Action> refreshSwatches = new();

    public SettingsWindow(Settings cfg, Action onSaved, string? open = null)
    {
        c = cfg;
        saved = onSaved;
        colorsAtOpen = Pal.Overrides();
        Title = "BookDepthIQ DOM — settings";
        Icon = Brand.Icon();
        Width = 920;
        Height = 700;
        MinWidth = 720;
        MinHeight = 480;
        Background = Pal.B(Pal.Bg);
        FontFamily = Flight.Sans;
        FontSize = 12.5;
        title.Foreground = Pal.B(Pal.Text);
        blurb.Foreground = Pal.B(Pal.Muted);

        BuildSections();

        // Rail
        var rail = new StackPanel { Spacing = 2, Margin = new Thickness(10, 16, 10, 10) };
        rail.Children.Add(new TextBlock { Text = "SETTINGS", FontSize = 11, FontWeight = FontWeight.Bold, LetterSpacing = 2, Foreground = Pal.B(Pal.Accent), Margin = new Thickness(12, 0, 0, 14) });
        for (var i = 0; i < sections.Count; i++)
        {
            var k = i;
            var b = new Button
            {
                Content = new TextBlock { Text = sections[i].Name.ToUpperInvariant(), LetterSpacing = 0.8 }, FontSize = 11.5, FontWeight = FontWeight.SemiBold, Height = 34,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(12, 0), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(3, 0, 0, 0),
            };
            b.Click += (_, _) => Show(k);
            nav.Add(b);
            rail.Children.Add(b);
        }
        var railBox = new Border { Width = 200, Background = Pal.B(Pal.Panel), BorderBrush = Pal.B(Pal.Edge), BorderThickness = new Thickness(0, 0, 1, 0), Child = rail };

        // Footer
        var hint = new TextBlock { Text = "Esc cancels · Ctrl+S saves · colours preview live", FontSize = 11, Foreground = Pal.B(Pal.Muted), VerticalAlignment = VerticalAlignment.Center };
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        btns.Children.Add(Flight.HeadBtn("CANCEL", Close));
        btns.Children.Add(Flight.Primary("SAVE", Save, "save and rebuild the indicators from history"));
        var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(24, 10, 18, 10) };
        Grid.SetColumn(btns, 1);
        foot.Children.Add(hint);
        foot.Children.Add(btns);
        var footBox = new Border { Background = Pal.B(Pal.Panel), BorderBrush = Pal.B(Pal.Edge), BorderThickness = new Thickness(0, 1, 0, 0), Child = foot };

        // Section header + body
        var head = new StackPanel { Margin = new Thickness(24, 18, 24, 8) };
        head.Children.Add(title);
        head.Children.Add(blurb);
        var scroll = new ScrollViewer { Content = body, Padding = new Thickness(16, 0, 16, 16) };
        var main = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        main.Children.Add(head);
        main.Children.Add(scroll);

        var root = new DockPanel();
        DockPanel.SetDock(footBox, Dock.Bottom);
        DockPanel.SetDock(railBox, Dock.Left);
        root.Children.Add(footBox);
        root.Children.Add(railBox);
        root.Children.Add(main);
        Content = root;

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control)) { Save(); e.Handled = true; }
        };
        Closing += (_, _) => { if (!done) Pal.Apply(colorsAtOpen); };

        var start = open == null ? 0 : sections.FindIndex(x => x.Name.Equals(open, StringComparison.OrdinalIgnoreCase));
        Show(Math.Max(0, start));
    }

    private void Show(int i)
    {
        body.Content = sections[i].Body;
        title.Text = sections[i].Name;
        blurb.Text = sections[i].Blurb;
        for (var k = 0; k < nav.Count; k++)
        {
            var on = k == i;
            nav[k].Background = Pal.B(on ? Pal.SegOn : Colors.Transparent);
            nav[k].Foreground = Pal.B(on ? Pal.Accent : Pal.Muted);
            nav[k].BorderBrush = on ? Pal.B(Pal.Accent) : Brushes.Transparent;
        }
    }

    private void Save()
    {
        foreach (var a in apply) a();
        c.Colors = Pal.Overrides();
        c.Save();
        done = true;
        saved();
        Close();
    }

    // ───────────── sections ─────────────

    private void BuildSections()
    {
        sections.Add(("Account", "Your prop firm's numbers. ROOM = equity − floor, or today's limit if that is tighter.", Cards(
            Card("RULES",
                Row("Start balance", "$ the account started with", Num(c.Start, v => c.Start = v)),
                Row("Max drawdown", "$ from the peak (or start) to the floor", Num(c.MaxDrawdown, v => c.MaxDrawdown = v)),
                Row("Drawdown type", "how the floor moves", Seg(new[] { "Static", "Intraday", "EOD" }, (int)c.DrawdownKind, i => c.DrawdownKind = (DrawdownKind)i)),
                Row("Stops trailing at", "$ equity where the floor locks (0 = never)", Num(c.LockAt, v => c.LockAt = v)),
                Row("Prop daily loss", "$ the firm allows a day (0 = none)", Num(c.DailyLoss, v => c.DailyLoss = v))),
            Card("TRAILING PEAK",
                Row("Reset peak", "forget the highest equity seen on this account", Flight.HeadBtn("RESET", () => c.Peaks.Remove(c.Account)))))));

        sections.Add(("Fees & warnings", "Warnings never block: one click always sends; the warning shows in the ticket and the order log.", Cards(
            Card("FEES",
                Row("Fee per contract per side", "$ commission + exchange + NFA, your broker's", Num(c.FeePerSide, v => c.FeePerSide = v))),
            Card("WARNINGS",
                Row("Max contracts", "warn above this size (0 = none)", Num(c.MaxContracts, v => c.MaxContracts = (int)v)),
                Row("Size × your normal", "warn when an entry is this many times your usual size", Num(c.SizeMult, v => c.SizeMult = v)),
                Row("Far from market, ticks", "warn when a limit is this far away (0 = 4 × ATR)", Num(c.FarTicks, v => c.FarTicks = (int)v))))));

        sections.Add(("Protect my day", "Limits apply to NEW entries only. FLATTEN, CANCEL, CLOSE HALF, B/E and KILL always work. The day starts 17:00 Chicago.", Cards(
            Card("LIMITS",
                Row("Daily loss limit", "$ (0 = off)", Num(c.DayLossUsd, v => c.DayLossUsd = v)),
                Row("Max trades a day", "0 = off", Num(c.DayMaxTrades, v => c.DayMaxTrades = (int)v)),
                Row("Cooldown after a loss", "seconds (0 = off)", Num(c.CooldownSec, v => c.CooldownSec = (int)v))),
            Card("AUTO-CLOSE · ACTS WITHOUT A CLICK",
                Row("Close everything at the limit", "once the daily loss limit is hit", Bool(c.CloseAtDayLimit, b => c.CloseAtDayLimit = b)),
                Row("Close before the weekend halt", "Friday, 5 minutes before the close time", Bool(c.WeekendClose, b => c.WeekendClose = b)),
                Row("Friday close", "HH:MM Chicago", Text(c.WeekendCloseTime, v => c.WeekendCloseTime = v, 90))))));

        sections.Add(("Ladder", "Rows, columns and what the ladder tracks. Drag a column header divider to resize; double-click resets.", Cards(
            Card("ROWS",
                Row("Row height", "px", Num(c.RowHeight, v => c.RowHeight = v)),
                Row("Ticks per row", "group the ladder", Seg(LadderMath.GroupSteps.Select(x => x.ToString(CultureInfo.InvariantCulture)).ToArray(), Math.Max(0, Array.IndexOf(LadderMath.GroupSteps, c.Group)), i => c.Group = LadderMath.GroupSteps[i]))),
            Card("COLUMNS",
                Row("BUY / SELL order columns", null, Bool(c.Columns.Orders, b => c.Columns.Orders = b)),
                Row("SOLD / BOUGHT footprint", null, Bool(c.Columns.Footprint, b => c.Columns.Footprint = b)),
                Row("P&L", "open P&L at each price", Bool(c.Columns.Pnl, b => c.Columns.Pnl = b)),
                Row("Heat · pull/stack", "resting size over time, far left", Bool(c.Columns.Heat, b => c.Columns.Heat = b)),
                Row("Volume profile", "visible range", Bool(c.Columns.Profile, b => c.Columns.Profile = b)),
                Row("Δ delta", "aggressive buys − sells per price", Bool(c.Columns.Delta, b => c.Columns.Delta = b))),
            Card("HEAT · nPOC",
                Row("Heat · pull/stack window", "how far back the heatmap reaches", Seg(new[] { "5M", "15M", "30M", "60M" }, Idx(new[] { 5, 15, 30, 60 }, c.HeatMinutes, 0), i => c.HeatMinutes = new[] { 5, 15, 30, 60 }[i])),
                Row("Heat contrast", "how much typical-size levels fade so big liquidity stands out", Seg(new[] { "SOFT", "NORMAL", "SHARP" }, Math.Clamp(c.HeatContrast, 0, 2), i => c.HeatContrast = i)),
                Row("Naked POC lines", "each period's POC until price trades there again", Bool(c.NpocOn, b => c.NpocOn = b)),
                Row("nPOC period", null, Seg(new[] { "5M", "15M", "30M", "60M" }, Idx(new[] { 5, 15, 30, 60 }, c.NpocMinutes, 1), i => c.NpocMinutes = new[] { 5, 15, 30, 60 }[i]))),
            Card("LINES · DATA",
                Row("SuperTrend lines", "ST1 / ST2 and the no-chase line", Bool(c.ShowTrend, b => c.ShowTrend = b)),
                Row("History to load", "hours, 1–48", Num(c.HistoryHours, v => c.HistoryHours = Math.Clamp((int)v, 1, 48)))))));

        sections.Add(("Tape & log", "Which prints the tape shows, and the text size for the tape and the order log; rows grow with it. Ctrl+wheel over either changes the size on the fly.", Cards(
            Card("FILTER",
                Row("Tape: show prints of", "≥ contracts (default 1 = every print; e.g. 3, 10, 100)", Num(c.TapeMinSize, v => c.TapeMinSize = Math.Max(1, (long)v)))),
            Card("SIZE",
                Row("Tape text", "px, 8–22 (default 11.5)", Num(c.TapeFont, v => c.TapeFont = Settings.ClampFont(v))),
                Row("Order log text", "px, 8–22 (default 11)", Num(c.LogFont, v => c.LogFont = Settings.ClampFont(v)))))));

        var ind = c.IndicatorsFor(c.Instrument);
        sections.Add(("Indicators", "ADX, VWAP, profile, footprint and big trades — one set for every instrument.", Cards(
            Card("ADX · MARKET STATE",
                Row("ADX timeframe", "completed candles; independent of nPOC and SuperTrend", Seg(new[]{"1M","3M","5M","10M","15M","30M"}, Idx(AdxBars.Timeframes,c.AdxMinutes,1), i=>c.AdxMinutes=AdxBars.Timeframes[i])),
                Row("ADX period", "bars, 1–100 (default 14)", Num(c.AdxPeriod,v=>c.AdxPeriod=double.IsFinite(v)?(int)Math.Clamp(v,1,100):14)),
                Row("Trend threshold", "ADX at or above this = trend; default 25", Num(c.AdxThreshold,v=>c.AdxThreshold=v)),
                new TextBlock {Text="Strength only, not a prediction. History expands automatically (up to 48h); ADX — until enough completed bars arrive.",FontSize=11,Foreground=Pal.B(Pal.Muted),TextWrapping=TextWrapping.Wrap}),
            Card("VWAP",
                Row("VWAP + σ bands", null, Bool(ind.VwapOn, b => ind = ind with { VwapOn = b })),
                Row("Session start", "HH:MM", Text(ind.VwapStart, v => ind = ind with { VwapStart = v }, 90)),
                Row("Session end", "HH:MM", Text(ind.VwapEnd, v => ind = ind with { VwapEnd = v }, 90)),
                Row("Time zone", "IANA name, e.g. America/Chicago", Text(ind.VwapTz, v => ind = ind with { VwapTz = v })),
                Row("Bands", "σ, comma separated (e.g. 1, 2)", Text(string.Join(", ", ind.VwapBands.Select(Ui.F)), v => ind = ind with { VwapBands = IndicatorCfg.ParseBands(v) }, 110))),
            Card("PROFILE · Δ · FOOTPRINT",
                Row("Start time", "HH:MM; blank = when the DOM opened", Text(ind.ProfileStart, v => ind = ind with { ProfileStart = v }, 90)),
                Row("Backfill from history", "off = live prints only; on = NinjaTrader's history from the start time", Bool(ind.ProfileHistory, b => ind = ind with { ProfileHistory = b }))),
            Card("FOOTPRINT IMBALANCES",
                Row("Imbalances", null, Bool(ind.FpOn, b => ind = ind with { FpOn = b })),
                Row("Ratio", "3 = 3:1", Num(ind.FpRatio, v => ind = ind with { FpRatio = v })),
                Row("Min row volume", "% of the busiest row", Num(ind.FpMinPct, v => ind = ind with { FpMinPct = v })),
                Row("Stack", "rows in a row", Num(ind.FpStack, v => ind = ind with { FpStack = (int)v })),
                Row("Stack zones as lines", null, Bool(ind.FpLines, b => ind = ind with { FpLines = b }))),
            Card("BIG TRADES",
                Row("Big trades on the tape", null, Bool(ind.BigOn, b => ind = ind with { BigOn = b })),
                Row("Big trade", "≥ contracts", Num(ind.BigMin, v => ind = ind with { BigMin = (long)v }))))));
        apply.Add(() => c.SetIndicators(c.Instrument, ind));

        var gx = c.Gex;
        sections.Add(("GEX", "Dealer gamma levels from CBOE's free delayed option chains: ES/MES → SPX, NQ/MNQ → QQQ, YM → DIA, GC → GLD, CL → USO. " +
                             "Positioning is inferred, not observed; levels, not direction. Read-only: GEX never touches an order.", Cards(
            Card("GEX LEVELS",
                Row("GEX on the ladder", "call wall, put wall, gamma flip, magnet, numbered levels", Bool(gx.On, b => gx.On = b)),
                Row("Expiries", "4 nearest (default) · today's only · every expiry", Seg(new[] { "NEAR 4", "0DTE", "ALL" }, (int)gx.Scope, i => gx.Scope = (GexStore.Scope)i)),
                Row("Map", "daily open-interest map (moves once a day) or recalculated every poll", Seg(new[] { "DAILY", "RECALC" }, gx.Live ? 1 : 0, i => gx.Live = i == 1))),
            Card("DRAW",
                Row("Numbered levels", "GEX 1…n, strongest first, within ~2 days' move (0–7)", Num(gx.TopN, v => gx.TopN = Math.Clamp((int)v, 0, 7))),
                Row("Expected range", "one-day move from iv30", Bool(gx.ShowRange, b => gx.ShowRange = b)),
                Row("Magnet", "the heaviest combined call + put strike (the pin)", Bool(gx.ShowMagnet, b => gx.ShowMagnet = b)),
                Row("Line width", "px (walls and flip draw thicker); colours in Colours › GEX", Num(gx.LineWidth, v => gx.LineWidth = Math.Clamp(v, 0.5, 6)))))));

        sections.Add(("SuperTrend","ST1 is the slow line, ST2 the fast one. The scalp read is built on ST1.", Cards(
            Trend("ST1", c.St1, t => c.St1 = t), Trend("ST2", c.St2, t => c.St2 = t))));

        // Every OR setting in one place. One ON switch and one MID switch (they used to be split between
        // here and Indicators); defaults are the Vest panel's (BookDepthIQ PAXOR RTH).
        var or = c.PaxOr;
        var orInd = c.IndicatorsFor(c.Instrument);
        bool orOn = or.Enabled && orInd.OrOn, orMid = or.ShowMid || orInd.OrMid;
        sections.Add(("Opening range", "BookDepthIQ PAXOR, as in the Vest panel: OR = high/low of the first seconds after the bell, kept until the next weekday bell; EXT rungs every step beyond it (NQ/MNQ and ES/MES).", Cards(
            Card("OPENING RANGE",
                Row("Opening range on the ladder", "OR H (blue), OR L (red), dashed while forming", Bool(orOn, b => orOn = b)),
                Row("Mid line", "OR MID (gold)", Bool(orMid, b => orMid = b))),
            Card("WINDOW",
                Row("Bell", "HH:MM:SS (default 08:30:00)", Text(or.StartTime, v => or = or with { StartTime = v }, 110)),
                Row("Window", "seconds after the bell (default 30)", Num(or.DurationSec, v => or = or with { DurationSec = Math.Max(1, (int)v) })),
                Row("Time zone", "IANA name (default America/Chicago)", Text(or.Timezone, v => or = or with { Timezone = v }))),
            Card("EXT RUNGS",
                Row("NQ / MNQ step", "points (default 65)", Num(or.NqLevelPoints, v => or = or with { NqLevelPoints = v })),
                Row("ES / MES step", "points (default 15)", Num(or.EsLevelPoints, v => or = or with { EsLevelPoints = v }))))));
        apply.Add(() =>
        {
            c.PaxOr = or with { Enabled = orOn, ShowMid = orMid };
            c.SetIndicators(c.Instrument, c.IndicatorsFor(c.Instrument) with { OrOn = orOn, OrMid = orMid });
        });

        // Initial Balance (BookDepthIQ's IB layer): all 12 ranges are always tracked; these choose which to draw.
        var ib = c.Ib;
        var lens = Core.Ib.Lengths;
        sections.Add(("Initial balance", "The high and low of the first minutes after the open, from every trade (NinjaTrader's history, then live). " +
                                         "RTH opens at the OR bell for index futures (CL 08:00, metals and treasuries 07:20, Chicago); Globex at 17:00. " +
                                         "Each IB that forms is kept in ib-history.jsonl for the median, percentile and previous-day stats.", Cards(
            Card("INITIAL BALANCE",
                Row("IB on the ladder", "IB H (cyan) and IB L (orange), dashed while forming; the IB pill in the header", Bool(ib.On, b => ib.On = b)),
                Row("Session", "RTH open or the 17:00 Globex open", Seg(new[] { "RTH", "GLOBEX" }, ib.Session == IbSession.Rth ? 0 : 1, i => ib.Session = i == 0 ? IbSession.Rth : IbSession.Globex)),
                Row("Length", "minutes after the open (default 15)", Seg(lens.Select(x => x + "M").ToArray(), Idx(lens, ib.LengthMin, 3), i => ib.LengthMin = lens[i]))),
            Card("DRAW",
                Row("Mid line", null, Bool(ib.ShowMid, b => ib.ShowMid = b)),
                Row("Extensions", "1.5×, 2×, 3× the range above and below, once formed", Bool(ib.ShowExtensions, b => ib.ShowExtensions = b)),
                Row("Value area", "IB VAH / VAL: 70% of the IB window's volume around its POC", Bool(ib.ShowValueArea, b => ib.ShowValueArea = b)),
                Row("Line width", "px; colours in Colours › Chart lines", Num(ib.LineWidth, v => ib.LineWidth = Math.Clamp(v, 0.5, 6)))))));

        sections.Add(("Colours", "Every colour the DOM draws. Changes show live; SAVE keeps them, CANCEL puts them back.", ColoursBody()));
    }

    private Control ColoursBody()
    {
        var presets = new Seg(Pal.Presets.Select(p => p.Name.ToUpperInvariant()).ToArray()) { Width = 440 };
        presets.SelectedIndex = Math.Max(0, Array.FindIndex(Pal.Presets, p => SameColours(p.Colors, Pal.Overrides())));
        presets.SelectionChanged += (_, _) => { Pal.Apply(Pal.Presets[presets.SelectedIndex].Colors); Repaint(); };
        var cards = new List<Control>
        {
            Card("SCHEME",
                Row("Preset", "a starting point; tune any colour below", presets),
                Row("Reset all", "back to the default colours", Flight.HeadBtn("RESET", () => { Pal.Apply(new Dictionary<string, string>()); presets.SelectedIndex = 0; Repaint(); }))),
        };
        foreach (var grp in Pal.Tokens.GroupBy(t => t.Group))
            cards.Add(Card(grp.Key.ToUpperInvariant(), grp.Select(t => Row(t.Label, t.Tip, ColourField(t))).ToArray()));
        return Cards(cards.ToArray());
    }

    private static bool SameColours(Dictionary<string, string> a, Dictionary<string, string> b) =>
        a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && string.Equals(v, kv.Value, StringComparison.OrdinalIgnoreCase));

    private void Repaint()
    {
        foreach (var r in refreshSwatches) r();
        Show(sections.Count - 1);
    }

    private static readonly string[] Swatches =
    {
        "#000000", "#0b0e13", "#121722", "#1f2633", "#2a3142", "#4b5563", "#7b8597", "#b0b8c4", "#d7dde8", "#ffffff",
        "#22d3ee", "#06b6d4", "#00e5ff", "#38bdf8", "#0ea5e9", "#3b82f6", "#60a5fa", "#6366f1", "#0e3a44", "#16324a",
        "#34d399", "#10b981", "#22c55e", "#00ff88", "#84cc16", "#2ec4b6", "#065f46", "#14532d", "#a3e635", "#bef264",
        "#fbbf24", "#eab308", "#f59e0b", "#ffd400", "#fb923c", "#f97316", "#f59e5b", "#ff8c00", "#44250e", "#78350f",
        "#f87171", "#ef4444", "#ff5c5c", "#dc2626", "#4a1414", "#5a1414", "#f472b6", "#ec4899", "#e879f9", "#a78bfa",
    };

    /// <summary>A colour: swatch (click for a palette) + hex box + reset. Applies live.</summary>
    private Control ColourField(Pal.Token t)
    {
        var swatch = new Button { Width = 34, Height = 26, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), BorderBrush = Pal.B(Pal.Edge), Padding = new Thickness(0) };
        var hex = Flight.Text(92);
        hex.FontFamily = Pal.Mono;
        hex.FontSize = 12.5;
        void Paint()
        {
            var w = Pal.Wanted(t.Key);
            swatch.Background = new SolidColorBrush(w);
            hex.Text = Pal.Hex(w);
            hex.BorderBrush = Pal.B(Pal.Edge);
        }
        void SetTo(Color col)
        {
            Pal.Set(t.Key, col);
            Paint();
        }
        void Commit()
        {
            if (Pal.TryParse(hex.Text, out var col)) SetTo(col);
            else hex.BorderBrush = Pal.B(Pal.Neg);
        }
        hex.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        hex.LostFocus += (_, _) => Commit();

        var grid = new UniformGrid { Columns = 10, Margin = new Thickness(2) };
        var fly = new Flyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        foreach (var s in Swatches)
        {
            var col = Color.Parse(s);
            var b = new Button { Width = 22, Height = 22, Margin = new Thickness(2), Padding = new Thickness(0), CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(col), BorderBrush = Pal.B(Pal.Edge), BorderThickness = new Thickness(1) };
            ToolTip.SetTip(b, s);
            b.Click += (_, _) => { SetTo(col); fly.Hide(); };
            grid.Children.Add(b);
        }
        var flyBody = new StackPanel { Spacing = 6 };
        flyBody.Children.Add(new TextBlock { Text = t.Label.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.2, Foreground = Pal.B(Pal.Muted) });
        flyBody.Children.Add(grid);
        flyBody.Children.Add(new TextBlock { Text = "or type any #rrggbb in the box", FontSize = 10.5, Foreground = Pal.B(Pal.Muted) });
        fly.Content = flyBody;
        swatch.Flyout = fly;

        var reset = Flight.HeadBtn("↺", () => SetTo(t.Default), $"default {Pal.Hex(t.Default)}");
        reset.Padding = new Thickness(9, 0);
        reset.Margin = new Thickness(0);

        Paint();
        refreshSwatches.Add(Paint);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(swatch);
        row.Children.Add(hex);
        row.Children.Add(reset);
        return row;
    }

    // ───────────── layout ─────────────

    private static Control Cards(params Control[] cards)
    {
        var sp = new StackPanel { MaxWidth = 680, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (var k in cards) sp.Children.Add(k);
        return sp;
    }

    private static Control Card(string title, params Control[] rows)
    {
        var sp = new StackPanel();
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0) sp.Children.Add(new Border { Height = 1, Background = Pal.B(Pal.Edge), Margin = new Thickness(0, 2) });
            sp.Children.Add(rows[i]);
        }
        return Flight.Card(title, sp);
    }

    private static Control Row(string label, string? hint, Control field)
    {
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        left.Children.Add(new TextBlock { Text = label, FontSize = 13, Foreground = Pal.B(Pal.Text) });
        if (!string.IsNullOrEmpty(hint)) left.Children.Add(new TextBlock { Text = hint, FontSize = 11, Foreground = Pal.B(Pal.Muted), TextWrapping = TextWrapping.Wrap });
        field.VerticalAlignment = VerticalAlignment.Center;
        field.HorizontalAlignment = HorizontalAlignment.Right;
        field.Margin = new Thickness(16, 0, 0, 0);
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinHeight = 44, Margin = new Thickness(2, 3) };
        Grid.SetColumn(field, 1);
        g.Children.Add(left);
        g.Children.Add(field);
        return g;
    }

    private static int Idx(int[] items, int v, int dflt) => Array.IndexOf(items, v) is var i && i >= 0 ? i : dflt;

    // ───────────── fields ─────────────

    private Control Num(double v, Action<double> set)
    {
        var t = Flight.Num(110);
        t.Text = Ui.F(v);
        apply.Add(() => set(Ui.Num(t, v)));
        return t;
    }

    private Control Text(string v, Action<string> set, double width = 200)
    {
        var t = Flight.Text(width);
        t.Text = v;
        apply.Add(() => set(t.Text?.Trim() ?? v));
        return t;
    }

    private Control Bool(bool v, Action<bool> set)
    {
        var k = Flight.Switch(v);
        apply.Add(() => set(k.IsChecked == true));
        return k;
    }

    private Control Seg(string[] items, int index, Action<int> set)
    {
        var s = new Seg(items) { Width = Math.Max(150, items.Length * 62) };
        s.SelectedIndex = index;
        apply.Add(() => set(s.SelectedIndex));
        return s;
    }

    private Control Trend(string name, TrendConfig t, Action<TrendConfig> set)
    {
        var cur = t;
        var card = Card(name,
            Row("On", null, Bool(t.Enabled, b => cur = cur with { Enabled = b })),
            Row("Candle", "seconds", Num(t.IntervalSec, v => cur = cur with { IntervalSec = Math.Max(1, (int)v) })),
            Row("ATR candles", null, Num(t.NumCandles, v => cur = cur with { NumCandles = Math.Max(1, (int)v) })),
            Row("Multiplier", "× ATR", Num(t.Multiplier, v => cur = cur with { Multiplier = v })),
            Row("Flips on", "what turns the line", Seg(Enum.GetNames<SwitchCond>(), (int)t.Switch, i => cur = cur with { Switch = (SwitchCond)i })),
            Row("Confirm candles", null, Num(t.Confirm, v => cur = cur with { Confirm = Math.Max(1, (int)v) })),
            Row("Volume ×", "for the Volume flip", Num(t.VolMult, v => cur = cur with { VolMult = v })),
            Row("Min delta", "for the Delta flips", Num(t.DeltaMin, v => cur = cur with { DeltaMin = v })));
        apply.Add(() => set(cur));
        return card;
    }
}
