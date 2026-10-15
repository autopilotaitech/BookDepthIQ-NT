using System.Globalization;
using Avalonia.Input;
using Avalonia.Media;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

// Orders (every one from a click), management (B/E after TP1, trail, stop guard, weekend and day-limit
// closes — each off until the user turns it on, and none ever opens a position), hotkeys, ladder lines.
public sealed partial class MainWindow
{
    private readonly BeWatch be = new();
    private readonly TrailBook trail = new();
    private readonly StopGuard guard = new();
    private long lastMgmtMs;
    private long? lastTrailTick;
    private string weekendDone = "";
    private string dayCloseDone = "";
    private string managementAccount = "";
    private bool managementSeenPosition;
    private long managementEntryUntil;
    private sealed record ReverseIntent(string Account,string Instrument,bool Buy,int Qty,Ticket Ticket,
        string Cid,long PositionRevision,long OrdersRevision,long Expires,bool Acknowledged = false);
    private ReverseIntent? reverseIntent;

    private void ClearManagement()
    {
        trail.Clear(); be.Clear(); guard.Clear(); lastTrailTick=null;
        managementSeenPosition=false; managementEntryUntil=0; managementAccount=s.Account;
        reverseIntent=null;
    }

    private void OnExecutionReceived(Exec execution)
    {
        if (managementAccount==s.Account && cfg.BeAfterTp1) be.OnExecution(execution);
    }

    private void OnCommandAcknowledged(string cid,bool ok)
    {
        if(reverseIntent is not { } intent || intent.Cid!=cid) return;
        if(!ok) { reverseIntent=null; Refuse("REVERSE aborted: flatten was refused"); }
        else reverseIntent=intent with { Acknowledged=true };
    }

    private string NextCid() => $"c{Environment.TickCount64}-{++cidSeq}";

    /// <summary>Sends a command, or says why it can't. Never a silent no-op.</summary>
    private bool Send(string what, Func<string, string> build, bool exit = false)
        => SendCore(what, build);

    private bool SendCore(string what, Func<string, string> build)
    {
        s.Note(OrderLogStatus.Click, $"{s.Account} {what} · {s.Instrument}");
        if (!s.Connected) return Refuse("not connected to NinjaTrader — is the BookDepthIQBridge AddOn compiled (F5) and NinjaTrader running?");
        var cid = NextCid();
        s.Sent[cid] = Environment.TickCount64;
        if (!bridge.Send(build(cid))) { s.Sent.Remove(cid); return Refuse("the bridge connection dropped — reconnecting"); }
        return true;
    }

    private bool Refuse(string why)
    {
        s.LastOk = false;
        s.LastResult = why;
        s.Note(OrderLogStatus.Blocked, why);
        Notice(why);
        return false;
    }

    // ───────────── protect my day ─────────────

    private DayState Day()
    {
        var today = Protect.TradingDay(DomState.NowMs());
        var recorded = s.RecordedToday(DomState.NowMs());
        if (!cfg.Days.TryGetValue(s.Account, out var d) || d.Day != today)
        {
            // The day starts at the first equity NinjaTrader reports; until then nothing is stored.
            d = new DayState(today, s.Equity);
            d = Protect.WithRecordedActivity(d, recorded);
            if (!s.HasAccountSnapshot || !(s.Equity > 0)) return d;
            cfg.Days[s.Account] = d;
            cfg.Save();
        }
        var updated = Protect.WithRecordedActivity(d, recorded);
        if(updated != d && s.HasAccountSnapshot) { cfg.Days[s.Account]=updated; cfg.Save(); }
        d=updated;
        return d;
    }

    private void OnTradeClosed(JournalTrade t)
    {
        Day(); // the recorded trip already supplies the count; do not increment a second counter
        RefreshSide();
    }

