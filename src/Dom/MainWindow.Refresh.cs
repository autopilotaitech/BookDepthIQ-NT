using System.Globalization;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

// Screen refresh: header pills, account and position strips, bracket bar, risk / checks / cost lines,
// tape, journal and the order log.
public sealed partial class MainWindow
{
    private long lastRefresh;
    private int lastLogCount = -1, lastJournalCount = -1;

    private void Tick()
    {
        Manage();
        ladder.InvalidateVisual();
        tapeView.InvalidateVisual();
        logView.InvalidateVisual();
        var now = Environment.TickCount64;
        if (now - lastRefresh < 200) return;
        lastRefresh = now;
        RefreshTop();
        RefreshTicket();
        foreach (var p in chipPaint) p();
        if (s.OrderLog.Entries.Count != lastLogCount || s.Journal.Count != lastJournalCount) RefreshSide();
        // A command NinjaTrader never answered says so instead of looking like nothing happened.
        foreach (var (cid, at) in s.Sent.Where(x => Environment.TickCount64 - x.Value > 8000).ToList())
        {
            s.Sent.Remove(cid);
            s.LastOk = false;
            s.LastResult = "no answer from NinjaTrader in 8 s — check NinjaTrader's Log tab and the order log";
            s.Note(OrderLogStatus.Error, s.LastResult);
            Notice(s.LastResult);
        }
        if (now > noticeUntil) notice.Text = "";
        // Status lines take no space when they have nothing to say.
        foreach (var line in new[] { riskLine, checkLine, costLine, lockLine, notice, resultBar }) line.IsVisible = !string.IsNullOrEmpty(line.Text);
    }

