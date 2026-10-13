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

/// <summary>The DOM window: header, account + position strips, ladder with tape and order log, and
/// the ticket with CME risk (contracts, ticks, $ per tick, fees). Built in code so every control
/// is checked by the compiler.</summary>
public sealed class MainWindow : Window
{
    private readonly DomState s = new();
    private readonly Settings cfg = Settings.Load();
    private readonly BridgeClient bridge;
    private readonly LadderControl ladder;
    private DrawdownTracker dd;

    private readonly TextBox instBox = Input(130);
    private readonly ComboBox acctBox = new() { Width = 150, Height = 24, FontSize = 12 };
    private readonly TextBlock connText = Label("", Pal.Muted);
    private readonly TextBlock acctLine = Label("", Pal.Muted);
    private readonly TextBlock posLine = Label("", Pal.Text);
    private readonly TextBlock riskLine = Label("", Pal.Text);
    private readonly TextBlock resultBar = Label("", Pal.Muted);
    private readonly StackPanel tape = new() { Spacing = 0 };
    private readonly StackPanel log = new() { Spacing = 0 };
    private readonly ComboBox sizeMode = new() { Width = 110, Height = 24, FontSize = 12, ItemsSource = new[] { "contracts", "$ risk @SL", "% room @SL" } };
    private readonly TextBox sizeIn = Input(54), tpIn = Input(44), slIn = Input(44), feeIn = Input(50), maxIn = Input(40);
    private readonly TextBox startIn = Input(70), ddIn = Input(60), lockIn = Input(70), dayIn = Input(60);
    private readonly ComboBox ddKind = new() { Width = 130, Height = 24, FontSize = 12, ItemsSource = new[] { "static", "trailing intraday", "trailing EOD" } };
    private readonly ToggleButton bracketBtn = new() { Content = "BRACKET ON", FontSize = 11 };
    private int cidSeq;
    private bool loading;

    public MainWindow()
    {
        Title = "BookDepthIQ DOM — NinjaTrader";
        Width = 1100;
        Height = 820;
        Background = Pal.B(Pal.Bg);
        FontFamily = Pal.Mono;
        FontSize = 12;

        dd = new DrawdownTracker(cfg.Rules, cfg.Peaks.TryGetValue(cfg.Account, out var pk) ? pk : null);
        bridge = new BridgeClient(s);
        ladder = new LadderControl(s);
        ladder.PlaceRequested += (buy, tick, stop) => Place(buy, tick, stop);
        ladder.CancelRequested += o => SendCmd($"CANCEL {o.Action} {o.Type} @ {Px(o.Price)}", cid => Protocol.Cancel(cid, s.Account, o.Id));
        ladder.MoveRequested += (o, t) => SendCmd($"MOVE {o.Action} {o.Type} → {Px(t * s.TickSize)}", cid => Protocol.Move(cid, s.Account, o.Id, t * s.TickSize));

        Content = Build();
        LoadFields();
        bridge.Connected += () =>
        {
            bridge.Send(Protocol.SelectAccount(cfg.Account));
            bridge.Send(Protocol.Subscribe(cfg.Instrument));
        };
        s.Account = cfg.Account;
        s.Changed += OnChanged;
        bridge.Start();
        // Repaint the ladder at most ~20×/s; market messages only mark it dirty.
        DispatcherTimer.Run(() => { ladder.InvalidateVisual(); return true; }, TimeSpan.FromMilliseconds(50));
        KeyDown += OnKey;
        // Dev only: BDIQ_SNAPSHOT=<file.png> saves a picture of the window after 4 s and exits.
        if (Environment.GetEnvironmentVariable("BDIQ_SNAPSHOT") is { Length: > 0 } shot)
            DispatcherTimer.RunOnce(() =>
            {
                var size = new PixelSize((int)Bounds.Width, (int)Bounds.Height);
                using var bmp = new Avalonia.Media.Imaging.RenderTargetBitmap(size);
                bmp.Render(this);
                bmp.Save(shot);
                Close();
            }, TimeSpan.FromSeconds(4));
        Closed += (_, _) => { SaveFields(); bridge.Dispose(); };
    }

    // ───────────── layout ─────────────

