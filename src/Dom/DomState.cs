using System.Text.Json;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

public sealed record AccountInfo(string Name, string Conn);
public sealed record WorkingOrder(string Id, string Inst, string Action, string Type, int Qty, int Filled, double Limit, double Stop, string State, string Oco, string Name)
{
    public bool IsBuy => Action is "Buy" or "BuyToCover";
    /// <summary>The price the order rests at on the ladder.</summary>
    public double Price => Type is "StopMarket" or "StopLimit" ? Stop : Limit;
    public bool IsStop => Type is "StopMarket" or "StopLimit";
    public bool IsTarget => Name.StartsWith("BDIQ TP", StringComparison.Ordinal);
    public bool IsStopLeg => Name.StartsWith("BDIQ SL", StringComparison.Ordinal) || (IsStop && Name != "BDIQ entry");
    public int Left => Qty - Filled;
}

/// <summary>Everything the DOM shows, fed by bridge messages, with the Vest panel's analytics on top.
/// All access on the UI thread.</summary>
public sealed class DomState
{
    // ── connection / account ──
    public bool Connected { get; set; }
    public List<AccountInfo> Accounts { get; } = new();
    public string Account { get; set; } = "Sim101";
    public double Cash, Realized, Unrealized, NetLiq, TrailDd, DailyLimit, Commission;
    /// <summary>Positions on the streamed account by instrument (they can arrive before the instrument is set).</summary>
    public Dictionary<string, (int Qty, double Avg)> Positions { get; } = new();
    public int PosQty => Positions.TryGetValue(Instrument, out var p) ? p.Qty : 0;
    public double PosAvg => Positions.TryGetValue(Instrument, out var p) ? p.Avg : 0;
    public List<WorkingOrder> Orders { get; } = new();
    public long LastMsgMs { get; set; }
    public long LastFeedMs { get; set; }

    // ── instrument / market ──
    public string Instrument { get; set; } = "";
    public double TickSize { get; set; } = 0.25;
    public double PointValue { get; set; } = 20;
    public double TickValue => TickSize * PointValue;
    public Book Book { get; set; } = Book.Empty;
    public long? LastTick { get; set; }
    public List<Trade> Tape { get; } = new();
    public bool HistoryLoaded { get; private set; }
    public string HistoryNote { get; private set; } = "";

    // ── analytics (ported from the Vest panel) ──
    public LiquidityTracker Liquidity { get; private set; } = new();
    public OutlookRecorder Outlook { get; } = new();
    public int OutlookObservations { get; private set; }
    public string OutlookRecordStatus { get; private set; } = "collecting observations";
    /// <summary>Naked POCs: every period's POC until price trades there again (history + live).</summary>
    public NakedPocTracker Npoc { get; private set; } = new();
    public Dictionary<long, double> Bought { get; } = new();
    public Dictionary<long, double> Sold { get; } = new();
    /// <summary>Session volume and delta per tick (VWAP session window).</summary>
    public Dictionary<long, double> Vol { get; } = new();
    public Dictionary<long, double> Delta { get; } = new();
    public double CumDelta { get; private set; }
    public DualTrend Trend { get; private set; } = new();
    public DualSnapshot TrendSnap { get; private set; } = new(new(TrendDir.Neutral, null, false), new(TrendDir.Neutral, null, false), CombinedState.Chop, 0);
    public MinuteBars Bars { get; } = new();
    public Vwap Vwap { get; } = new();
    private (long From, long To)? vwapWindow;
    /// <summary>Start of the profile window (epoch ms): the latest ProfileStart, or when the DOM opened or
    /// switched market. Rechecked every 30 s; a new window starts clean.</summary>
    public long ProfileFrom { get; private set; }
    private string profileKey = "";
    private long profileCheckedAt;
    private long openedAt = NowMs();

    private void RefreshProfileWindow(IndicatorCfg ind)
    {
        var now = NowMs();
        if (now - profileCheckedAt < 30_000 && profileKey != "") return;
        profileCheckedAt = now;
        var w = ind.ProfileStart.Length > 0 ? Session.Window(now, ind.ProfileStart, ind.ProfileStart, ind.VwapTz) : null;
        var from = w?.From ?? openedAt;
        var key = $"{Instrument}|{from}|{ind.ProfileHistory}";
        if (key == profileKey) return;
        profileKey = key;
        ProfileFrom = from;
        Vol.Clear(); Delta.Clear(); CumDelta = 0; Bought.Clear(); Sold.Clear();
    }