    private void RefreshTop()
    {
        var ack = Cost.Percentiles(s.AckMs);
        Ui.SetPill(bridgePill, s.Connected ? (ack is { } a ? $"● NT {a.P50} ms" : "● NT") : "○ NT", s.Connected ? Pal.Pos : Pal.Warn,
            s.Connected ? $"connected to the BookDepthIQBridge AddOn in NinjaTrader{(ack is { } a2 ? $" · answer time median {a2.P50} ms, p95 {a2.P95} ms (last {s.AckMs.Count} commands)" : "")}" : $"waiting for the NinjaTrader bridge on 127.0.0.1:{Protocol.Port} — copy nt8/AddOns/BookDepthIQBridge.cs into NinjaTrader and press F5");
        var age = s.FeedAgeMs;
        var health = Cost.FeedHealth(age == long.MaxValue ? null : age, s.Connected);
        Ui.SetPill(feedPill, (health == "live" ? $"FEED {Math.Min(age, 9999)} ms" : health.ToUpperInvariant()) + (s.HistoryLoaded || !s.Connected ? "" : " · history…"),
            health == "live" ? Pal.Pos : health == "slow" ? Pal.Warn : Pal.Neg, "time since NinjaTrader's last book or print" + (s.HistoryNote != "" ? " · " + s.HistoryNote : ""));

        var snap = s.TrendSnap;
        var stText = snap.State switch { CombinedState.StrongLong => "▲▲ STRONG LONG", CombinedState.WeakLong => "▲ LONG", CombinedState.StrongShort => "▼▼ STRONG SHORT", CombinedState.WeakShort => "▼ SHORT", _ => "CHOP" };
        Ui.SetPill(trendPill, $"{stText} {(snap.Confidence >= 0 ? "+" : "")}{snap.Confidence}", snap.State is CombinedState.StrongLong or CombinedState.WeakLong ? Pal.Pos : snap.State is CombinedState.StrongShort or CombinedState.WeakShort ? Pal.Neg : Pal.Muted,
            "dual SuperTrend (BookDepthIQ trend.rs): ST1 60 s and ST2 15 s, confidence −100..100");

        var now = DomState.NowMs();
        var atr = s.Trend.T1.AtrTicks;
        var vwapSigma = s.Vwap.Sigma;
        var view = Structure.Market(s.Bars.RegimeAt(now), lastState, snap.T1.Dir, s.LastTick, s.Vwap.Value, vwapSigma, snap.T1.LineTicks, atr);
        lastState = view.State;
        Ui.SetPill(statePill, view.State switch { MarketState.TrendUp => "TREND ▲", MarketState.TrendDown => "TREND ▼", MarketState.Chop => "CHOP", _ => "warming" } + (view.Extended != null ? " · EXTENDED" : ""),
            view.State == MarketState.Chop ? Pal.Warn : view.State == MarketState.Warming ? Pal.Muted : Pal.Text, view.Why);
        var sc = Structure.Scalp(snap.T1.Dir, s.LastTick, snap.T1.LineTicks, atr, s.Bars.RegimeAt(now, Structure.ScalpPullbackBars));
        if (sc is null) Ui.SetPill(scalpPill, "SCALP —", Pal.Muted, "SuperTrend scalp read: needs ST1 and 6 minutes of prints");
        else
        {
            var side = sc.Long ? "LONG" : "SHORT";
            var (txt, col) = sc.Zone switch
            {
                ScalpZone.Go => ($"GO {side} — pullback to ST1", Pal.Pos),
                ScalpZone.Near => ($"{side} — wait for the dip", Pal.Text),
                _ => ($"STRETCHED {sc.DistAtr:0.0} ATR — don't chase", Pal.Warn),
            };
            Ui.SetPill(scalpPill, txt, col, $"ST1 {side}: price {sc.DistAtr:0.0} ATR past the line, last 5 min {(sc.Pull < 0 ? "pulling back" : "running")}. Measured on Vest NQ: GO won 63% at 10/10; 3+ ATR out won 47%.");
        }

        // account strip
        var eq = s.Equity;
        dd.OnEquity(eq);
        if (!cfg.Peaks.TryGetValue(cfg.Account, out var pk) || Math.Abs(pk - dd.Peak) > 0.005) { cfg.Peaks[cfg.Account] = dd.Peak; cfg.Save(); }
        var room = dd.Room(eq);
        var day = Day();
        var dayPnl = eq - day.StartEquity;
        Set("EQUITY", $"${eq:N0}", Pal.Text);
        Set("ROOM", $"${room:N0}", room <= 0 ? Pal.Neg : Pal.Text);
        Set("TODAY", $"{LadderControl.Usd(dayPnl)} · {day.Trades}", dayPnl >= 0 ? Pal.Pos : Pal.Neg);
        ToolTip.SetTip(roomTile, $"{s.Account} · floor ${dd.Floor:N2} ({cfg.DrawdownKind}) · room = equity − floor (or today's limit if tighter)" +
                                 $"{(s.TrailDd != 0 ? $" · NT trailing DD ${s.TrailDd:N2}" : "")} · {s.Instrument} tick {s.TickSize} = ${s.TickValue:N2}");
        roomBar.Value = cfg.MaxDrawdown > 0 ? Math.Clamp(room / cfg.MaxDrawdown, 0, 1) : 0;
        roomBar.Foreground = Pal.B(roomBar.Value > 0.5 ? Pal.Pos : roomBar.Value > 0.25 ? Pal.Warn : Pal.Neg);

        // position strip
        var q = s.PosQty;
        Set("POSITION", q == 0 ? "flat" : $"{(q > 0 ? "+" : "")}{q} ct", q > 0 ? Pal.Bid : q < 0 ? Pal.Ask : Pal.Muted);
        Set("AVG", q == 0 ? "—" : Px(s.PosAvg), Pal.Text);
        Set("FAIL PRICE", FailPrice() is double fp ? Px(fp) : "—", Pal.Neg);
        Set("OPEN P&L", q == 0 ? "$0.00" : LadderControl.Usd(s.OpenPnl), s.OpenPnl >= 0 ? Pal.Pos : Pal.Neg);
        var ttf = q != 0 && s.TickValue > 0 ? room / (Math.Abs(q) * s.TickValue) : double.PositiveInfinity;
        Set("TO FLOOR", double.IsInfinity(ttf) ? "—" : $"{ttf:0} t", ttf < 20 ? Pal.Neg : Pal.Text);
        var prof = ladder.VisibleProfile;
        Set("POC · VA", prof.Poc is long poc ? $"{Px(poc * s.TickSize)} · {Px((prof.Val ?? poc) * s.TickSize)}–{Px((prof.Vah ?? poc) * s.TickSize)}" : "—", Pal.Warn);
        Set("CUM Δ", (s.CumDelta >= 0 ? "+" : "") + s.CumDelta.ToString("0", CultureInfo.InvariantCulture), s.CumDelta >= 0 ? Pal.Bid : Pal.Ask);
        string Line(TrendSnapshot t) => t.LineTicks is long l ? $"{(t.Dir == TrendDir.Up ? "▲" : "▼")} {Px(l * s.TickSize)}" : "—";
        Set("ST1 · ST2", $"{Line(snap.T1)} · {Line(snap.T2)}", snap.T1.Dir == TrendDir.Up ? Pal.Pos : snap.T1.Dir == TrendDir.Down ? Pal.Neg : Pal.Muted);
        var vz = s.Vwap.Value is double vw && vwapSigma is double sg && sg > 0 && s.LastTick is long lt ? (lt - vw) / sg : (double?)null;
        Set("VWAP", s.Vwap.Value is double vv ? $"{Px(vv * s.TickSize)} {(vz is double z ? $"{z:+0.0;-0.0}σ" : "")}" : "—", Pal.Vwap);
        var share = Footprint.BidShare(s.Book.Bids.Sizes.Take(10), s.Book.Asks.Sizes.Take(10));
        Set("BID / ASK LIQ", share is double b ? $"{Pct(b)} / {Pct(1 - b)}" : "—", Pal.Text);
        Set("REALIZED", LadderControl.Usd(s.Realized), s.Realized >= 0 ? Pal.Pos : Pal.Neg);
        RefreshGex();

        // bracket bar in a trade
        bracketPad.IsVisible = q != 0;
        if (q == 0) bracketLine.Text = "";
        else
        {
            var legs = s.OrdersOnScreen.Where(o => o.IsTarget || o.IsStopLeg).OrderBy(o => o.Price).ToList();
            var parts = legs.Select(o => $"{Leg(o)} {Px(o.Price)} {LadderControl.Usd((o.Price - s.PosAvg) * Math.Sign(q) * o.Left * s.PointValue)}");
            var mgmt = string.Join(" · ", new[] { be.Inst == s.Instrument ? "B/E after TP1 armed" : null, trail.Has(s.Instrument) ? $"trail {trailBox.SelectedItem}" : null, cfg.StopGuard ? "stop guard on" : null }.Where(x => x != null));
            bracketLine.Text = $"{(q > 0 ? "LONG" : "SHORT")} {Math.Abs(q)} @ {Px(s.PosAvg)} · now {LadderControl.Usd(s.OpenPnl)} · {(legs.Count == 0 ? "NO STOP OR TARGET ON NINJATRADER" : string.Join("  |  ", parts))}{(mgmt != "" ? " · " + mgmt : "")}";
            bracketLine.Foreground = Pal.B(legs.Any(o => o.IsStopLeg) ? Pal.Text : Pal.Neg);
        }
    }