    /// <summary>Why a NEW entry can't go (null = it can). Only real blockers: connection, instrument, size 0,
    /// your own day limits. Fat-finger checks only warn.</summary>
    private string? NotReady(Ticket tk)
    {
        if (!s.Connected) return "not connected to NinjaTrader";
        if(s.BridgeOrderVersion<Protocol.OrderVersion) return "update BookDepthIQBridge.cs and compile NinjaTrader (F5): this bridge is too old for safe Ticket orders";
        if(!s.AccountConnectionReady) return "selected NinjaTrader account is disconnected";
        if (!s.HasTradingSnapshot) return "waiting for fresh balances, position and orders for the selected NinjaTrader account";
        if (string.IsNullOrEmpty(s.Instrument)) return "no instrument — type one (e.g. NQ 12-26) and press Go";
        if (tk.StopWhy != null && cfg.BracketOn) return tk.StopWhy;
        if (tk.Contracts <= 0)
            return tk.Risk.BelowOne ? $"size is 0: ${tk.Risk.RiskWanted:N0} buys less than one contract at {tk.StopTicks} ticks" : $"size is 0 — check Size ({sizeMode.SelectedItem} {Ui.F(cfg.SizeInput)})";
        var lim = cfg.Limits;
        if (lim.LossUsd > 0 || lim.MaxTrades > 0 || lim.CooldownSec > 0)
        {
            var v = Protect.Check(lim, Day(), s.Equity, DomState.NowMs(), cfg.BracketOn && tk.StopTicks > 0 ? tk.Risk.Loss : null);
            if (!v.Ok) return v.Message;
        }
        if (!tk.Split.Ok && cfg.BracketOn) return tk.Split.Message;
        return null;
    }

    private void Warn(Ticket tk, long? distTicks)
    {
        var ck = Cost.Checks(Checks(tk, distTicks));
        if (Cost.HasWarning(ck))
        {
            var msg = "sent with warning: " + string.Join(" · ", ck.Where(c => c.Level != CheckLevel.Ok).Select(c => c.Text));
            s.Note(OrderLogStatus.Click, msg);
            Notice(msg);
        }
        if (s.FeedAgeMs > 6000) { s.Note(OrderLogStatus.Click, "market data looks stale — sent anyway"); Notice("market data looks stale — sent anyway"); }
    }

    private CheckInput Checks(Ticket tk, long? distTicks)
    {
        var recent = s.Journal.TakeLast(20).ToList();
        var far = cfg.FarTicks > 0 ? cfg.FarTicks : s.Trend.T1.AtrTicks is double a ? (long)Math.Round(4 * a) : 0;
        var spread = s.Book.BestAsk - s.Book.BestBid;
        return new CheckInput
        {
            Contracts = tk.Contracts, NormalContracts = recent.Count >= 5 ? recent.Average(t => t.Qty) : null, SizeMult = cfg.SizeMult,
            DistTicks = distTicks, FarTicks = far, Gate = spread is long sp ? Cost.SpreadGate(sp, s.Spread.Quantile(0.5), s.Spread.Quantile(0.9)) : null, SpreadTicks = spread,
            SlTicks = tk.StopTicks, BracketsOn = cfg.BracketOn, FailTicks = double.IsInfinity(tk.Risk.TicksToFail) ? null : tk.Risk.TicksToFail, MaxContracts = cfg.MaxContracts,
        };
    }

    // ───────────── entries ─────────────

    private bool Reducing(bool buy) => (s.PosQty>0 && !buy) || (s.PosQty<0 && buy);
    private string? ReductionNotReady(int qty)
    {
        if(!s.Connected || !s.HasTradingSnapshot) return "waiting for NinjaTrader position and order snapshots";
        if(s.BridgeOrderVersion<Protocol.OrderVersion) return "update and compile the bridge before submitting reductions";
        if(qty<=0 || qty>Math.Abs(s.PosQty)) return "exit size must be between 1 and the remaining position; use REVERSE to change direction";
        return null;
    }

    private void Market(bool buy)
    {
        if(reverseIntent!=null) { Refuse("REVERSE is waiting for flat — cancel or flatten to abort"); return; }
        var tk = Ticket.For(cfg, s, dd, buy);
        if ((Reducing(buy) ? ReductionNotReady(tk.Contracts) : NotReady(tk)) is string why) { s.Note(OrderLogStatus.Click, $"{(buy ? "BUY" : "SELL")} MKT"); Refuse(why); return; }
        if(!Reducing(buy)) Warn(tk, null);
        var exp = buy ? s.Book.BestAsk : s.Book.BestBid;
        if (exp is long e) s.LastIntent = (e * s.TickSize, DomState.NowMs(), buy);
        Entry(buy, "market", null, tk);
    }