    /// <summary>Settings changed: rebuild the profile window on the next print.</summary>
    public void ResetProfileWindow() { profileKey = ""; profileCheckedAt = 0; }
    public PaxOr? Or { get; private set; }

    // ── Initial Balance (BookDepthIQ ib_store.rs): every range tracked from history + live prints ──
    public IbTracker Ib { get; private set; } = new("08:30");
    /// <summary>Formed IBs, shared by every instrument: %APPDATA%\BookDepthIQ-NT\ib-history.jsonl.</summary>
    public static IbHistory IbHistory { get; } = new(Path.Combine(Settings.Dir, "ib-history.jsonl"));
    /// <summary>Live prints held while NinjaTrader's history loads, so the IB sees every print oldest first.</summary>
    private readonly List<(long Ms, long Tick, long Size, char Side)> ibPending = new();
    public string IbRoot => CmeSpecs.RootOf(Instrument);

    private void NewIb()
    {
        Ib = new IbTracker(Core.Ib.RthStart(IbRoot, cfg.PaxOr.StartTime));
        ibPending.Clear();
    }

    private void FeedIb(long ms, long t, long size, char side, bool live)
    {
        if (live && !HistoryLoaded)
        {
            if (ibPending.Count < 200_000) ibPending.Add((ms, t, size, side));
            return;
        }
        // History fills in only before the DOM subscribed; live prints cover the rest (nothing twice).
        if (!live && ms >= openedAt) return;
        if (Ib.LastMs is long last && ms < last) return; // out of order: the windows and events need time order
        var (formed, armed) = Ib.OnTrade(ms, t, size, side);
        foreach (var s in armed) RestoreIb(s);
        foreach (var f in formed) RecordIb(f);
    }

    private void FlushIb()
    {
        foreach (var p in ibPending.OrderBy(p => p.Ms).ToList()) FeedIb(p.Ms, p.Tick, p.Size, p.Side, live: true); // HistoryLoaded is set: these go straight in
        ibPending.Clear();
    }

    /// <summary>A session armed after some of its windows closed: seed those from today's history rows.</summary>
    private void RestoreIb(IbSession s)
    {
        if (Ib.AnchorOf(s) is not long a) return;
        foreach (var len in Ib.MissedLengths(s))
            if (IbHistory.Get(Core.Ib.SessionDate(a), IbRoot, s, len, Core.Ib.OpenHhmm(a)) is { } row)
                Ib.RestoreFormed(s, len, row.High, row.Low);
    }

    /// <summary>A whole IB (not partial) from a real feed goes to history. NinjaTrader's Playback connection
    /// replays old tape at today's clock, so it never writes.</summary>
    private void RecordIb(IbFormed f)
    {
        if (f.Partial || Account.StartsWith("Playback", StringComparison.OrdinalIgnoreCase) || IbRoot.Length == 0) return;
        IbHistory.Record(new IbHistoryRow(Core.Ib.SessionDate(f.AnchorMs), IbRoot, f.Session, f.LenMin, Core.Ib.OpenHhmm(f.AnchorMs), f.High, f.Low, TickSize));
    }
    public SpreadWindow Spread { get; } = new();

    // ── journal, log ──
    public List<Exec> Execs { get; } = new();
    public List<JournalTrade> Journal { get; } = new();
    public OrderLog OrderLog { get; } = new();
    /// <summary>Command id → when it was sent (TickCount64), for the answer time; the last answer times.</summary>
    public Dictionary<string, long> Sent { get; } = new();
    public List<long> AckMs { get; } = new();
    public string? LastResult { get; set; }
    public bool LastOk { get; set; } = true;

    private Settings cfg;
    public DomState(Settings cfg, string? journalFile = null)
    {
        this.cfg = cfg;
        journalPath = journalFile ?? Path.Combine(Settings.Dir, "journal.json");
        Trend = new DualTrend(cfg.St1, cfg.St2);
        LoadJournal();
    }

    public event Action? Changed;
    public event Action<JournalTrade>? TradeClosed;
    public void Raise() => Changed?.Invoke();

    public void Note(OrderLogStatus st, string text) => OrderLog.Add(st, text);

    /// <summary>A new heat · pull/stack window, minutes (5, 15, 30, 60): only the heat starts fresh.</summary>
    public void SetHeatWindow(int minutes)
    {
        Liquidity = new LiquidityTracker(Math.Clamp(minutes, 5, 60) * 60_000L);
        Liquidity.SetFlowWindow(cfg.FlowSeconds);
    }