    private long lastGexSample;

    /// <summary>GEX: follow the instrument, feed the basis from the live book mid once a second (LIVE
    /// prints only — a replayed tape against today's chain would be nonsense), and show the read.</summary>
    private void RefreshGex()
    {
        var g = cfg.Gex;
        if (!g.On) { gex.Stop(); Set("GEX", "off", Pal.Muted); return; }
        gex.Follow(s.Instrument);
        var wall = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var livePrints = s.Tape.Count > 0 && Math.Abs(wall - s.Tape[0].TimeMs) < 120_000;
        if (livePrints && Environment.TickCount64 - lastGexSample >= 1000 && s.Book.BestBid is long bb && s.Book.BestAsk is long ba)
        {
            lastGexSample = Environment.TickCount64;
            gex.SampleBasis((bb + ba) / 2.0 * s.TickSize);
        }
        var box = (Control?)stat["GEX"].Parent;
        var view = GexView();
        if (gex.Store is not { } st) { Set("GEX", "n/a", Pal.Muted); if (box != null) ToolTip.SetTip(box, gex.Status); return; }
        if (view is null)
        {
            var why = st.Frozen == null && st.Live == null ? "loading…" : $"basis {st.BasisSamples}/30";
            Set("GEX", why, Pal.Muted);
            if (box != null) ToolTip.SetTip(box, $"{gex.Status}\nNo levels drawn until the futures/{st.Source.Name} {(st.Source.Conversion == GexConversion.Ratio ? "ratio" : "spread")} is measured from live prints (30 s) — a level at the wrong price is worse than none.");
            return;
        }
        var net = view.Set.NetGex;
        var stale = GexStale();
        Set("GEX", $"{(net >= 0 ? "+γ" : "−γ")} {Money(Math.Abs(net))}{(stale ? " · old" : "")}", stale ? Pal.Muted : net >= 0 ? Pal.Pos : Pal.Neg);
        if (box != null)
        {
            var set = view.Set;
            string P(double? v) => v is double x ? Px(x) : "—";
            var age = gex.PayloadUtc is DateTime t ? $"{(DateTime.UtcNow - t).TotalMinutes:0} min ago" : "—";
            ToolTip.SetTip(box,
                $"{(net >= 0 ? "POSITIVE gamma: dealers damp moves" : "NEGATIVE gamma: dealers amplify moves")} · net {(net < 0 ? "−" : "")}{Money(Math.Abs(net))} per 1%\n" +
                $"call wall {P(set.CallWall)} · put wall {P(set.PutWall)} · flip {P(set.Flip)} · magnet {P(set.Magnet)}\n" +
                $"range {P(view.RangeLow)} – {P(view.RangeHigh)}\n" +
                $"{(g.Scope switch { GexStore.Scope.ZeroDte => "today's expiry", GexStore.Scope.All => "every expiry", _ => "4 nearest expiries" })} · {(g.Live ? "recalculated map" : "daily open-interest map")} · " +
                $"{st.Source.Name} {(view.Mode == GexConversion.Ratio ? $"× {view.Basis:0.0000}" : $"{view.Basis:+0.00;-0.00}")} · payload {age}\n{gex.Status}\n" +
                "Dealer positioning is inferred (customers sell calls, buy puts), not observed. Delayed data. Levels, not direction.");
        }
    }