    private void Place(bool buy, long row, bool stop)
    {
        if(reverseIntent!=null) { Refuse("REVERSE is waiting for flat — cancel or flatten to abort"); return; }
        var price = row * s.TickSize;
        var tk = Ticket.For(cfg, s, dd, buy, row);
        var reducing=Reducing(buy);
        if ((reducing ? ReductionNotReady(tk.Contracts) : NotReady(tk)) is string why) { s.Note(OrderLogStatus.Click, $"{(buy ? "BUY" : "SELL")} {(stop ? "STOP" : "LIMIT")} @ {Px(price)}"); Refuse(why); return; }
        var mid = s.LastTick ?? s.Book.BestBid;
        if(!reducing) Warn(tk, mid is long m ? row - m : null);
        s.LastIntent = (price, DomState.NowMs(), buy);
        if (!stop && cfg.ScaleOn)
        {
            var end = row + (buy ? -1 : 1) * cfg.ScaleSpanTicks;
            var plan = Targets.ScaleOrders(tk.Contracts, row, end, cfg.ScaleCount, cfg.ScaleSkew, cfg.ScaleDir);
            if (!plan.Ok) { Refuse(plan.Message); return; }
            var slices=reducing ? Array.Empty<Piece>() : tk.Slices(cfg.BracketOn);
            var target=0; var allocated=0; var sent=false;
            foreach (var leg in plan.Legs)
            {
                var child=new List<Piece>(); var numbers=new List<int>(); var left=leg.Qty;
                while(left>0 && target<slices.Count)
                {
                    var take=Math.Min(left,slices[target].Qty-allocated);
                    child.Add(new Piece(slices[target].Ticks,take)); numbers.Add(target+1);
                    left-=take; allocated+=take;
                    if(allocated==slices[target].Qty) { target++; allocated=0; }
                }
                if(!Send($"SCALE {(reducing ? "OUT" : "IN")} {(buy ? "BUY" : "SELL")} {leg.Qty} ct @ {Px(leg.Ticks * s.TickSize)}", cid => Protocol.Order(cid, s.Account, s.Instrument, buy ? "buy" : "sell", "limit", leg.Qty, leg.Ticks * s.TickSize, child, !reducing && cfg.BracketOn ? tk.StopTicks : 0,numbers,reduceOnly:reducing))) break;
                sent=true;
            }
            if(sent && !reducing) ArmManagement(tk,slices,buy);
            return;
        }
        Entry(buy, stop ? "stop" : "limit", price, tk);
    }

    private void Entry(bool buy, string type, double? price, Ticket tk)
    {
        var reducing=Reducing(buy);
        var slices = reducing ? Array.Empty<Piece>() : tk.Slices(cfg.BracketOn);
        var sl = !reducing && cfg.BracketOn ? tk.StopTicks : 0;
        var what = $"{(buy ? "BUY" : "SELL")} {type.ToUpperInvariant()} {tk.Contracts} ct{(price is double p ? " @ " + Px(p) : "")}" +
                   (slices.Count > 0 ? $" · {string.Join(" ", slices.Where(x => x.Ticks > 0).Select((x, i) => $"TP{i + 1} {x.Ticks}t×{x.Qty}"))} SL {sl}t" : " · no bracket");
        if (!Send(what, cid => Protocol.Order(cid, s.Account, s.Instrument, buy ? "buy" : "sell", type, tk.Contracts, price, slices, sl,reduceOnly:reducing))) return;
        if(!reducing) ArmManagement(tk,slices,buy);
    }

    private void ArmManagement(Ticket tk,IReadOnlyList<Piece> slices,bool buy)
    {
        if(managementAccount!=s.Account) ClearManagement();
        managementAccount=s.Account; managementEntryUntil=DomState.NowMs()+8000;
        if (cfg.BracketOn && cfg.BeAfterTp1 && slices.Count(x => x.Ticks > 0) >= 2) { be.Arm(s.Instrument, cfg.BeOffsetTicks,slices[0].Qty,buy); s.Note(OrderLogStatus.Ok, $"B/E after TP1 armed (offset {cfg.BeOffsetTicks}t)"); }
        if (cfg.Trail != TrailMode.Off)
        {
            trail.Arm(s.Instrument, tk.StopTicks, cfg.TrailStartR, cfg.Trail == TrailMode.St1 ? 1 : cfg.Trail == TrailMode.St2 ? 2 : 0);
            s.Note(OrderLogStatus.Ok, $"trailing stop armed ({trailBox.SelectedItem})");
        }
    }

    // ───────────── management buttons (exits are never gated) ─────────────

    private void BreakEven()
    {
        reverseIntent=null;
        if (s.PosQty == 0) { Refuse("B/E: flat — nothing to protect"); return; }
        var isLong = s.PosQty > 0;
        var target = Math.Round(s.PosAvg / s.TickSize) + (isLong ? 1 : -1) * cfg.BeOffsetTicks;
        var bid = s.Book.BestBid; var ask = s.Book.BestAsk;
        if (isLong && bid is long b && target >= b) { Refuse($"B/E stop {Px(target * s.TickSize)} is not below the bid"); return; }
        if (!isLong && ask is long a && target <= a) { Refuse($"B/E stop {Px(target * s.TickSize)} is not above the ask"); return; }
        MoveStops((long)target, "B/E");
    }

