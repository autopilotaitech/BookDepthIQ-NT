using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

/// <summary>The risk calculator in CME terms (port of the Vest panel's RISK page): your account or a plan,
/// a stop and target in ticks, a size rule; every CME contract with $/tick, contracts for that risk, $ lost
/// and won with fees, and ticks to the fail line. "Use in DOM" writes the plan into the ticket. Never sends
/// an order.</summary>
public sealed class RiskWindow : Window
{
    private readonly Settings cfg;
    private readonly DomState s;
    private readonly DrawdownTracker dd;
    private readonly Action<SizeMode, double, int, int> use;
    private readonly ComboBox acct = Ui.Combo(150, new[] { "my account", "custom plan" });
    private readonly TextBox bal = Ui.Input(90), room = Ui.Input(80), stop = Ui.Input(50), tp = Ui.Input(50), size = Ui.Input(60), fee = Ui.Input(50);
    private readonly ComboBox mode = Ui.Combo(130, new[] { "contracts", "$ risk @SL", "% room @SL" });
    private readonly StackPanel table = new();
    private readonly TextBlock map = Ui.Label("", Pal.Text, size: 12, wrap: true);
    private readonly TextBlock head = Ui.Label("", Pal.Muted, size: 11.5, wrap: true);
    private bool loading;

    public RiskWindow(Settings cfg, DomState s, DrawdownTracker dd, Action<SizeMode, double, int, int> use)
    {
        this.cfg = cfg;
        this.s = s;
        this.dd = dd;
        this.use = use;
        Title = "BookDepthIQ — RISK";
        Width = 980;
        Height = 640;
        Background = Pal.B(Pal.Bg);
        FontFamily = Pal.Mono;
        FontSize = 12;

        Reset();
        foreach (var t in new[] { bal, room, stop, tp, size, fee }) t.TextChanged += (_, _) => Recalc();
        mode.SelectionChanged += (_, _) => Recalc();
        acct.SelectionChanged += (_, _) => { if (loading) return; var mine = acct.SelectedIndex == 0; bal.IsEnabled = room.IsEnabled = !mine; Recalc(); };

        var top = new StackPanel();
        top.Children.Add(Ui.Bar(Ui.Label("RISK", Pal.Text, bold: true), head,
            Ui.Btn("reset", Reset, "back to the defaults: your account, the ticket's stop/target/size, your fee"),
            Ui.Btn("Use in DOM", () => { use((SizeMode)Math.Max(0, mode.SelectedIndex), Ui.Num(size, 1), (int)Ui.Num(stop, 16), (int)Ui.Num(tp, 32)); Close(); }, "writes this size rule, stop and target into the DOM ticket. Nothing is sent.")));
        top.Children.Add(Ui.Row(Ui.Label("account", Pal.Muted), acct, Ui.Label("balance $", Pal.Muted), bal, Ui.Label("room to fail $", Pal.Muted), room,
            Ui.Label("SL ticks", Pal.Muted), stop, Ui.Label("TP ticks", Pal.Muted), tp, Ui.Label("size", Pal.Muted), mode, size, Ui.Label("fee/side $", Pal.Muted), fee));
        top.Children.Add(Ui.Pad(map));
        var dock = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        dock.Children.Add(top);
        dock.Children.Add(new ScrollViewer { Content = table, Margin = new Thickness(6) });
        Content = dock;
        Recalc();
    }

    private void Reset()
    {
        loading = true;
        acct.SelectedIndex = 0;
        bal.Text = Ui.F(Math.Round(s.Equity, 2));
        room.Text = Ui.F(Math.Round(dd.Room(s.Equity), 2));
        bal.IsEnabled = room.IsEnabled = false;
        var tk = Ticket.For(cfg, s, dd, true);
        stop.Text = Math.Max(1, tk.StopTicks).ToString(CultureInfo.InvariantCulture);
        tp.Text = (tk.Targets.FirstOrDefault(t => t.Ticks > 0)?.Ticks ?? tk.StopTicks * 2).ToString(CultureInfo.InvariantCulture);
        mode.SelectedIndex = (int)cfg.SizeMode;
        size.Text = Ui.F(cfg.SizeInput);
        fee.Text = Ui.F(cfg.FeePerSide);
        loading = false;
        Recalc();
    }