    private GexView? GexView() => cfg.Gex.On ? gex.Store?.View(s.TickSize, cfg.Gex.Scope, cfg.Gex.Live) : null;

    /// <summary>Old when CBOE hasn't answered for 10 min or the payload is over 30 min old (closed, weekend).</summary>
    private bool GexStale() =>
        gex.LastOk is not DateTime ok || (DateTime.UtcNow - ok).TotalMinutes > 10 || gex.PayloadUtc is not DateTime p || (DateTime.UtcNow - p).TotalMinutes > 30;

    private static string Money(double v) =>
        v >= 1e9 ? $"${v / 1e9:0.0}B" : v >= 1e6 ? $"${v / 1e6:0}M" : $"${v / 1e3:0}K";

    private void Set(string key, string text, Color c)
    {
        var t = stat[key];
        t.Text = text;
        t.Foreground = Pal.B(c);
    }

    private void RefreshTicket()
    {
        var isLong = cfg.Preview != PreviewMode.Short;
        var tk = Ticket.For(cfg, s, dd, isLong);
        var r = tk.Risk;
        var be = CmeRisk.BreakEvenTicks(s.TickValue, cfg.FeePerSide);
        var why = tk.StopWhy ?? (r.BelowOne ? $" · ${r.RiskWanted:N0} buys less than 1 contract at {tk.StopTicks}t" : r.Capped ? $" · capped at your max {cfg.MaxContracts}" : "");
        var split = tk.Split.Ok ? (tk.Split.Legs.Count > 1 ? " · " + string.Join(" ", tk.Split.Legs.Select((l, i) => $"TP{i + 1} {l.Ticks}t×{l.Qty}")) : "") : " · " + tk.Split.Message;
        // Readout tiles: the numbers that matter, big; the details on the line under them.
        void Tile(string k, string text, Color c) { if (readout.TryGetValue(k, out var tb)) { tb.Text = text; tb.Foreground = Pal.B(c); } }
        Tile("ct", r.Contracts.ToString(CultureInfo.InvariantCulture), r.Contracts > 0 ? Pal.Text : Pal.Warn);
        Tile("risk", $"${r.Loss:N0}", r.StopPastFail ? Pal.Neg : Pal.Ask);
        Tile("win", $"${r.Win:N0}", Pal.Pos);
        Tile("rr", r.RR.ToString("0.00", CultureInfo.InvariantCulture), r.RR >= 1.5 ? Pal.Pos : r.RR >= 1 ? Pal.Text : Pal.Warn);
        Tile("room", double.IsInfinity(r.RoomUsed) ? "—" : Pct(r.RoomUsed), r.RoomUsed > 0.5 ? Pal.Neg : r.RoomUsed > 0.25 ? Pal.Warn : Pal.Text);
        Tile("losers", r.LosersLeft == int.MaxValue ? "—" : r.LosersLeft.ToString(CultureInfo.InvariantCulture), r.LosersLeft <= 2 ? Pal.Neg : Pal.Text);
        riskLine.Text = $"SL {tk.StopTicks}t · ${r.DollarsPerTick:N2}/tick · fees ${r.Fees:N2} · break-even {be:0.0}t{split}{why}";
        riskLine.Foreground = Pal.B(r.StopPastFail ? Pal.Neg : Pal.Muted);
        if (r.StopPastFail) riskLine.Text += $" · STOP IS PAST THE FAIL LINE ({r.TicksToFail:0}t)";

        var ck = Cost.Checks(Checks(tk, null)).Where(c => c.Level != CheckLevel.Ok).ToList();
        checkLine.Text = ck.Count == 0 ? "" : "! " + string.Join(" · ", ck.Select(c => c.Text)) + " — warnings only, one click sends";

        var buy = Cost.Trip(s.Book, true, Math.Max(1, r.Contracts), s.TickValue, cfg.FeePerSide);
        var sell = Cost.Trip(s.Book, false, Math.Max(1, r.Contracts), s.TickValue, cfg.FeePerSide);
        var tp1 = tk.Targets.FirstOrDefault(t => t.Ticks > 0)?.Ticks ?? 0;
        var edge = Cost.EdgeRatio(tp1, buy?.BreakEvenTicks);
        var spread = s.Book.BestAsk - s.Book.BestBid;
        var gate = spread is long sp ? Cost.SpreadGate(sp, s.Spread.Quantile(0.5), s.Spread.Quantile(0.9)) : null;
        costLine.Text = $"spread {(spread?.ToString() ?? "—")}t {gate?.ToString().ToLowerInvariant() ?? "learning…"} · " +
                        $"BUY ≈ {(buy is null ? "—" : Px(buy.Entry.AvgTicks * s.TickSize))} slip {buy?.Entry.SlipTicks:0.#}t · SELL ≈ {(sell is null ? "—" : Px(sell.Entry.AvgTicks * s.TickSize))} slip {sell?.Entry.SlipTicks:0.#}t · " +
                        $"break-even {buy?.BreakEvenTicks:0.0}t (${buy?.TotalUsd:N2}){(edge is double e ? $" · TP1 {tp1}t = {e:0.0}× cost" : "")}";

        var lim = cfg.Limits;
        if (lim.LossUsd > 0 || lim.MaxTrades > 0 || lim.CooldownSec > 0)
        {
            var v = Protect.Check(lim, Day(), s.Equity, DomState.NowMs(), r.Loss);
            lockLine.Text = v.Ok ? "" : $"ENTRIES PAUSED — {v.Message}. FLATTEN, CANCEL, CLOSE HALF, B/E and KILL keep working.";
        }
        else lockLine.Text = "";

        resultBar.Text = s.LastResult is null ? "" : (s.LastOk ? "✓ " : "✗ ") + s.LastResult;
        resultBar.Foreground = Pal.B(s.LastOk ? Pal.Pos : Pal.Neg);

        var names = s.Accounts.Select(a => a.Name).ToList();
        if (!names.SequenceEqual(acctBox.ItemsSource?.Cast<string>() ?? Enumerable.Empty<string>()))
        {
            loading = true;
            acctBox.ItemsSource = names;
            acctBox.SelectedItem = names.Contains(cfg.Account) ? cfg.Account : null;
            loading = false;
        }
        armBtn.Content = cfg.HotkeysArmed ? "HOTKEYS ARMED" : "HOTKEYS SAFE";
    }