    /// <summary>Moves every stop leg on the position to ticks; with none, adds one for the whole position.</summary>
    private void MoveStops(long ticks, string why)
    {
        var stops = s.OrdersOnScreen.Where(o => o.IsStopLeg && o.IsBuy == (s.PosQty < 0)).ToList();
        if (stops.Count == 0) { Send($"{why}: add stop @ {Px(ticks * s.TickSize)}", cid => Protocol.AddStop(cid, s.Account, s.Instrument, ticks * s.TickSize), exit: true); return; }
        foreach (var o in stops) Send($"{why}: {Leg(o)} → {Px(ticks * s.TickSize)}", cid => Protocol.Move(cid, s.Account, o.Id, ticks * s.TickSize), exit: true);
    }

    private void CloseHalf()
    {
        reverseIntent=null;
        var q = Math.Abs(s.PosQty) / 2;
        if (s.PosQty == 0) { Refuse("CLOSE HALF: flat"); return; }
        if (q == 0) { Refuse($"{Math.Abs(s.PosQty)} ct can't be halved — use FLATTEN"); return; }
        Send($"CLOSE HALF {q} of {Math.Abs(s.PosQty)}", cid => Protocol.ClosePart(cid, s.Account, s.Instrument, q), exit: true);
    }

    private void Reverse()
    {
        if(reverseIntent!=null) { Refuse("REVERSE already pending — cancel or flatten to abort"); return; }
        if (s.PosQty == 0) { Refuse("REVERSE: flat — nothing to reverse"); return; }
        var buy = s.PosQty < 0;
        var qty = Math.Abs(s.PosQty);
        var tk = Ticket.For(cfg, s, dd, buy,contracts:qty);
        if (NotReady(tk) is string why) { Refuse("REVERSE: " + why); return; }
        if(tk.Contracts!=qty) { Refuse("REVERSE: position exceeds your configured maximum contracts"); return; }
        ClearManagement();
        var cidSent=""; var posRevision=s.PositionRevision; var ordersRevision=s.OrdersRevision;
        if(Send($"REVERSE: waiting for confirmed flat before {(buy ? "BUY" : "SELL")} {qty} ct", cid => { cidSent=cid; return Protocol.Flatten(cid,s.Account,s.Instrument); },exit:true))
            reverseIntent=new(s.Account,s.Instrument,buy,qty,tk,cidSent,posRevision,ordersRevision,DomState.NowMs()+8000);
    }

    private void CancelAll() { reverseIntent=null; Send("CANCEL ALL", cid => Protocol.CancelAll(cid, s.Account, s.Instrument), exit: true); }
    private void CancelWorkingOrder(WorkingOrder order)
    {
        reverseIntent=null;
        Send($"CANCEL {Leg(order)} @ {Px(order.Price)}",cid=>Protocol.Cancel(cid,s.Account,order.Id),exit:true);
    }
    private void Flatten() { reverseIntent=null; Send("FLATTEN", cid => Protocol.Flatten(cid, s.Account, s.Instrument), exit: true); }
    private void Kill()
    {
        ClearManagement();
        Send("KILL — every position and order on the account", cid => Protocol.Kill(cid, s.Account), exit: true);
    }

    // ───────────── management without a click (each off until you turn it on) ─────────────