    /// <summary>Settings that change the analytics (ST configs, OR, VWAP session) rebuild them from history.</summary>
    public void ResetAnalytics()
    {
        Outlook.Reset();
        OutlookObservations = 0;
        Liquidity = new LiquidityTracker(Math.Clamp(cfg.HeatMinutes, 5, 60) * 60_000L);
        Liquidity.SetFlowWindow(cfg.FlowSeconds);
        Npoc = new NakedPocTracker(Math.Clamp(cfg.NpocMinutes, 1, 240));
        Bought.Clear(); Sold.Clear(); Vol.Clear(); Delta.Clear(); CumDelta = 0;
        Trend = new DualTrend(cfg.St1, cfg.St2);
        TrendSnap = Trend.Snapshot();
        Bars.Clear(); Vwap.Reset(); vwapWindow = null; Or = null;
        NewIb();
        profileKey = ""; profileCheckedAt = 0;
        Tape.Clear(); LastTick = null; HistoryLoaded = false; HistoryNote = "";
    }

    /// <summary>Applies one bridge message. Unknown types are ignored.</summary>
    public void Apply(JsonElement m)
    {
        LastMsgMs = Environment.TickCount64;
        switch (S(m, "t"))
        {
            case "hello":
                Connected = true;
                Accounts.Clear();
                if (m.TryGetProperty("accounts", out var al))
                    foreach (var a in al.EnumerateArray()) Accounts.Add(new AccountInfo(S(a, "name"), S(a, "conn")));
                break;
            case "inst":
                Instrument = S(m, "name");
                TickSize = D(m, "tick", TickSize);
                PointValue = D(m, "pv", PointValue);
                Book = Book.Empty;
                ResetAnalytics();
                openedAt = NowMs(); // the live profile counts from here (a new market subscription)
                break;
            case "book":
                LastFeedMs = Environment.TickCount64;
                Book = Book.FromLevels(Levels(m, "bids"), Levels(m, "asks"), TickSize, (long)D(m, "ms", 0));
                Liquidity.OnBook(Book, NowMs());
                if (Book.BestBid is long bb && Book.BestAsk is long ba) Spread.Push(ba - bb, NowMs());
                var bidSum = Book.Bids.Sizes.Take(10).Sum();
                var askSum = Book.Asks.Sizes.Take(10).Sum();
                Trend.SetBookImbalance(bidSum + askSum > 0 ? (double)(bidSum - askSum) / (bidSum + askSum) : 0);
                break;
            case "trade":
            {
                LastFeedMs = Environment.TickCount64;
                var side = S(m, "side") is { Length: > 0 } sd ? sd[0] : '?';
                OnPrint((long)D(m, "ms", NowMs()), D(m, "p", 0), (long)D(m, "v", 0), side, live: true);
                break;
            }
            case "hist":
                if (S(m, "inst") != Instrument) break;
                if (m.TryGetProperty("trades", out var tr))
                    foreach (var r in tr.EnumerateArray())
                    {
                        var a = r.EnumerateArray().ToArray();
                        if (a.Length < 4) continue;
                        var sd = a[3].GetString() is { Length: > 0 } x ? x[0] : '?';
                        OnPrint((long)a[0].GetDouble(), a[1].GetDouble(), (long)a[2].GetDouble(), sd, live: false);
                    }
                if (m.TryGetProperty("done", out var dn) && dn.ValueKind == JsonValueKind.True)
                {
                    HistoryLoaded = true;
                    HistoryNote = S(m, "err") is { Length: > 0 } e ? "history: " + e : "";
                    FlushIb();
                }
                break;
            case "account":
                if (S(m, "name") != Account) break;
                Cash = D(m, "cash", 0); Realized = D(m, "realized", 0); Unrealized = D(m, "unrealized", 0);
                NetLiq = D(m, "netliq", 0); TrailDd = D(m, "trailDd", 0); DailyLimit = D(m, "dailyLimit", 0); Commission = D(m, "commission", 0);
                break;
            case "pos":
                if (S(m, "acct") == Account)
                {
                    var q = (int)D(m, "qty", 0);
                    if (q == 0) Positions.Remove(S(m, "inst"));
                    else Positions[S(m, "inst")] = (q, D(m, "avg", 0));
                }
                break;
            case "orders":
                if (S(m, "acct") != Account) break;
                Orders.Clear();
                foreach (var o in m.GetProperty("list").EnumerateArray())
                    Orders.Add(new WorkingOrder(S(o, "id"), S(o, "inst"), S(o, "action"), S(o, "type"), (int)D(o, "qty", 0), (int)D(o, "filled", 0), D(o, "limit", 0), D(o, "stop", 0), S(o, "state"), S(o, "oco"), S(o, "name")));
                NoteStops();
                break;
            case "exec":
                if (S(m, "acct") != Account) break;
                OnExec(new Exec(S(m, "id"), S(m, "inst"), S(m, "name"), S(m, "side") == "b", (int)D(m, "qty", 0), D(m, "price", 0), D(m, "commission", 0) + D(m, "fee", 0), (long)D(m, "ms", NowMs())));
                break;
            case "ack":
            {
                var ok = m.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
                LastOk = ok;
                var cid = S(m, "cid");
                var ms = Sent.TryGetValue(cid, out var at) ? Environment.TickCount64 - at : (long?)null;
                Sent.Remove(cid);
                if (ms is long x) { AckMs.Add(x); if (AckMs.Count > 50) AckMs.RemoveAt(0); }
                LastResult = S(m, "msg") + (ms is long y ? $" · {y} ms" : "");
                Note(ok ? OrderLogStatus.Ok : OrderLogStatus.Refused, LastResult);
                break;
            }
            case "reject":
                LastOk = false;
                LastResult = $"REJECTED by NinjaTrader: {S(m, "action")} {S(m, "type")} {S(m, "qty")} {S(m, "inst")} ({S(m, "name")}) — {S(m, "error")} {S(m, "msg")}";
                Note(OrderLogStatus.Refused, LastResult);
                break;
            case "log":
                Note(OrderLogStatus.Error, S(m, "msg"));
                break;
        }
    }