    private void RefreshSide()
    {
        lastLogCount = s.OrderLog.Entries.Count;
        lastJournalCount = s.Journal.Count;
        sideTitle.Text = cfg.ShowLog ? "LOG" : cfg.ShowJournal ? "JOURNAL" : "TAPE";
        logButtons.IsVisible = cfg.ShowLog;
        if (cfg.ShowLog) { if (side.Content != logView) side.Content = logView; }
        else if (cfg.ShowJournal)
        {
            var st = Journal.Stats(s.Journal);
            journalSum.Text = $"{st.N} trades · {Pct(st.WinRate)} won · {LadderControl.Usd(st.TotalUsd)} net · avg {(st.AvgR is double r ? $"{r:+0.00;-0.00}R" : "—")} · fees ${st.FeesUsd:N2}" +
                              $"{(st.AvgSlipTicks is double sl ? $" · slip avg {sl:+0.0;-0.0}t" : "")}" + Environment.NewLine + string.Join("  ", st.BySetup.Select(x => $"{x.Setup} {x.N}: {LadderControl.Usd(x.TotalUsd)}"));
            journalList.Children.Clear();
            journalList.Children.Add(Ui.Pad(journalSum, 4));
            journalList.Children.Add(Ui.Row(Ui.Btn("copy CSV", async () => { if (Clipboard is { } cb) await cb.SetTextAsync(Journal.Csv(s.Journal)); Notice("journal CSV copied"); }),
                Ui.Btn("clear", () => { s.Journal.Clear(); s.SaveJournal(); RefreshSide(); })));
            foreach (var t in Enumerable.Reverse(s.Journal).Take(80))
                journalList.Children.Add(Ui.Label($"{DateTimeOffset.FromUnixTimeMilliseconds(t.Close).LocalDateTime:HH:mm} {CmeSpecs.RootOf(t.Inst),-4} {(t.Long ? "L" : "S")} {t.Qty,2} {(t.R is double rr ? $"{rr:+0.0;-0.0}R" : "  — "),6} {LadderControl.Usd(t.Pnl),10} {t.Setup}",
                    t.Pnl >= 0 ? Pal.Pos : Pal.Neg, size: 11));
            journalScroll ??= new ScrollViewer { Content = journalList };
            if (side.Content != journalScroll) side.Content = journalScroll;
        }
        else if (side.Content != tapeView) side.Content = tapeView;
    }
}