    private void Manage()
    {
        var now = DomState.NowMs();
        if(!s.Connected || !s.AccountConnectionReady || managementAccount!=s.Account) { ClearManagement(); return; }
        if(!s.HasTradingSnapshot) return;
        if (now - lastMgmtMs < 250) return;
        lastMgmtMs = now;
        if(reverseIntent is { } intent)
        {
            if(intent.Account!=s.Account || intent.Instrument!=s.Instrument || now>intent.Expires)
            { reverseIntent=null; Refuse("REVERSE aborted: selection changed or flat confirmation timed out"); }
            else if(intent.Acknowledged && s.PositionRevision>intent.PositionRevision && s.OrdersRevision>intent.OrdersRevision
                && s.PosQty==0 && !s.OrdersOnScreen.Any())
            {
                reverseIntent=null;
                var current=intent.Ticket.Reprice(cfg,s,dd);
                if(NotReady(current) is string why) Refuse("REVERSE aborted: "+why);
                else if(current.Contracts!=intent.Qty) Refuse("REVERSE aborted: maximum contracts changed");
                else Entry(intent.Buy,"market",null,current);
            }
        }
        var abs = Math.Abs(s.PosQty);
        var isLong = s.PosQty > 0;
        var entryPending=s.OrdersOnScreen.Any(o=>o.Name=="BDIQ entry") || (!managementSeenPosition && now<=managementEntryUntil);
        if(!cfg.BeAfterTp1) be.Clear();
        if(cfg.Trail==TrailMode.Off) { trail.Clear(); lastTrailTick=null; }
        if(!cfg.StopGuard) guard.Clear();
        if(abs>0) managementSeenPosition=true;

        // B/E after TP1
        var tps = s.OrdersOnScreen.Count(o => o.IsTarget);
        if (be.Inst == s.Instrument)
        {
            var r = be.Check(abs, tps,entryPending);
            if (r == "fire")
            {
                var target = (long)Math.Round(s.PosAvg / s.TickSize) + (isLong ? 1 : -1) * cfg.BeOffsetTicks;
                MoveStops(target, "B/E after TP1");
            }
        }
        // trailing stop
        if (abs > 0 && trail.Has(s.Instrument) && s.LastTick is long lt && lt != lastTrailTick)
        {
            lastTrailTick = lt;
            var stop = s.StopOnScreen is double sp ? (long?)Math.Round(sp / s.TickSize) : null;
            var snap = s.TrendSnap;
            TrendLine? L(TrendSnapshot t) => t.LineTicks is long x && t.Dir != TrendDir.Neutral ? new TrendLine(x, t.Dir) : null;
            var want = trail.OnTrade(s.Instrument, isLong, (long)Math.Round(s.PosAvg / s.TickSize), lt, stop, L(snap.T1), L(snap.T2));
            if (want is long w) MoveStops(w, "trail");
        }
        if(abs==0) { trail.ResetPosition(s.Instrument); lastTrailTick=null; }
        if (abs == 0 && !entryPending && (managementSeenPosition || now>managementEntryUntil))
        { trail.Clear(s.Instrument); be.Clear(s.Instrument); lastTrailTick = null; managementSeenPosition=false; }
        // stop guard
        if (cfg.StopGuard)
            foreach (var (_, stopPx) in guard.Check(abs > 0 ? new[] { (s.Instrument, s.StopOnScreen) } : Array.Empty<(string, double?)>(), now))
                Send($"STOP GUARD: stop missing — re-place @ {Px(stopPx)}", cid => Protocol.AddStop(cid, s.Account, s.Instrument, stopPx), exit: true);
        // close before the weekend halt
        if (cfg.WeekendClose && abs > 0 && Core.Manage.WeekendCloseAt(now, cfg.WeekendCloseTime) is long at && now >= at)
        {
            var key = Protect.TradingDay(now) + s.Account;
            if (weekendDone != key) { weekendDone = key; Send("WEEKEND CLOSE: flatten before the CME weekend halt", cid => Protocol.Kill(cid, s.Account), exit: true); }
        }
        // close at the daily loss limit
        if (cfg.CloseAtDayLimit && cfg.DayLossUsd > 0 && Protect.LimitHit(cfg.Limits, Day(), s.Equity))
        {
            var key = Day().Day + s.Account;
            if (dayCloseDone != key) { dayCloseDone = key; Send($"DAILY LOSS LIMIT ${cfg.DayLossUsd:N0}: close everything", cid => Protocol.Kill(cid, s.Account), exit: true); }
        }
    }

    // ───────────── preview ─────────────

    private IReadOnlyList<(int, long, bool, string)> PreviewLegs()
    {
        if (cfg.Preview == PreviewMode.Off || s.PosQty != 0) return Array.Empty<(int, long, bool, string)>();
        var isLong = cfg.Preview == PreviewMode.Long;
        var refT = isLong ? s.Book.BestAsk : s.Book.BestBid;
        if (refT is not long r) return Array.Empty<(int, long, bool, string)>();
        var tk = Ticket.For(cfg, s, dd, isLong, r);
        var dir = isLong ? 1 : -1;
        var o = new List<(int, long, bool, string)>();
        if (tk.StopTicks > 0) o.Add((-1, r - dir * tk.StopTicks, !isLong, $"SL -${tk.Risk.Loss:N0}"));
        var legs = tk.Split.Ok ? tk.Split.Legs : new List<Piece>();
        for (var i = 0; i < legs.Count; i++)
            o.Add((i, r + dir * legs[i].Ticks, !isLong, $"TP{i + 1} +${legs[i].Ticks * s.TickValue * legs[i].Qty:N0}"));
        return o;
    }

