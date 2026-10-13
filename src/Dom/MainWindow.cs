using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

/// <summary>The DOM window — the Vest panel's layout: header (instrument, account, health, SuperTrend and
/// scalp read, RISK, settings, KILL), account and position strips, bracket bar, ladder with tape / journal /
/// order log, and the ticket with CME risk. Built in code so the compiler checks every control.</summary>
public sealed partial class MainWindow : Window
{
    private readonly Settings cfg = Settings.Load();
    private readonly DomState s;
    private readonly BridgeClient bridge;
    private readonly LadderControl ladder;
    private DrawdownTracker dd;
    private bool loading;
    private int cidSeq;
    private MarketState? lastState;

    // header
    private readonly TextBox instBox = Ui.Input(120, "NinjaTrader instrument, e.g. NQ 12-26 (Enter or Go)");
    private readonly ComboBox acctBox = Ui.Combo(140, Array.Empty<string>(), "NinjaTrader account (Sim101 to test)");
    private readonly Border bridgePill = Ui.Pill("BRIDGE", Pal.Warn);
    private readonly Border feedPill = Ui.Pill("FEED", Pal.Warn);
    private readonly Border trendPill = Ui.Pill("ST", Pal.Muted);
    private readonly Border scalpPill = Ui.Pill("SCALP", Pal.Muted);
    private readonly Border statePill = Ui.Pill("STATE", Pal.Muted);
    private readonly ToggleButton armBtn = Flight.Pill("HOTKEYS SAFE", "B / S / E / R hotkeys work only while armed");

    // strips
    private readonly TextBlock acctLine = Ui.Label("", Pal.Muted, size: 11.5);
    private readonly ProgressBar roomBar = new() { Width = 160, Height = 6, Minimum = 0, Maximum = 1, VerticalAlignment = VerticalAlignment.Center };
    private readonly Dictionary<string, TextBlock> stat = new();
    private readonly TextBlock bracketLine = Ui.Label("", Pal.Muted, size: 11.5);
    private readonly TextBlock lockLine = Ui.Label("", Pal.Neg, bold: true, size: 11.5, wrap: true);