    private Control Build()
    {
        var header = Bar(
            Label("BookDepthIQ", Pal.Text, bold: true), instBox, Btn("Go", (_, _) => { cfg.Instrument = instBox.Text?.Trim() ?? ""; cfg.Save(); bridge.Send(Protocol.Subscribe(cfg.Instrument)); ladder.Recenter(); }),
            Label("account", Pal.Muted), acctBox, connText);
        acctBox.SelectionChanged += (_, _) =>
        {
            if (acctBox.SelectedItem is not string a || a == cfg.Account) return;
            cfg.Account = a; s.Account = a; cfg.Save();
            dd = new DrawdownTracker(cfg.Rules, cfg.Peaks.TryGetValue(a, out var pk) ? pk : null);
            bridge.Send(Protocol.SelectAccount(a));
        };

        var rules = new Expander
        {
            Header = Label("account rules (yours): start · max drawdown · trailing · lock · daily loss · fees", Pal.Muted),
            Content = Bar(
                Label("start $", Pal.Muted), startIn, Label("max DD $", Pal.Muted), ddIn, ddKind, Label("stops trailing at $", Pal.Muted), lockIn,
                Label("daily loss $", Pal.Muted), dayIn, Label("fee/side $", Pal.Muted), feeIn, Label("max ct", Pal.Muted), maxIn,
                Btn("save", (_, _) => { SaveFields(); dd = new DrawdownTracker(cfg.Rules, cfg.Peaks.TryGetValue(cfg.Account, out var pk) ? pk : null); OnChanged(); })),
            Padding = new Thickness(4),
        };

        var side = new DockPanel { Width = 270, Background = Pal.B(Pal.Bg) };
        var tapeHead = Label("TAPE", Pal.Muted, size: 10);
        var logHead = Label("ORDER LOG", Pal.Muted, size: 10);
        var logScroll = new ScrollViewer { Content = log, Height = 220 };
        DockPanel.SetDock(tapeHead, Dock.Top);
        DockPanel.SetDock(logScroll, Dock.Bottom);
        DockPanel.SetDock(logHead, Dock.Bottom);
        side.Children.Add(tapeHead);
        side.Children.Add(logScroll);
        side.Children.Add(logHead);
        side.Children.Add(new ScrollViewer { Content = tape });

        var middle = new DockPanel();
        DockPanel.SetDock(side, Dock.Right);
        middle.Children.Add(side);
        middle.Children.Add(new Border { BorderBrush = Pal.B(Pal.Line), BorderThickness = new Thickness(0, 0, 1, 0), Child = ladder });

        bracketBtn.IsCheckedChanged += (_, _) => { bracketBtn.Content = bracketBtn.IsChecked == true ? "BRACKET ON" : "BRACKET OFF"; SaveFields(); };
        foreach (var tb in new[] { sizeIn, tpIn, slIn }) tb.TextChanged += (_, _) => { SaveFields(); UpdateRiskLine(); };
        sizeMode.SelectionChanged += (_, _) => { SaveFields(); UpdateRiskLine(); };

        var ticket = new StackPanel { Background = Pal.B(Pal.Panel), Spacing = 4, Margin = new Thickness(0) };
        ticket.Children.Add(Bar(Label("Size", Pal.Muted), sizeMode, sizeIn, Label("TP ticks", Pal.Muted), tpIn, Label("SL ticks", Pal.Muted), slIn, bracketBtn));
        ticket.Children.Add(Pad(riskLine));
        ticket.Children.Add(Pad(resultBar));
        var buttons = new UniformGrid { Columns = 4, Margin = new Thickness(6, 2, 6, 6) };
        buttons.Children.Add(BigBtn("BUY MKT", Pal.BuyBtn, Pal.Bid, (_, _) => Market(true)));
        buttons.Children.Add(BigBtn("SELL MKT", Pal.SellBtn, Pal.Ask, (_, _) => Market(false)));
        buttons.Children.Add(BigBtn("CANCEL ALL", Color.Parse("#1a2130"), Pal.Text, (_, _) => SendCmd("CANCEL ALL", cid => Protocol.CancelAll(cid, s.Account, s.Instrument))));
        buttons.Children.Add(BigBtn("FLATTEN", Color.Parse("#4a1414"), Pal.Neg, (_, _) => SendCmd("FLATTEN", cid => Protocol.Flatten(cid, s.Account, s.Instrument))));
        ticket.Children.Add(buttons);

        var root = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(header);
        top.Children.Add(rules);
        top.Children.Add(Pad(acctLine));
        top.Children.Add(Pad(posLine));
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(ticket, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(ticket);
        root.Children.Add(middle);
        return root;
    }

    private void LoadFields()
    {
        loading = true;
        instBox.Text = cfg.Instrument;
        sizeMode.SelectedIndex = (int)cfg.SizeMode;
        sizeIn.Text = F(cfg.SizeInput);
        tpIn.Text = cfg.TpTicks.ToString(CultureInfo.InvariantCulture);
        slIn.Text = cfg.SlTicks.ToString(CultureInfo.InvariantCulture);
        bracketBtn.IsChecked = cfg.BracketOn;
        feeIn.Text = F(cfg.FeePerSide);
        maxIn.Text = cfg.MaxContracts.ToString(CultureInfo.InvariantCulture);
        startIn.Text = F(cfg.Start);
        ddIn.Text = F(cfg.MaxDrawdown);
        ddKind.SelectedIndex = (int)cfg.DrawdownKind;
        lockIn.Text = F(cfg.LockAt);
        dayIn.Text = F(cfg.DailyLoss);
        loading = false;
    }

    private void SaveFields()
    {
        if (loading) return;
        cfg.SizeMode = (SizeMode)Math.Max(0, sizeMode.SelectedIndex);
        cfg.SizeInput = Num(sizeIn, cfg.SizeInput);
        cfg.TpTicks = (int)Num(tpIn, cfg.TpTicks);
        cfg.SlTicks = (int)Num(slIn, cfg.SlTicks);
        cfg.BracketOn = bracketBtn.IsChecked == true;
        cfg.FeePerSide = Num(feeIn, cfg.FeePerSide);
        cfg.MaxContracts = (int)Num(maxIn, cfg.MaxContracts);
        cfg.Start = Num(startIn, cfg.Start);
        cfg.MaxDrawdown = Num(ddIn, cfg.MaxDrawdown);
        cfg.DrawdownKind = (DrawdownKind)Math.Max(0, ddKind.SelectedIndex);
        cfg.LockAt = Num(lockIn, cfg.LockAt);
        cfg.DailyLoss = Num(dayIn, cfg.DailyLoss);
        cfg.Save();
    }

    // ───────────── risk ─────────────

    private double Equity => s.NetLiq > 0 ? s.NetLiq : s.Cash + s.OpenPnl;

    private RiskOut Risk() => CmeRisk.Calc(new RiskInput
    {
        TickValue = s.TickValue,
        RoomUsd = dd.Room(Equity),
        StopTicks = cfg.SlTicks,
        TargetTicks = cfg.TpTicks,
        FeePerSide = cfg.FeePerSide,
        Mode = cfg.SizeMode,
        Input = cfg.SizeInput,
        MaxContracts = cfg.MaxContracts,
    });

    private void UpdateRiskLine()
    {
        var r = Risk();
        var be = CmeRisk.BreakEvenTicks(s.TickValue, cfg.FeePerSide);
        var why = r.BelowOne ? $" · ${r.RiskWanted:N0} buys less than 1 contract at {cfg.SlTicks}t" : r.Capped ? $" · capped at your max {cfg.MaxContracts}" : "";
        riskLine.Text = $"= {r.Contracts} ct · ${r.DollarsPerTick:N2}/tick · risk ${r.Loss:N2} at SL · win ${r.Win:N2} at TP · R:R {r.RR:N2} · fees ${r.Fees:N2} · " +
                        $"{(double.IsInfinity(r.RoomUsed) ? "—" : (r.RoomUsed * 100).ToString("N0", CultureInfo.InvariantCulture) + "% of room")} · {(r.LosersLeft == int.MaxValue ? "—" : r.LosersLeft.ToString(CultureInfo.InvariantCulture))} losers left · break-even {be:N1}t{why}";
        riskLine.Foreground = Pal.B(r.StopPastFail ? Pal.Neg : Pal.Text);
        if (r.StopPastFail) riskLine.Text += $" · STOP IS PAST THE FAIL LINE ({r.TicksToFail:N0}t)";
    }

    // ───────────── orders (each one is a click) ─────────────

    private string NextCid() => $"c{Environment.TickCount64}-{++cidSeq}";

    /// <summary>Sends a command, or says on screen why it can't. Never a silent no-op.</summary>
    private void SendCmd(string what, Func<string, string> build)
    {
        s.Note('→', $"{s.Account} {what} · {s.Instrument}");
        if (!s.Connected) { Refuse("not connected to NinjaTrader — is the BookDepthIQBridge AddOn compiled and NT running?"); return; }
        if (!bridge.Send(build(NextCid()))) Refuse("the bridge connection dropped — reconnecting");
        OnChanged();
    }

    private void Refuse(string why)
    {
        s.LastOk = false;
        s.LastResult = why;
        s.Note('⊘', why);
        OnChanged();
    }

    /// <summary>Why an entry can't go right now (null = ready).</summary>
    private string? NotReady(int qty)
    {
        if (!s.Connected) return "not connected to NinjaTrader";
        if (string.IsNullOrEmpty(s.Instrument)) return "no instrument — type one (e.g. NQ 12-26) and press Go";
        if (qty <= 0)
        {
            var r = Risk();
            return r.BelowOne ? $"size is 0: ${r.RiskWanted:N0} risk buys less than one contract at {cfg.SlTicks} ticks" : $"size is 0 — check Size ({sizeMode.SelectedItem} {cfg.SizeInput})";
        }
        return null;
    }

    private void Market(bool buy)
    {
        var qty = Risk().Contracts;
        var why = NotReady(qty);
        if (why != null) { s.Note('→', $"{(buy ? "BUY" : "SELL")} MKT"); Refuse(why); return; }
        var (tp, sl) = cfg.BracketOn ? (cfg.TpTicks, cfg.SlTicks) : (0, 0);
        SendCmd($"{(buy ? "BUY" : "SELL")} MKT {qty} ct{(cfg.BracketOn ? $" TP {tp}t SL {sl}t" : "")}", cid => Protocol.Order(cid, s.Account, s.Instrument, buy ? "buy" : "sell", "market", qty, null, tp, sl));
    }

    private void Place(bool buy, long tick, bool stop)
    {
        var qty = Risk().Contracts;
        var why = NotReady(qty);
        var px = tick * s.TickSize;
        if (why != null) { s.Note('→', $"{(buy ? "BUY" : "SELL")} {(stop ? "STOP" : "LIMIT")} @ {Px(px)}"); Refuse(why); return; }
        var (tp, sl) = cfg.BracketOn ? (cfg.TpTicks, cfg.SlTicks) : (0, 0);
        SendCmd($"{(buy ? "BUY" : "SELL")} {(stop ? "STOP" : "LIMIT")} {qty} ct @ {Px(px)}{(cfg.BracketOn ? $" TP {tp}t SL {sl}t" : "")}",
            cid => Protocol.Order(cid, s.Account, s.Instrument, buy ? "buy" : "sell", stop ? "stop" : "limit", qty, px, tp, sl));
    }

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.Source is TextBox) return;
        if (e.Key == Key.Space) { ladder.Recenter(); e.Handled = true; }
        else if (e.Key == Key.F) { SendCmd("FLATTEN (F)", cid => Protocol.Flatten(cid, s.Account, s.Instrument)); e.Handled = true; }
        else if (e.Key == Key.Escape) { SendCmd("CANCEL ALL (Esc)", cid => Protocol.CancelAll(cid, s.Account, s.Instrument)); e.Handled = true; }
    }

    // ───────────── refresh ─────────────

    private void OnChanged()
    {
        connText.Text = s.Connected ? "● NinjaTrader bridge" : "○ waiting for the NinjaTrader bridge (127.0.0.1:" + Protocol.Port + ")";
        connText.Foreground = Pal.B(s.Connected ? Pal.Pos : Pal.Warn);
        var names = s.Accounts.Select(a => a.Name).ToList();
        if (!names.SequenceEqual(acctBox.ItemsSource?.Cast<string>() ?? Enumerable.Empty<string>()))
        {
            acctBox.ItemsSource = names;
            acctBox.SelectedItem = names.Contains(cfg.Account) ? cfg.Account : null;
        }

        var eq = Equity;
        dd.OnEquity(eq);
        if (dd.Peak != (cfg.Peaks.TryGetValue(cfg.Account, out var p) ? p : cfg.Start)) { cfg.Peaks[cfg.Account] = dd.Peak; cfg.Save(); }
        var room = dd.Room(eq);
        acctLine.Text = $"{s.Account} · equity ${eq:N2} · floor ${dd.Floor:N2} ({cfg.DrawdownKind}) · room ${room:N2} · realized {LadderControl.Usd(s.Realized)} · " +
                        $"{s.Instrument} tick {s.TickSize} = ${s.TickValue:N2}{(s.TrailDd != 0 ? $" · NT trailing DD ${s.TrailDd:N2}" : "")}";
        var ticksToFloor = s.PosQty != 0 && s.TickValue > 0 ? room / (Math.Abs(s.PosQty) * s.TickValue) : double.PositiveInfinity;
        posLine.Text = s.PosQty == 0
            ? "POSITION flat"
            : $"POSITION {(s.PosQty > 0 ? "+" : "")}{s.PosQty} @ {Px(s.PosAvg)} · OPEN P&L {LadderControl.Usd(s.OpenPnl)} · TO FLOOR {ticksToFloor:N0} ticks";
        posLine.Foreground = Pal.B(s.PosQty == 0 ? Pal.Muted : s.OpenPnl >= 0 ? Pal.Pos : Pal.Neg);
        resultBar.Text = s.LastResult is null ? "" : (s.LastOk ? "✓ " : "✗ ") + s.LastResult;
        resultBar.Foreground = Pal.B(s.LastOk ? Pal.Pos : Pal.Neg);
        UpdateRiskLine();

        tape.Children.Clear();
        foreach (var t in s.Tape.Take(60))
            tape.Children.Add(Label($"{DateTimeOffset.FromUnixTimeMilliseconds(t.TimeMs).LocalDateTime:HH:mm:ss}  {Px(t.Tick * s.TickSize),10}  {t.Size,5}", t.Side == 'b' ? Pal.Bid : t.Side == 's' ? Pal.Ask : Pal.Muted, size: 11));
        log.Children.Clear();
        foreach (var l in Enumerable.Reverse(s.Log).Take(60))
            log.Children.Add(Label($"{l.Time:HH:mm:ss} {l.Mark} {l.Text}", l.Mark == '✓' ? Pal.Pos : l.Mark is '✗' or '⊘' ? Pal.Neg : l.Mark == '→' ? Pal.Text : Pal.Muted, size: 11, wrap: true));
    }

    // ───────────── helpers ─────────────

    private string Px(double price) => price.ToString("F" + LadderControl.Decimals(s.TickSize), CultureInfo.InvariantCulture);
    private static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    private static double Num(TextBox t, double dflt) => double.TryParse(t.Text?.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : dflt;

    private static TextBox Input(double w) => new() { Width = w, Height = 24, MinHeight = 24, FontSize = 12, Padding = new Thickness(4, 2), Background = Pal.B(Pal.Bg), BorderBrush = Pal.B(Pal.Line) };
    private static TextBlock Label(string text, Color c, bool bold = false, double size = 12, bool wrap = false) =>
        new() { Text = text, Foreground = Pal.B(c), FontWeight = bold ? FontWeight.Bold : FontWeight.Normal, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };
    private static Control Pad(Control c) => new Border { Padding = new Thickness(8, 2), Child = c };
    private static Button Btn(string text, EventHandler<Avalonia.Interactivity.RoutedEventArgs> click)
    {
        var b = new Button { Content = text, FontSize = 11, Padding = new Thickness(8, 2), Height = 24 };
        b.Click += click;
        return b;
    }
    private static Button BigBtn(string text, Color bg, Color fg, EventHandler<Avalonia.Interactivity.RoutedEventArgs> click)
    {
        var b = new Button
        {
            Content = text, Background = Pal.B(bg), Foreground = Pal.B(fg), FontWeight = FontWeight.Bold, Height = 38,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2),
        };
        b.Click += click;
        return b;
    }
    private static Control Bar(params Control[] items)
    {
        var p = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 4) };
        foreach (var c in items) { c.Margin = new Thickness(0, 0, 8, 0); p.Children.Add(c); }
        return new Border { Background = Pal.B(Pal.Panel), BorderBrush = Pal.B(Pal.Line), BorderThickness = new Thickness(0, 0, 0, 1), Child = p };
    }
}