    private void PreviewDropped(int kind, long row)
    {
        var isLong = cfg.Preview == PreviewMode.Long;
        if ((isLong ? s.Book.BestAsk : s.Book.BestBid) is not long r) return;
        var ticks = (int)Math.Abs(row - r);
        if (ticks == 0) return;
        if (kind < 0)
        {
            var stop = Ticket.For(cfg, s, dd, isLong).StopTicks;
            if (cfg.StopMode != StopMode.Ticks)
            {
                cfg.TpTicks = cfg.TpR.Select(x => (int)Math.Round(x * stop)).ToArray();
                cfg.StopMode = StopMode.Ticks;
            }
            cfg.SlTicks = ticks;
        }
        else if (cfg.StopMode == StopMode.Ticks) { var a = cfg.TpTicks.ToArray(); a[kind] = ticks; cfg.TpTicks = a; }
        else { var st = Math.Max(1, Ticket.For(cfg, s, dd, isLong).StopTicks); var a = cfg.TpR.ToArray(); a[kind] = Math.Round(ticks / (double)st, 2); cfg.TpR = a; }
        cfg.Save();
        LoadFields();
    }

    // ───────────── ladder lines ─────────────





    private IReadOnlyList<LadderLine> LadderLines()
    {
        var o = new List<LadderLine>();
        var ind = cfg.IndicatorsFor(s.Instrument);
        if (ind.VwapOn && s.Vwap.Value is double v)
        {
            o.Add(new LadderLine((long)Math.Round(v), "VWAP", Pal.Vwap, false, 1.5));
            foreach (var (k, lo, hi) in s.Vwap.Bands(ind.VwapBands))
            {
                o.Add(new LadderLine((long)Math.Round(hi), $"+{Ui.F(k)}σ", Pal.Vwap, true));
                o.Add(new LadderLine((long)Math.Round(lo), $"-{Ui.F(k)}σ", Pal.Vwap, true));
            }
        }
        var snap = s.TrendSnap;
        if (cfg.ShowTrend)
        {
            if (snap.T1.LineTicks is long l1) o.Add(new LadderLine(l1, (snap.T1.Dir == TrendDir.Up ? "▲" : "▼") + "ST1", snap.T1.Dir == TrendDir.Up ? Pal.StUp : Pal.StDn, false, 1.5));
            if (snap.T2.LineTicks is long l2) o.Add(new LadderLine(l2, (snap.T2.Dir == TrendDir.Up ? "▲" : "▼") + "ST2", snap.T2.Dir == TrendDir.Up ? Pal.StUp : Pal.StDn, true));
            if (cfg.ShowNoChase && snap.T1.LineTicks is long line && s.Trend.T1.AtrTicks is double atr && atr > 0 && snap.T1.Dir != TrendDir.Neutral)
            {
                var up = snap.T1.Dir == TrendDir.Up;
                o.Add(new LadderLine((long)Math.Round(line + (up ? 1 : -1) * Structure.ScalpNearAtr * atr), up ? "NO CHASE above (ST1 +2 ATR)" : "NO CHASE below (ST1 −2 ATR)", Pal.NoChase, true));
            }
        }
        // Naked POCs: each finished period's POC, until price trades there again.
        if (cfg.NpocOn)
            foreach (var n in s.Npoc.Levels)
                o.Add(new LadderLine(n.Ticks, $"nPOC {DateTimeOffset.FromUnixTimeMilliseconds(n.PeriodStartMs).LocalDateTime:HH:mm}", Pal.Npoc, true, 1.5));
        // GEX (BookDepthIQ's GEX Levels): numbered levels first and thinnest, then range, walls, magnet,
        // and the flip last — the level being watched. Old data dims rather than vanishing.
        if (GexView() is { } gv)
        {
            var gc = cfg.Gex;
            var dim = GexStale();
            Color C(Color c) => dim ? Color.FromArgb(150, c.R, c.G, c.B) : c;
            long T(double price) => (long)Math.Round(price / s.TickSize);
            var w = Math.Max(0.5, gc.LineWidth);
            var n = 0;
            foreach (var (price, _) in gv.Set.Levels.Take(Math.Clamp(gc.TopN, 0, 7))) o.Add(new LadderLine(T(price), $"GEX {++n}", C(Pal.GexLevel), false, w * 0.7));
            if (gc.ShowRange)
            {
                if (gv.RangeHigh is double rh) o.Add(new LadderLine(T(rh), "RANGE HIGH", C(Pal.GexRange), true, w * 0.7));
                if (gv.RangeLow is double rl) o.Add(new LadderLine(T(rl), "RANGE LOW", C(Pal.GexRange), true, w * 0.7));
            }
            if (gv.Set.CallWall is double cw) o.Add(new LadderLine(T(cw), "CALL WALL", C(Pal.GexCall), false, w * 1.4));
            if (gv.Set.PutWall is double pw) o.Add(new LadderLine(T(pw), "PUT WALL", C(Pal.GexPut), false, w * 1.4));
            if (gc.ShowMagnet && gv.Set.Magnet is double mg) o.Add(new LadderLine(T(mg), "MAGNET", C(Pal.GexMagnet), true, w * 1.2));
            if (gv.Set.Flip is double fl) o.Add(new LadderLine(T(fl), "γ FLIP", C(Pal.GexFlip), false, w * 1.6));
        }
        // Opening range, coloured as the Vest panel (BookDepthIQ PAXOR): OR H blue and OR L red, solid
        // 3 px; MID gold 2 px; EXT rungs dashed 2 px in their side's colour; H/L dashed while forming.
        if (s.Or is { } or)
        {
            var forming = or.Forming(DomState.NowMs());
            foreach (var l in or.Levels())
                o.Add(l.Kind switch
                {
                    OrKind.High => new LadderLine(l.Ticks, l.Label + (forming ? " forming" : ""), Pal.OrHigh, forming, 3),
                    OrKind.Low => new LadderLine(l.Ticks, l.Label + (forming ? " forming" : ""), Pal.OrLow, forming, 3),
                    OrKind.Mid => new LadderLine(l.Ticks, l.Label, Pal.OrMid, forming, 2),
                    OrKind.Upper => new LadderLine(l.Ticks, l.Label, Pal.OrHigh, true, 2),
                    _ => new LadderLine(l.Ticks, l.Label, Pal.OrLow, true, 2),
                });
        }
        // Initial Balance (BookDepthIQ ib.ts): extensions and VA dotted and thinner, MID dashed, H / L solid —
        // dashed while the window is still open (extensions withheld until it forms).
        if (cfg.Ib.On && s.Ib.View(cfg.Ib.Session, cfg.Ib.LengthMin) is { High: long ih, Low: long il } ibv)
        {
            var w = Math.Max(0.5, cfg.Ib.LineWidth);
            var name = $"IB {ibv.LenMin}m{(ibv.Partial ? " PARTIAL" : "")}";
            var span = ih - il;
            if (cfg.Ib.ShowExtensions && ibv.Formed && span > 0)
                foreach (var m in Ib.ExtensionMults)
                {
                    var off = (long)Math.Round(span * (m - 1));
                    o.Add(new LadderLine(ih + off, $"IB {Ui.F(m)}x H", Pal.IbLevel, true, w * 0.7));
                    o.Add(new LadderLine(il - off, $"IB {Ui.F(m)}x L", Pal.IbLevel, true, w * 0.7));
                }
            if (cfg.Ib.ShowValueArea && ibv.Vah is long vah && ibv.Val is long val)
            {
                o.Add(new LadderLine(vah, "IB VAH", Pal.IbLevel, true, w * 0.7));
                o.Add(new LadderLine(val, "IB VAL", Pal.IbLevel, true, w * 0.7));
            }
            if (cfg.Ib.ShowMid) o.Add(new LadderLine((long)Math.Round(il + span / 2.0), $"{name} MID", Pal.IbLevel, true, w));
            o.Add(new LadderLine(ih, $"{name} H{(ibv.Formed ? "" : " forming")}", Pal.IbHigh, !ibv.Formed, w * 1.4));
            o.Add(new LadderLine(il, $"{name} L{(ibv.Formed ? "" : " forming")}", Pal.IbLow, !ibv.Formed, w * 1.4));
        }
        if (ind.FpOn && ind.FpLines)
        {
            var maxFp = Math.Max(1, s.Bought.Values.Concat(s.Sold.Values).DefaultIfEmpty(0).Max());
            var imb = Footprint.Find(s.Bought, s.Sold, 1, ind.FpRatio, maxFp * ind.FpMinPct / 100.0, ind.FpStack);
            foreach (var (t, buy, rows) in IndicatorCfg.StackZones(imb.BuyStack, imb.SellStack, 1))
                o.Add(new LadderLine(t, $"{(buy ? "BUY" : "SELL")} STACK ×{rows}", buy ? Pal.StackBuy : Pal.StackSell, true));
        }
        if (lastState == MarketState.Chop && s.Bars.Range(30, DomState.NowMs()) is var (hi30, lo30))
        {
            o.Add(new LadderLine(hi30, "RANGE H 30m — fade", Pal.Range, true));
            o.Add(new LadderLine(lo30, "RANGE L 30m — fade", Pal.Range, true));
        }
        // working bracket legs with their $
        foreach (var leg in s.OrdersOnScreen.Where(x => (x.IsTarget || x.IsStopLeg) && s.PosQty != 0))
        {
            var usd = (leg.Price - s.PosAvg) * (s.PosQty > 0 ? 1 : -1) * leg.Left * s.PointValue;
            o.Add(new LadderLine(Book.ToTicks(leg.Price, s.TickSize), $"{Leg(leg)} {LadderControl.Usd(usd)}", leg.IsTarget ? Pal.Pos : Pal.Neg, true));
        }
        // the price where the account fails at this size
        if (s.PosQty != 0 && FailPrice() is double fp) o.Add(new LadderLine(Book.ToTicks(fp, s.TickSize), "FAIL", Pal.Neg, false, 2));
        // preview entry line
        if (cfg.Preview != PreviewMode.Off && s.PosQty == 0 && (cfg.Preview == PreviewMode.Long ? s.Book.BestAsk : s.Book.BestBid) is long pr)
        {
            var tk = Ticket.For(cfg, s, dd, cfg.Preview == PreviewMode.Long, pr);
            o.Add(new LadderLine(pr, $"PREVIEW {(cfg.Preview == PreviewMode.Long ? "LONG" : "SHORT")} {tk.Contracts} ct", Pal.Text, true));
        }
        return o;
    }