    // side
    private TapeControl tapeView = null!;
    private LogControl logView = null!;
    private readonly TextBlock sideTitle = new() { FontSize = 10, Foreground = Pal.B(Pal.Muted), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private Grid sideGrid = null!;
    private StackPanel logButtons = null!;
    private ScrollViewer ticketPanel = null!;
    private readonly StackPanel journalList = new();
    private readonly ContentControl side = new();
    private ScrollViewer? journalScroll;
    private readonly TextBlock journalSum = Ui.Label("", Pal.Text, size: 11.5, wrap: true);

    // ticket
    private readonly Seg sizeMode = new(new[] { "CONTRACTS", "$ RISK", "% ROOM" }, "how size is set: a fixed number of contracts, $ lost at the stop (fees in), or % of the room left to your fail floor");
    private readonly TextBox sizeIn = Flight.Num(84, "contracts, $ risk at the stop, or % of room — by the mode above");
    private readonly WrapPanel quick = new() { Orientation = Orientation.Horizontal };
    private readonly Seg stopMode = new(new[] { "TICKS", "ATR", "SWING" }, "stop distance: your ticks, ST1 ATR × mult, or past the swing high/low");
    private readonly TextBox slIn = Flight.Num(70, "stop ticks"), atrIn = Flight.Num(70, "ATR multiple"), swingIn = Flight.Num(60, "swing lookback, minutes"), bufIn = Flight.Num(60, "ticks past the swing");
    private readonly TextBox[] tpIn = { Flight.Num(64, "TP1 ticks (R in ATR/swing mode)"), Flight.Num(64, "TP2 (0 = off)"), Flight.Num(64, "TP3 (0 = off)") };
    private readonly TextBox splitIn = Flight.Num(96, "size split across the targets that are on, e.g. 50/30/20");
    private readonly ToggleButton bracketBtn = Flight.Pill("BRACKET", "attach your TP/SL to every entry (one OCO pair per target, like NT's ATM)");
    private readonly ToggleButton beChk = Flight.Pill("B/E AFTER TP1", "when TP1 fills, the panel moves your stops to entry ± offset (needs 2+ targets). Acts without a click; off by default.");
    private readonly TextBox beOff = Flight.Num(56, "B/E offset ticks (+ in your favour)");
    private readonly Seg trailBox = new(new[] { "OFF", "R", "ST1", "ST2" }, "trailing stop: after N R, one risk behind the best price; or ON the ST1/ST2 line. Moves only your way; acts without a click.");
    private readonly TextBox trailR = Flight.Num(56, "start trailing after this many R");
    private readonly ToggleButton guardChk = Flight.Pill("STOP GUARD", "if a stop the panel saw on your position disappears, put it back at its last price");
    private readonly TextBox setupIn = Flight.Text(130, "setup tag saved with the trade in the journal");
    private readonly Seg previewBox = new(new[] { "OFF", "LONG", "SHORT" }, "preview: the would-be stop and targets on the ladder while flat; drag them to set distances. Nothing is sent.");
    private readonly ToggleButton scaleChk = Flight.Pill("SCALE", "a ladder click sends several limits from that price away from the market (no brackets: press B/E or set a stop after)");
    private readonly TextBox scaleCount = Flight.Num(52, "orders (2–10)"), scaleSpan = Flight.Num(56, "span ticks"), scaleSkew = Flight.Num(52, "size skew (1 = even)");
    private readonly Seg scaleDir = new(new[] { "BIG START", "EVEN", "BIG END" });
    private readonly Dictionary<string, TextBlock> readout = new();
    private readonly TextBlock riskLine = Ui.Label("", Pal.Text, size: 11.5, wrap: true);
    private readonly TextBlock checkLine = Ui.Label("", Pal.Warn, size: 11.5, wrap: true);
    private readonly TextBlock costLine = Ui.Label("", Pal.Muted, size: 11.5, wrap: true);
    private readonly TextBlock resultBar = Ui.Label("", Pal.Muted, size: 12, wrap: true);
    private readonly TextBlock notice = Ui.Label("", Pal.Warn, size: 12, wrap: true);
    private long noticeUntil;

    public MainWindow()
    {
        Title = "BookDepthIQ DOM — NinjaTrader";
        Width = 1380;
        Height = 900;
        Background = Pal.B(Pal.Bg);
        FontFamily = Pal.Mono;
        FontSize = 12;

        s = new DomState(cfg) { Account = cfg.Account };
        dd = NewTracker();
        bridge = new BridgeClient(s);
        ladder = new LadderControl(s, cfg)
        {
            Lines = LadderLines,
            PreviewLegs = PreviewLegs,
        };
        ladder.PlaceRequested += (buy, row, stop) => Place(buy, row, stop);
        ladder.CancelRequested += o => Send($"CANCEL {Leg(o)} @ {Px(o.Price)}", cid => Protocol.Cancel(cid, s.Account, o.Id), exit: true);
        ladder.MoveRequested += (o, t) => Send($"MOVE {Leg(o)} {Px(o.Price)} → {Px(t * s.TickSize)}", cid => Protocol.Move(cid, s.Account, o.Id, t * s.TickSize), exit: true);
        ladder.PreviewDropped += PreviewDropped;

        Content = Build();
        LoadFields();
        bridge.Connected += () =>
        {
            bridge.Send(Protocol.SelectAccount(cfg.Account));
            bridge.Send(Protocol.Subscribe(cfg.Instrument));
            bridge.Send(Protocol.History(cfg.Instrument, cfg.HistoryHours));
        };
        s.TradeClosed += OnTradeClosed;
        bridge.Start();
        // Repaint at most ~20×/s; bridge messages only change state. Management runs on the same tick.
        DispatcherTimer.Run(() => { Tick(); return true; }, TimeSpan.FromMilliseconds(50));
        KeyDown += OnKey;
        Closed += (_, _) => { SaveFields(); bridge.Dispose(); };

        // Dev only: BDIQ_SNAPSHOT=<file.png> saves a picture of the window after BDIQ_SNAPSHOT_SEC (4) s and exits.
        if (Environment.GetEnvironmentVariable("BDIQ_SNAPSHOT") is { Length: > 0 } shot)
            DispatcherTimer.RunOnce(() =>
            {
                using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height));
                bmp.Render(this);
                bmp.Save(shot);
                Close();
            }, TimeSpan.FromSeconds(double.TryParse(Environment.GetEnvironmentVariable("BDIQ_SNAPSHOT_SEC"), out var sec) ? sec : 4));
    }

    private DrawdownTracker NewTracker() => new(cfg.Rules, cfg.Peaks.TryGetValue(cfg.Account, out var pk) ? pk : null);

    // ───────────── layout ─────────────

    private Control Build()
    {
        instBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) SwitchInstrument(); };
        acctBox.SelectionChanged += (_, _) =>
        {
            if (acctBox.SelectedItem is not string a || a == cfg.Account) return;
            cfg.Account = a; s.Account = a; s.Positions.Clear(); s.Orders.Clear(); cfg.Save();
            dd = NewTracker();
            bridge.Send(Protocol.SelectAccount(a));
        };
        armBtn.IsCheckedChanged += (_, _) => { if (loading) return; cfg.HotkeysArmed = armBtn.IsChecked == true; cfg.Save(); };

        var header = Ui.Bar(
            Ui.Label("BookDepthIQ", Pal.Text, bold: true), instBox, Ui.Btn("Go", SwitchInstrument), acctBox, bridgePill, feedPill, trendPill, scalpPill, statePill,
            Ui.Btn("RISK", () => new RiskWindow(cfg, s, dd, ApplyRiskPlan).Show(this), "risk calculator for every CME contract; Use in DOM writes the plan into the ticket"),
            Ui.Btn("SETTINGS", () => new SettingsWindow(cfg, OnSettingsSaved).Show(this), "account rules, protect my day, indicators, SuperTrend, ladder columns, fees"),
            Ui.Btn("KILL ⏻", Kill, "closes every position and cancels every order on this account (Ctrl+K). Never blocked.", Pal.Neg, Color.Parse("#3a1010")));

        var acctRow = Ui.Bar(acctLine, roomBar);
        var statRow = new WrapPanel { Margin = new Thickness(8, 4) };
        foreach (var (k, tip) in new[]
        {
            ("POSITION", ""), ("AVG", ""), ("FAIL PRICE", "price where your account hits its floor at this size"), ("OPEN P&L", ""), ("TO FLOOR", "ticks against you before the account fails"),
            ("POC · VA", "visible-range volume profile: point of control and value area of the rows on screen"), ("CUM Δ", "session delta: aggressive buys − sells"), ("ST1 · ST2", "dual SuperTrend lines (60 s / 15 s)"),
            ("VWAP", "session VWAP and how many σ price is from it"), ("BID / ASK LIQ", "share of resting size on each side (top 10 levels)"), ("REALIZED", "NinjaTrader realized P&L"), ("TODAY", "journal: trades and net $ today"),
        })
        {
            var (box, v) = Ui.Stat(k, tip == "" ? null : tip);
            stat[k] = v;
            statRow.Children.Add(box);
        }
        var top = new StackPanel();
        top.Children.Add(header);
        top.Children.Add(acctRow);
        top.Children.Add(new Border { BorderBrush = Pal.B(Pal.Line), BorderThickness = new Thickness(0, 0, 0, 1), Child = statRow });
        top.Children.Add(Ui.Pad(bracketLine));

        // Right column (owner's layout, 2026-10-13): TAPE / JOURNAL / LOG view with its toggles, then the
        // ticket settings, then the order buttons in three columns. The ladder runs the full height.
        tapeView = new TapeControl(s, cfg);
        logView = new LogControl(s);
        Control Mini(string text, Action a, string tip)
        {
            var b = Ui.Btn(text, a, tip);
            b.Padding = new Thickness(6, 0);
            b.Height = 18;
            b.FontSize = 10;
            b.Margin = new Thickness(4, 0, 0, 0);
            return b;
        }
        var sideHead = new DockPanel { Background = Pal.B(Pal.Panel), Height = 24 };
        var toggles = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(0, 0, 4, 0) };
        logButtons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal };
        logButtons.Children.Add(Mini("copy", CopyLog, "copy the whole order log with context — paste it in a bug report"));
        logButtons.Children.Add(Mini("clear", () => { s.OrderLog.Clear(); logView.InvalidateVisual(); }, "clear the order log"));
        toggles.Children.Add(logButtons);
        toggles.Children.Add(Mini("journal", () => { cfg.ShowJournal = !cfg.ShowJournal; cfg.ShowLog = false; cfg.Save(); RefreshSide(); }, "trade journal: every round trip with its setup, risk, result and R (click again for the tape)"));
        toggles.Children.Add(Mini("log", () => { cfg.ShowLog = !cfg.ShowLog; cfg.ShowJournal = false; cfg.Save(); RefreshSide(); }, "order log: every click, NinjaTrader's answer, rejections and fills (click again for the tape)"));
        toggles.Children.Add(Mini("ticket", () => { cfg.ShowTicket = !cfg.ShowTicket; cfg.Save(); ApplyTicketRows(); }, "show or hide the ticket settings (hidden = tape and buttons only)"));
        toggles.Children.Add(Mini("heat", () => { cfg.Columns.Heat = !cfg.Columns.Heat; cfg.Save(); }, "heatmap + pull/stack column on the ladder"));
        toggles.Children.Add(Mini("ST", () => { cfg.ShowTrend = !cfg.ShowTrend; cfg.Save(); }, "SuperTrend lines on the ladder"));
        DockPanel.SetDock(toggles, Dock.Right);
        sideHead.Children.Add(toggles);
        sideTitle.Margin = new Thickness(6, 0);
        sideHead.Children.Add(sideTitle);

        var settingsPanel = BuildTicket();
        var buttons = BuildButtons();
        sideGrid = new Grid { Width = 372, Background = Pal.B(Pal.Bg), RowDefinitions = new RowDefinitions("Auto,*,*,Auto") };
        Grid.SetRow(sideHead, 0);
        Grid.SetRow(side, 1);
        Grid.SetRow(settingsPanel, 2);
        Grid.SetRow(buttons, 3);
        sideGrid.Children.Add(sideHead);
        sideGrid.Children.Add(side);
        sideGrid.Children.Add(settingsPanel);
        sideGrid.Children.Add(buttons);
        var sideDock = sideGrid;
        ApplyTicketRows();

        // Ladder | splitter | right column. Drag the splitter to size the right column (saved).
        sideGrid.Width = double.NaN;
        var middle = new Grid { ColumnDefinitions = new ColumnDefinitions($"*,5,{Math.Clamp(cfg.SideWidth, 240, 900).ToString(CultureInfo.InvariantCulture)}") };
        var splitter = new GridSplitter { Background = Pal.B(Pal.Line), ResizeDirection = GridResizeDirection.Columns };
        ToolTip.SetTip(splitter, "drag to size the right column");
        Grid.SetColumn(ladder, 0);
        Grid.SetColumn(splitter, 1);
        Grid.SetColumn(sideDock, 2);
        middle.Children.Add(ladder);
        middle.Children.Add(splitter);
        middle.Children.Add(sideDock);
        splitter.DragCompleted += (_, _) => { cfg.SideWidth = middle.ColumnDefinitions[2].ActualWidth; cfg.Save(); };

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(middle);
        RefreshSide();
        return root;
    }

    /// <summary>The ticket, flight-panel style: a READOUT of the numbers that matter in big type (with the cost /
    /// warning / result lines), then SIZE · STOP · TARGETS · MANAGE · ENTRY cards.</summary>
    private Control BuildTicket()
    {
        foreach (var tb in new[] { sizeIn, slIn, atrIn, swingIn, bufIn, splitIn, beOff, trailR, setupIn, scaleCount, scaleSpan, scaleSkew }.Concat(tpIn))
            tb.TextChanged += (_, _) => SaveFields();
        foreach (var seg in new[] { sizeMode, stopMode, trailBox, previewBox, scaleDir }) seg.SelectionChanged += (_, _) => { SaveFields(); BuildQuick(); };
        foreach (var ck in new[] { beChk, guardChk, scaleChk, bracketBtn }) ck.IsCheckedChanged += (_, _) => SaveFields();

        static Control Row(params Control[] items)
        {
            var p = new WrapPanel { Orientation = Orientation.Horizontal };
            foreach (var c in items) { c.Margin = new Thickness(0, 0, 8, 6); p.Children.Add(c); }
            return p;
        }
        var t = new StackPanel { Spacing = 0, Margin = new Thickness(0, 4, 0, 4) };
        t.Children.Add(Flight.Card("SIZE", new StackPanel { Spacing = 6, Children = { sizeMode, Row(Flight.Labeled("VALUE", sizeIn), quick) } }));
        t.Children.Add(Flight.Card("STOP", new StackPanel { Spacing = 6, Children = {
            stopMode,
            Row(Flight.Labeled("TICKS", slIn), Flight.Labeled("ATR ×", atrIn), Flight.Labeled("MINUTES", swingIn), Flight.Labeled("BUFFER", bufIn), Flight.Labeled(" ", bracketBtn)) } }));
        t.Children.Add(Flight.Card("TARGETS", Row(Flight.Labeled("TP1", tpIn[0]), Flight.Labeled("TP2", tpIn[1]), Flight.Labeled("TP3", tpIn[2]), Flight.Labeled("SPLIT %", splitIn))));
        t.Children.Add(Flight.Card("MANAGE", new StackPanel { Spacing = 6, Children = {
            Row(beChk, Flight.Labeled("OFFSET", beOff), guardChk),
            Flight.Labeled("TRAIL", trailBox),
            Row(Flight.Labeled("START AFTER R", trailR)) } }));
        t.Children.Add(Flight.Card("ENTRY", new StackPanel { Spacing = 6, Children = {
            Row(Flight.Labeled("SETUP", setupIn), Flight.Labeled(" ", armBtn)),
            Flight.Labeled("PREVIEW", previewBox),
            Row(scaleChk, Flight.Labeled("ORDERS", scaleCount), Flight.Labeled("SPAN", scaleSpan), Flight.Labeled("SKEW", scaleSkew)),
            scaleDir } }));

        // Readout: the numbers that matter, big.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        var tiles = new[] { ("CONTRACTS", "ct"), ("RISK", "risk"), ("WIN @TP1", "win"), ("R:R", "rr"), ("OF ROOM", "room"), ("LOSERS LEFT", "losers") };
        for (var i = 0; i < tiles.Length; i++)
        {
            var v = new TextBlock { FontFamily = Pal.Mono, FontSize = 18, FontWeight = FontWeight.SemiBold, Foreground = Pal.B(Pal.Text), Text = "—" };
            readout[tiles[i].Item2] = v;
            var tile = new StackPanel { Margin = new Thickness(0, 2, 0, 4), Children = { new TextBlock { Text = tiles[i].Item1, FontFamily = Flight.Sans, FontSize = 9.5, LetterSpacing = 1, Foreground = Pal.B(Pal.Muted) }, v } };
            Grid.SetColumn(tile, i % 3);
            Grid.SetRow(tile, i / 3);
            grid.Children.Add(tile);
        }
        var lines = new StackPanel { Spacing = 3 };
        lines.Children.Add(grid);
        foreach (var line in new[] { riskLine, checkLine, costLine, lockLine, notice, resultBar })
        {
            line.FontFamily = Flight.Sans;
            line.FontSize = 11.5;
            lines.Children.Add(line);
        }
        t.Children.Insert(0, Flight.Card("READOUT", lines)); // the numbers first, always in view
        ticketPanel = new ScrollViewer { Content = t, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, IsVisible = cfg.ShowTicket };
        return ticketPanel;
    }

    /// <summary>Ticket shown: tape 40% / ticket 60% of the column (the ticket scrolls inside its share).
    /// Hidden: the tape takes it all, the buttons stay.</summary>
    private void ApplyTicketRows()
    {
        ticketPanel.IsVisible = cfg.ShowTicket;
        sideGrid.RowDefinitions[1].Height = new GridLength(cfg.ShowTicket ? 0.8 : 1, GridUnitType.Star);
        sideGrid.RowDefinitions[2].Height = cfg.ShowTicket ? new GridLength(1.2, GridUnitType.Star) : new GridLength(0);
    }

    /// <summary>Order buttons in three columns: BUY MKT · SELL MKT · FLATTEN, then REVERSE · CANCEL ALL, then
    /// B/E · CLOSE HALF (the two-button rows split the width evenly).</summary>
    private Control BuildButtons()
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), Margin = new Thickness(4, 4, 4, 6) };
        void Add(Control b, int row, int col, int span)
        {
            Grid.SetRow(b, row);
            Grid.SetColumn(b, col);
            Grid.SetColumnSpan(b, span);
            g.Children.Add(b);
        }
        var dark = Color.Parse("#1a2130");
        Add(Ui.BigBtn("BUY MKT", Pal.BuyBtn, Pal.Bid, () => Market(true), "market buy with your size and bracket (B when hotkeys are armed)"), 0, 0, 2);
        Add(Ui.BigBtn("SELL MKT", Pal.SellBtn, Pal.Ask, () => Market(false), "market sell with your size and bracket (S when armed)"), 0, 2, 2);
        Add(Ui.BigBtn("FLATTEN", Color.Parse("#4a1414"), Pal.Neg, Flatten, "close the position and cancel its orders (F). Never blocked."), 0, 4, 2);
        Add(Ui.BigBtn("REVERSE", dark, Pal.Text, Reverse, "flip the position at market (R when armed)"), 1, 0, 3);
        Add(Ui.BigBtn("CANCEL ALL", dark, Pal.Text, CancelAll, "cancel every order on this instrument (Esc). Never blocked."), 1, 3, 3);
        Add(Ui.BigBtn("B/E", dark, Pal.Text, BreakEven, "move your stops to entry ± B/E offset (E when armed)"), 2, 0, 3);
        Add(Ui.BigBtn("CLOSE HALF", dark, Pal.Text, CloseHalf, "close half the position at market; bracket legs trimmed. Never blocked."), 2, 3, 3);
        return new Border { BorderBrush = Pal.B(Pal.Line), BorderThickness = new Thickness(0, 1, 0, 0), Background = Pal.B(Pal.Panel), Child = g };
    }

    /// <summary>Quick size chips for the size mode: contracts, $ risk or % of room.</summary>
    private void BuildQuick()
    {
        quick.Children.Clear();
        var (vals, fmt) = cfg.SizeMode switch
        {
            SizeMode.Contracts => (new double[] { 1, 2, 3, 5, 10 }, "{0}"),
            SizeMode.RiskUsd => (new double[] { 50, 100, 250, 500 }, "${0}"),
            _ => (new double[] { 10, 25, 50, 100 }, "{0}%"),
        };
        foreach (var v in vals)
        {
            var b = new Button
            {
                Content = string.Format(CultureInfo.InvariantCulture, fmt, v), FontFamily = Pal.Mono, FontSize = 13, Height = 30, MinWidth = 40, Padding = new Thickness(8, 0),
                Margin = new Thickness(0, 14, 4, 0), CornerRadius = new CornerRadius(4), Background = Pal.B(Flight.Field), BorderBrush = Pal.B(Flight.CardEdge), BorderThickness = new Thickness(1),
                Foreground = Pal.B(Ui.Num(sizeIn, -1) == v ? Flight.Accent : Pal.Text), HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center,
            };
            b.Click += (_, _) => { sizeIn.Text = Ui.F(v); SaveFields(); BuildQuick(); };
            quick.Children.Add(b);
        }
        // Show only the fields the chosen modes use (each sits in its labelled wrapper).
        static void Show(Control c, bool v, int up = 1)
        {
            Control? x = c;
            for (var i = 0; i < up && x != null; i++) x = x.Parent as Control;
            if (x != null) x.IsVisible = v;
        }
        Show(atrIn, cfg.StopMode == StopMode.Atr);
        Show(swingIn, cfg.StopMode == StopMode.Swing);
        Show(bufIn, cfg.StopMode == StopMode.Swing);
        Show(slIn, cfg.StopMode == StopMode.Ticks);
        foreach (var x in new[] { scaleCount, scaleSpan, scaleSkew }) Show(x, cfg.ScaleOn);
        scaleDir.IsVisible = cfg.ScaleOn;
        Show(trailR, cfg.Trail == TrailMode.R, 2);
        Show(beOff, cfg.BeAfterTp1);
    }

    private void LoadFields()
    {
        loading = true;
        instBox.Text = cfg.Instrument;
        sizeMode.SelectedIndex = (int)cfg.SizeMode;
        sizeIn.Text = Ui.F(cfg.SizeInput);
        stopMode.SelectedIndex = (int)cfg.StopMode;
        slIn.Text = cfg.SlTicks.ToString(CultureInfo.InvariantCulture);
        atrIn.Text = Ui.F(cfg.AtrMult);
        swingIn.Text = cfg.SwingMinutes.ToString(CultureInfo.InvariantCulture);
        bufIn.Text = cfg.SwingBufTicks.ToString(CultureInfo.InvariantCulture);
        for (var i = 0; i < 3; i++) tpIn[i].Text = cfg.StopMode == StopMode.Ticks ? (i < cfg.TpTicks.Length ? cfg.TpTicks[i] : 0).ToString(CultureInfo.InvariantCulture) : Ui.F(i < cfg.TpR.Length ? cfg.TpR[i] : 0);
        splitIn.Text = cfg.TpSplit;
        bracketBtn.IsChecked = cfg.BracketOn;
        beChk.IsChecked = cfg.BeAfterTp1;
        beOff.Text = cfg.BeOffsetTicks.ToString(CultureInfo.InvariantCulture);
        trailBox.SelectedIndex = (int)cfg.Trail;
        trailR.Text = Ui.F(cfg.TrailStartR);
        guardChk.IsChecked = cfg.StopGuard;
        setupIn.Text = cfg.Setup;
        previewBox.SelectedIndex = (int)cfg.Preview;
        scaleChk.IsChecked = cfg.ScaleOn;
        scaleCount.Text = cfg.ScaleCount.ToString(CultureInfo.InvariantCulture);
        scaleSpan.Text = cfg.ScaleSpanTicks.ToString(CultureInfo.InvariantCulture);
        scaleSkew.Text = Ui.F(cfg.ScaleSkew);
        scaleDir.SelectedIndex = (int)cfg.ScaleDir;
        armBtn.IsChecked = cfg.HotkeysArmed;
        loading = false;
        BuildQuick();
    }

    private void SaveFields()
    {
        if (loading) return;
        var prevStop = cfg.StopMode;
        cfg.SizeMode = (SizeMode)Math.Max(0, sizeMode.SelectedIndex);
        cfg.SizeInput = Ui.Num(sizeIn, cfg.SizeInput);
        cfg.StopMode = (StopMode)Math.Max(0, stopMode.SelectedIndex);
        cfg.SlTicks = (int)Ui.Num(slIn, cfg.SlTicks);
        cfg.AtrMult = Ui.Num(atrIn, cfg.AtrMult);
        cfg.SwingMinutes = Math.Max(1, (int)Ui.Num(swingIn, cfg.SwingMinutes));
        cfg.SwingBufTicks = (int)Ui.Num(bufIn, cfg.SwingBufTicks);
        if (prevStop == cfg.StopMode)
        {
            if (cfg.StopMode == StopMode.Ticks) cfg.TpTicks = tpIn.Select((x, i) => (int)Ui.Num(x, i < cfg.TpTicks.Length ? cfg.TpTicks[i] : 0)).ToArray();
            else cfg.TpR = tpIn.Select((x, i) => Ui.Num(x, i < cfg.TpR.Length ? cfg.TpR[i] : 0)).ToArray();
        }
        cfg.TpSplit = splitIn.Text ?? cfg.TpSplit;
        cfg.BracketOn = bracketBtn.IsChecked == true;
        cfg.BeAfterTp1 = beChk.IsChecked == true;
        cfg.BeOffsetTicks = (int)Ui.Num(beOff, cfg.BeOffsetTicks);
        cfg.Trail = (TrailMode)Math.Max(0, trailBox.SelectedIndex);
        cfg.TrailStartR = Ui.Num(trailR, cfg.TrailStartR);
        cfg.StopGuard = guardChk.IsChecked == true;
        cfg.Setup = setupIn.Text ?? "";
        cfg.Preview = (PreviewMode)Math.Max(0, previewBox.SelectedIndex);
        cfg.ScaleOn = scaleChk.IsChecked == true;
        cfg.ScaleCount = (int)Ui.Num(scaleCount, cfg.ScaleCount);
        cfg.ScaleSpanTicks = (int)Ui.Num(scaleSpan, cfg.ScaleSpanTicks);
        cfg.ScaleSkew = Ui.Num(scaleSkew, cfg.ScaleSkew);
        cfg.ScaleDir = (SkewDir)Math.Max(0, scaleDir.SelectedIndex);
        cfg.Save();
        if (prevStop != cfg.StopMode) LoadFields(); // targets switch between ticks and R
        else BuildQuick();
    }

    private void OnSettingsSaved()
    {
        dd = NewTracker();
        s.ResetAnalytics();
        bridge.Send(Protocol.History(s.Instrument, cfg.HistoryHours));
        LoadFields();
    }

    private void SwitchInstrument()
    {
        var name = instBox.Text?.Trim() ?? "";
        if (name.Length == 0) return;
        cfg.Instrument = name;
        cfg.Save();
        bridge.Send(Protocol.Subscribe(name));
        bridge.Send(Protocol.History(name, cfg.HistoryHours));
        trail.Clear();
        be.Clear();
        ladder.Recenter();
    }

    /// <summary>RISK → Use in DOM: size rule, stop and target into the ticket.</summary>
    private void ApplyRiskPlan(SizeMode mode, double input, int stopTicks, int tpTicks)
    {
        cfg.SizeMode = mode;
        cfg.SizeInput = input;
        cfg.StopMode = StopMode.Ticks;
        cfg.SlTicks = stopTicks;
        cfg.TpTicks = new[] { tpTicks, 0, 0 };
        cfg.Save();
        LoadFields();
        Notice($"RISK plan in the ticket: {sizeMode.SelectedItem} {Ui.F(input)}, SL {stopTicks}t, TP {tpTicks}t");
    }

    private void Notice(string text, int ms = 6000)
    {
        notice.Text = text;
        noticeUntil = Environment.TickCount64 + ms;
    }

    private string Px(double price) => price.ToString("F" + LadderControl.Decimals(s.TickSize), CultureInfo.InvariantCulture);
    private static string Leg(WorkingOrder o) => $"{(o.Name.StartsWith("BDIQ", StringComparison.Ordinal) ? o.Name[5..] : o.Type)} {o.Action} {o.Left}";
}
