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
    private readonly ToggleButton armBtn = new() { Content = "HOTKEYS SAFE", FontSize = 11, Height = 24 };

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
    private readonly StackPanel journalList = new();
    private readonly ContentControl side = new();
    private ScrollViewer? journalScroll;
    private readonly TextBlock journalSum = Ui.Label("", Pal.Text, size: 11.5, wrap: true);

    // ticket
    private readonly ComboBox sizeMode = Ui.Combo(130, new[] { "contracts", "$ risk @SL", "% room @SL" }, "how size is set: a fixed number of contracts, $ lost at the stop (fees in), or % of the room left to your fail floor");
    private readonly TextBox sizeIn = Ui.Input(52);
    private readonly StackPanel quick = new() { Orientation = Orientation.Horizontal, Spacing = 3 };
    private readonly ComboBox stopMode = Ui.Combo(112, new[] { "SL ticks", "SL ATR", "SL swing" }, "stop distance: your ticks, ST1 ATR × mult, or past the swing high/low");
    private readonly TextBox slIn = Ui.Input(44, "stop ticks"), atrIn = Ui.Input(40, "ATR multiple"), swingIn = Ui.Input(36, "swing lookback, minutes"), bufIn = Ui.Input(32, "ticks past the swing");
    private readonly TextBox[] tpIn = { Ui.Input(44, "TP1 ticks (R in ATR/swing mode)"), Ui.Input(44, "TP2 (0 = off)"), Ui.Input(44, "TP3 (0 = off)") };
    private readonly TextBox splitIn = Ui.Input(76, "size split across the targets that are on, e.g. 50/30/20");
    private readonly ToggleButton bracketBtn = new() { Content = "BRACKET ON", FontSize = 11, Height = 24 };
    private readonly CheckBox beChk = Ui.Check("B/E after TP1", "when TP1 fills, the panel moves your stops to entry ± offset (needs 2+ targets). Acts without a click; off by default.");
    private readonly TextBox beOff = Ui.Input(32, "B/E offset ticks (+ in your favour)");
    private readonly ComboBox trailBox = Ui.Combo(118, new[] { "trail off", "trail R", "trail ST1", "trail ST2" }, "trailing stop: after N R, one risk behind the best price; or ON the ST1/ST2 line. Moves only your way; acts without a click.");
    private readonly TextBox trailR = Ui.Input(36, "start trailing after this many R");
    private readonly CheckBox guardChk = Ui.Check("stop guard", "if a stop the panel saw on your position disappears, put it back at its last price");
    private readonly TextBox setupIn = Ui.Input(90, "setup tag saved with the trade in the journal");
    private readonly ComboBox previewBox = Ui.Combo(150, new[] { "preview off", "PREVIEW LONG", "PREVIEW SHORT" }, "draw the would-be stop and targets on the ladder while flat; drag them to set distances. Nothing is sent.");
    private readonly CheckBox scaleChk = Ui.Check("SCALE", "a ladder click sends several limits from that price away from the market (no brackets: press B/E or set a stop after)");
    private readonly TextBox scaleCount = Ui.Input(30, "orders (2–10)"), scaleSpan = Ui.Input(36, "span ticks"), scaleSkew = Ui.Input(30, "size skew (1 = even)");
    private readonly ComboBox scaleDir = Ui.Combo(104, new[] { "big start", "even", "big end" });
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
        ladder.GroupStep += d => { cfg.Group = LadderMath.StepGroup(cfg.Group, d); cfg.Save(); };

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
            ("POC · VA", "session volume profile: point of control and value area"), ("CUM Δ", "session delta: aggressive buys − sells"), ("ST1 · ST2", "dual SuperTrend lines (60 s / 15 s)"),
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

        // side panel, as in the Vest panel: TAPE (or JOURNAL) header with its toggles, the tape, then the
        // ORDER LOG in the bottom 40% with copy / clear.
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
        toggles.Children.Add(Mini("journal", () => { cfg.ShowJournal = !cfg.ShowJournal; cfg.Save(); RefreshSide(); }, "trade journal: every round trip with its setup, risk, result and R"));
        toggles.Children.Add(Mini("heat", () => { cfg.Columns.Heat = !cfg.Columns.Heat; cfg.Save(); }, "heatmap + pull/stack column on the ladder"));
        toggles.Children.Add(Mini("log", () => { cfg.ShowLog = !cfg.ShowLog; cfg.Save(); RefreshSide(); }, "show or hide the order log under the tape"));
        toggles.Children.Add(Mini("ST", () => { cfg.ShowTrend = !cfg.ShowTrend; cfg.Save(); }, "SuperTrend lines on the ladder"));
        DockPanel.SetDock(toggles, Dock.Right);
        sideHead.Children.Add(toggles);
        sideTitle.Margin = new Thickness(6, 0);
        sideHead.Children.Add(sideTitle);
        var logHead = new DockPanel { Background = Pal.B(Pal.Panel), Height = 24 };
        var logBtns = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Margin = new Thickness(0, 0, 4, 0) };
        logBtns.Children.Add(Mini("copy", CopyLog, "copy the whole order log with context — paste it in a bug report"));
        logBtns.Children.Add(Mini("clear", () => { s.OrderLog.Clear(); logView.InvalidateVisual(); }, "clear the order log"));
        DockPanel.SetDock(logBtns, Dock.Right);
        logHead.Children.Add(logBtns);
        logHead.Children.Add(new TextBlock { Text = "ORDER LOG", FontSize = 10, Foreground = Pal.B(Pal.Muted), Margin = new Thickness(6, 0), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
        sideGrid = new Grid { Width = 280, Background = Pal.B(Pal.Bg), RowDefinitions = new RowDefinitions("Auto,*,Auto,0.66*") };
        Grid.SetRow(sideHead, 0);
        Grid.SetRow(side, 1);
        var logHeadBorder = new Border { BorderBrush = Pal.B(Pal.Line), BorderThickness = new Thickness(0, 1, 0, 1), Child = logHead };
        Grid.SetRow(logHeadBorder, 2);
        Grid.SetRow(logView, 3);
        sideGrid.Children.Add(sideHead);
        sideGrid.Children.Add(side);
        sideGrid.Children.Add(logHeadBorder);
        sideGrid.Children.Add(logView);
        var sideDock = sideGrid;

        var middle = new DockPanel();
        var sideBorder = new Border { BorderBrush = Pal.B(Pal.Line), BorderThickness = new Thickness(1, 0, 0, 0), Child = sideDock };
        DockPanel.SetDock(sideBorder, Dock.Right);
        middle.Children.Add(sideBorder);
        middle.Children.Add(ladder);

        var ticket = BuildTicket();
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(ticket, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(ticket);
        root.Children.Add(middle);
        RefreshSide();
        return root;
    }

    private Control BuildTicket()
    {
        bracketBtn.IsCheckedChanged += (_, _) => { bracketBtn.Content = bracketBtn.IsChecked == true ? "BRACKET ON" : "BRACKET OFF"; SaveFields(); };
        foreach (var tb in new[] { sizeIn, slIn, atrIn, swingIn, bufIn, splitIn, beOff, trailR, setupIn, scaleCount, scaleSpan, scaleSkew }.Concat(tpIn))
            tb.TextChanged += (_, _) => SaveFields();
        foreach (var cb in new[] { sizeMode, stopMode, trailBox, previewBox, scaleDir }) cb.SelectionChanged += (_, _) => { SaveFields(); BuildQuick(); };
        foreach (var ck in new[] { beChk, guardChk, scaleChk }) ck.IsCheckedChanged += (_, _) => SaveFields();

        var t = new StackPanel { Background = Pal.B(Pal.Panel) };
        t.Children.Add(Ui.Row(Ui.Label("Size", Pal.Muted), sizeMode, sizeIn, quick, stopMode, slIn, atrIn, swingIn, bufIn,
            Ui.Label("TP", Pal.Muted), tpIn[0], tpIn[1], tpIn[2], Ui.Label("split", Pal.Muted), splitIn, bracketBtn));
        t.Children.Add(Ui.Row(beChk, beOff, trailBox, trailR, guardChk, Ui.Label("setup", Pal.Muted), setupIn, previewBox, scaleChk, scaleCount, scaleSpan, scaleSkew, scaleDir, armBtn));
        t.Children.Add(Ui.Pad(riskLine));
        t.Children.Add(Ui.Pad(checkLine));
        t.Children.Add(Ui.Pad(costLine));
        t.Children.Add(Ui.Pad(lockLine));
        t.Children.Add(Ui.Pad(notice));
        t.Children.Add(Ui.Pad(resultBar));
        var buttons = new UniformGrid { Columns = 7, Margin = new Thickness(6, 2, 6, 6) };
        buttons.Children.Add(Ui.BigBtn("BUY MKT", Pal.BuyBtn, Pal.Bid, () => Market(true), "market buy with your size and bracket (B when hotkeys are armed)"));
        buttons.Children.Add(Ui.BigBtn("SELL MKT", Pal.SellBtn, Pal.Ask, () => Market(false), "market sell with your size and bracket (S when armed)"));
        buttons.Children.Add(Ui.BigBtn("B/E", Color.Parse("#1a2130"), Pal.Text, BreakEven, "move your stops to entry ± B/E offset (E when armed)"));
        buttons.Children.Add(Ui.BigBtn("CLOSE HALF", Color.Parse("#1a2130"), Pal.Text, CloseHalf, "close half the position at market; bracket legs trimmed. Never blocked."));
        buttons.Children.Add(Ui.BigBtn("REVERSE", Color.Parse("#1a2130"), Pal.Text, Reverse, "flip the position at market (R when armed)"));
        buttons.Children.Add(Ui.BigBtn("CANCEL ALL", Color.Parse("#1a2130"), Pal.Text, CancelAll, "cancel every order on this instrument (Esc). Never blocked."));
        buttons.Children.Add(Ui.BigBtn("FLATTEN", Color.Parse("#4a1414"), Pal.Neg, Flatten, "close the position and cancel its orders (F). Never blocked."));
        t.Children.Add(buttons);
        return new Border { BorderBrush = Pal.B(Pal.Line), BorderThickness = new Thickness(0, 1, 0, 0), Child = t };
    }

    /// <summary>Quick size buttons for the size mode: contracts, $ risk or % of room.</summary>
    private void BuildQuick()
    {
        quick.Children.Clear();
        var (vals, fmt) = cfg.SizeMode switch
        {
            SizeMode.Contracts => (new double[] { 1, 2, 3, 5, 10 }, "{0} ct"),
            SizeMode.RiskUsd => (new double[] { 50, 100, 250, 500 }, "${0}"),
            _ => (new double[] { 10, 25, 50, 100 }, "{0}%"),
        };
        foreach (var v in vals)
        {
            var b = Ui.Btn(string.Format(CultureInfo.InvariantCulture, fmt, v), () => { sizeIn.Text = Ui.F(v); SaveFields(); });
            b.Padding = new Thickness(5, 1);
            quick.Children.Add(b);
        }
        atrIn.IsVisible = cfg.StopMode == StopMode.Atr;
        swingIn.IsVisible = bufIn.IsVisible = cfg.StopMode == StopMode.Swing;
        slIn.IsVisible = cfg.StopMode == StopMode.Ticks;
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
        bracketBtn.Content = cfg.BracketOn ? "BRACKET ON" : "BRACKET OFF";
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