    private double? FailPrice()
    {
        if (s.PosQty == 0 || s.PointValue <= 0) return null;
        var room = dd.Room(s.Equity); // equity includes open P&L, so this is what is left from here
        var mark = s.PosQty > 0 ? s.Book.BestBid : s.Book.BestAsk;
        if (mark is not long m) return null;
        return m * s.TickSize - Math.Sign(s.PosQty) * room / (Math.Abs(s.PosQty) * s.PointValue);
    }

    // ───────────── hotkeys ─────────────

    private void OnKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.K) { Kill(); e.Handled = true; return; }
        if (e.Source is Avalonia.Controls.TextBox) return;
        switch (e.Key)
        {
            case Key.Space: ladder.Recenter(); break;
            case Key.F: Flatten(); break;
            case Key.Escape: CancelAll(); break;
            case Key.OemPlus or Key.Add: ladder.Zoom(1, ladder.Bounds.Height / 2); break;
            case Key.OemMinus or Key.Subtract: ladder.Zoom(-1, ladder.Bounds.Height / 2); break;
            case Key.Home: ladder.ResetZoom(); break;
            case Key.B or Key.S or Key.E or Key.R:
                if (!cfg.HotkeysArmed)
                {
                    var msg = $"hotkey {e.Key} ignored — hotkeys are SAFE (click HOTKEYS SAFE to arm them)";
                    s.Note(OrderLogStatus.Blocked, msg);
                    Notice(msg);
                    break;
                }
                if (e.Key == Key.B) Market(true);
                else if (e.Key == Key.S) Market(false);
                else if (e.Key == Key.E) BreakEven();
                else Reverse();
                break;
            default: return;
        }
        e.Handled = true;
    }

    private async void CopyLog()
    {
        var header = new[]
        {
            $"BookDepthIQ-NT order log · copied {DateTime.UtcNow:O}",
            $"account {s.Account} · {s.Instrument} · bridge {(s.Connected ? "connected" : "DOWN")} · feed age {(s.FeedAgeMs == long.MaxValue ? "none" : s.FeedAgeMs + " ms")} · hotkeys {(cfg.HotkeysArmed ? "ARMED" : "SAFE")} · size {cfg.SizeMode} {Ui.F(cfg.SizeInput)} · SL {cfg.StopMode} {cfg.SlTicks}t",
            "",
        };
        if (Clipboard is { } cb) await cb.SetTextAsync(s.OrderLog.Text(header));
        Notice("order log copied");
    }

    private static string Pct(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
}