    // ───────────── prints → analytics ─────────────

    private void OnPrint(long ms, double price, long size, char side, bool live)
    {
        if (size <= 0 || !(price > 0)) return;
        var t = Book.ToTicks(price, TickSize);
        LastTick = t;
        if (live)
        {
            if (FeedAgeMs > 2000) Outlook.Reset();
            if (Outlook.OnTrade(t, ms) is { } outcome)
            {
                try
                {
                    Directory.CreateDirectory(Settings.Dir);
                    File.AppendAllText(Path.Combine(Settings.Dir, "outlook-observations.jsonl"),
                        JsonSerializer.Serialize(new { Instrument, TickSize, Outcome = outcome }) + Environment.NewLine);
                    OutlookObservations++;
                    OutlookRecordStatus = $"{OutlookObservations} observations saved this session";
                }
                catch { OutlookRecordStatus = "observation save failed"; }
            }
            Tape.Insert(0, new Trade(t, size, side, ms));
            // Deep enough that a size filter on the tape (Settings › Tape & log) still has rows to show.
            if (Tape.Count > 5000) Tape.RemoveRange(5000, Tape.Count - 5000);
            Liquidity.OnTrade(t, size, side, NowMs());
        }
        // dual SuperTrend (unknown side counts as half each: no delta either way)
        var snap = Trend.OnTrade(ms, t, size, side == 'b' ? true : side == 's' ? false : (bool?)null);
        if (snap != null) TrendSnap = snap;
        Bars.Add(ms, t);
        // Naked POCs see every print, history and live.
        Npoc.OnPrint(ms, t, size);
        // Profile, Δ and footprint: live prints from the profile start (setting), plus history before the
        // first live print when "use history" is on — so nothing is counted twice.
        var ind = cfg.IndicatorsFor(Instrument);
        RefreshProfileWindow(ind);
        // Live prints count from the start; history only fills in before the DOM subscribed (openedAt).
        var counts = ms >= ProfileFrom && (live || (ind.ProfileHistory && ms < openedAt));
        if (counts)
        {
            Vol[t] = (Vol.TryGetValue(t, out var v) ? v : 0) + size;
            var sd = side == 'b' ? size : side == 's' ? -size : 0;
            Delta[t] = (Delta.TryGetValue(t, out var d) ? d : 0) + sd;
            CumDelta += sd;
            if (side == 'b') Bought[t] = (Bought.TryGetValue(t, out var bq) ? bq : 0) + size;
            else if (side == 's') Sold[t] = (Sold.TryGetValue(t, out var sq) ? sq : 0) + size;
        }
        // VWAP inside its session window
        var w = Session.Window(ms, ind.VwapStart, ind.VwapEnd, ind.VwapTz);
        if (w is { } win && (vwapWindow is null || win.From != vwapWindow.Value.From))
        {
            vwapWindow = win;
            Vwap.Reset();
        }
        if (vwapWindow is { } vw && ms >= vw.From && ms <= vw.To)
        {
            Vwap.Add(t, size);
        }
        // RTH opening range (NQ / ES)
        var root = PaxOr.OrRoot(Instrument);
        if (cfg.PaxOr.Enabled && ind.OrOn && root != "")
        {
            var sess = PaxOr.SessionAt(ms, cfg.PaxOr);
            if (sess != null && (Or is null || Or.Session.Start != sess.Start))
                Or = new PaxOr(sess, PaxOr.LevelFactorTicks(root, cfg.PaxOr, TickSize), cfg.PaxOr.ShowMid || ind.OrMid);
            Or?.Add(ms, t);
        }
        FeedIb(ms, t, size, side, live);
    }