    private void Recalc()
    {
        if (loading) return;
        var mine = acct.SelectedIndex <= 0;
        var equity = mine ? s.Equity : Ui.Num(bal, 0);
        var r = mine ? dd.Room(s.Equity) : Ui.Num(room, 0);
        if (mine) { bal.Text = Ui.F(Math.Round(equity, 2)); room.Text = Ui.F(Math.Round(r, 2)); }
        var sl = (int)Ui.Num(stop, 16);
        var tpT = (int)Ui.Num(tp, 32);
        var m = (SizeMode)Math.Max(0, mode.SelectedIndex);
        var input = Ui.Num(size, 1);
        var f = Ui.Num(fee, cfg.FeePerSide);
        head.Text = $"{(mine ? $"{s.Account}: equity ${equity:N2}, floor ${dd.Floor:N2} ({cfg.DrawdownKind})" : "planning a custom account")} · room ${r:N2} · prices from NinjaTrader · never sends an order";

        RiskOut Calc(double tickValue) => CmeRisk.Calc(new RiskInput { TickValue = tickValue, RoomUsd = r, StopTicks = sl, TargetTicks = tpT, FeePerSide = f, Mode = m, Input = input, MaxContracts = cfg.MaxContracts });

        var cur = Calc(s.TickValue);
        var px = s.LastTick is long lt ? lt * s.TickSize : (double?)null;
        string P(double v) => v.ToString("F" + LadderControl.Decimals(s.TickSize), CultureInfo.InvariantCulture);
        map.Text = $"{s.Instrument}: {cur.Contracts} ct · ${cur.DollarsPerTick:N2}/tick · TP {tpT}t win ${cur.Win:N2} · SL {sl}t lose ${cur.Loss:N2} · R:R {cur.RR:N2} · " +
                   $"{(double.IsInfinity(cur.RoomUsed) ? "—" : (cur.RoomUsed * 100).ToString("0", CultureInfo.InvariantCulture) + "%")} of room · {(cur.LosersLeft == int.MaxValue ? "—" : cur.LosersLeft.ToString(CultureInfo.InvariantCulture))} losers like it left · " +
                   $"fails {(double.IsInfinity(cur.TicksToFail) ? "—" : cur.TicksToFail.ToString("0", CultureInfo.InvariantCulture) + " ticks")} against you" +
                   (px is double p ? $"\nlong from {P(p)}: target {P(p + tpT * s.TickSize)} · stop {P(p - sl * s.TickSize)}{(double.IsInfinity(cur.TicksToFail) ? "" : $" · FAIL {P(p - cur.TicksToFail * s.TickSize)}")}" +
                                     $"  |  short: target {P(p - tpT * s.TickSize)} · stop {P(p + sl * s.TickSize)}{(double.IsInfinity(cur.TicksToFail) ? "" : $" · FAIL {P(p + cur.TicksToFail * s.TickSize)}")}" : "");
        map.Foreground = Pal.B(cur.StopPastFail ? Pal.Neg : Pal.Text);

        table.Children.Clear();
        table.Children.Add(RowText("MARKET", "NAME", "TICK", "$/TICK", "CT", "$/TICK×CT", "LOSE @SL", "WIN @TP", "% ROOM", "TICKS TO FAIL", Pal.Muted, false));
        var root = CmeSpecs.RootOf(s.Instrument);
        foreach (var spec in CmeSpecs.All)
        {
            var o = Calc(spec.TickValue);
            var row = RowText(spec.Root, spec.Name, spec.TickSize.ToString("0.#####", CultureInfo.InvariantCulture), $"${spec.TickValue:N2}", o.Contracts.ToString(CultureInfo.InvariantCulture),
                $"${o.DollarsPerTick:N2}", $"${o.Loss:N2}", $"${o.Win:N2}", double.IsInfinity(o.RoomUsed) ? "—" : (o.RoomUsed * 100).ToString("0", CultureInfo.InvariantCulture) + "%",
                double.IsInfinity(o.TicksToFail) ? "—" : o.TicksToFail.ToString("0", CultureInfo.InvariantCulture), o.StopPastFail ? Pal.Neg : spec.Root == root ? Pal.Warn : Pal.Text, spec.Root == root);
            table.Children.Add(row);
        }
        table.Children.Add(Ui.Pad(Ui.Label("Size from your rule at the same stop in ticks. A red row: the stop is past the fail line at that size. Tick values: CME Group contract specs; the DOM uses NinjaTrader's own for the market on screen.", Pal.Muted, size: 11, wrap: true)));
    }

    private static Control RowText(string a, string b, string c, string d, string e, string f, string g, string h, string i, string j, Color col, bool bold)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("60,230,70,70,50,90,90,90,70,*") };
        var cells = new[] { a, b, c, d, e, f, g, h, i, j };
        for (var k = 0; k < cells.Length; k++)
        {
            var t = Ui.Label(cells[k], col, bold, 11.5);
            Grid.SetColumn(t, k);
            grid.Children.Add(t);
        }
        return new Border { BorderBrush = Pal.B(Pal.RowLine), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(4, 3), Child = grid };
    }
}