    // ───────────── executions → journal ─────────────

    private readonly TripBuilder trips = new();
    /// <summary>What the last entry click expected (price), for slippage in the journal.</summary>
    public (double Price, long At, bool Long)? LastIntent { get; set; }

    private void OnExec(Exec e)
    {
        Execs.Add(e);
        if (Execs.Count > 2000) Execs.RemoveRange(0, Execs.Count - 2000);
        var row = trips.OnExec(e, Account,
            (inst, isLong) => new TripContext(cfg.Setup, cfg.StopMode.ToString(),
                LastIntent is { } li && li.Long == isLong && NowMs() - li.At < 120_000 ? li.Price : null,
                Orders.Where(o => o.Inst == inst && o.IsStopLeg).Select(o => (double?)o.Price).FirstOrDefault()),
            inst => inst == Instrument ? PointValue : CmeSpecs.Find(inst)?.PointValue ?? PointValue,
            inst => inst == Instrument ? TickSize : CmeSpecs.Find(inst)?.TickSize ?? TickSize);
        if (row is null) return;
        Journal.Add(row);
        SaveJournal();
        TradeClosed?.Invoke(row);
    }

    /// <summary>Brackets attach a moment after the fill: record the stop on the open trip when it shows.</summary>
    public void NoteStops()
    {
        foreach (var o in Orders.Where(o => o.IsStopLeg)) trips.SetStop(o.Inst, o.Price, Account);
    }

    private readonly string journalPath;
    private string JournalPath => journalPath;
    private void LoadJournal()
    {
        try { if (File.Exists(JournalPath)) Journal.AddRange(JsonSerializer.Deserialize<List<JournalTrade>>(File.ReadAllText(JournalPath)) ?? new()); }
        catch { /* a broken journal file starts a new journal */ }
    }
    public void SaveJournal()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
            var temporary = JournalPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(Journal));
            File.Move(temporary, JournalPath, overwrite: true);
        }
        catch (Exception e) { Note(OrderLogStatus.Error, "journal save failed: " + e.Message); }
    }

    // ───────────── views ─────────────

    public IEnumerable<WorkingOrder> OrdersOnScreen => Orders.Where(o => o.Inst == Instrument);

    /// <summary>Open P&L in $ at the price you would exit at now.</summary>
    public double OpenPnl
    {
        get
        {
            if (PosQty == 0) return 0;
            var exit = PosQty > 0 ? Book.BestBid : Book.BestAsk;
            if (exit is null) return Unrealized;
            return (exit.Value * TickSize - PosAvg) * PosQty * PointValue;
        }
    }

    public double Equity => NetLiq > 0 ? NetLiq : Cash + OpenPnl;
    public double? StopOnScreen => OrdersOnScreen.Where(o => o.IsStopLeg).Select(o => (double?)o.Price).FirstOrDefault();
    public long FeedAgeMs => LastFeedMs == 0 ? long.MaxValue : Environment.TickCount64 - LastFeedMs;

    public static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : e.TryGetProperty(k, out v) ? v.ToString() : "";
    private static double D(JsonElement e, string k, double dflt) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : dflt;
    private static IEnumerable<double[]> Levels(JsonElement e, string k) =>
        e.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray().Select(r => r.EnumerateArray().Select(x => x.GetDouble()).ToArray()).ToList()
            : Enumerable.Empty<double[]>();
}
