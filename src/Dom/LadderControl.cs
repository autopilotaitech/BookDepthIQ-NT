using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

/// <summary>A horizontal line across the ladder with its label (VWAP, ST1, OR H, TP, SL, FAIL …).</summary>
public sealed record LadderLine(long Ticks, string Label, Color Color, bool Dashed = false, double Width = 1);

/// <summary>
/// The price ladder, drawn directly (no per-cell controls) — the Vest panel's columns: BUY | SOLD | BID |
/// PRICE | ASK | BOUGHT | SELL | P&L | HEAT 5M · PULL/STACK | PROFILE | Δ, each switchable. Click the
/// buy side (BUY, SOLD, BID) for a buy limit, the sell side (ASK, BOUGHT, SELL) for a sell limit,
/// shift-click for a stop. Click a working order to cancel it, drag it to move it; drag a preview leg to
/// set the stop or a target. Calm follow: the ladder re-centres only when price drifts a sixth of the
/// screen (never while hovered unless price nears the edge). Wheel scrolls, Ctrl+wheel groups.
/// </summary>
public sealed class LadderControl : Control
{
    private enum Col { Buy, Sold, Bid, Price, Ask, Bought, Sell, Pnl, Heat, Profile, Delta }

    private readonly DomState s;
    private readonly Settings cfg;
    private long? center;
    private bool pointerOver;
    private long? hoverRow;
    private Col? hoverCol;
    private WorkingOrder? dragOrder;
    private int? dragPreview; // -1 = SL, 0..2 = TP
    private long? dragTo;
    private (Col Col, double X, double W)[] cols = Array.Empty<(Col, double, double)>();

    public Func<IReadOnlyList<LadderLine>> Lines { get; set; } = () => Array.Empty<LadderLine>();
    /// <summary>Preview legs while flat: (kind −1 = SL / 0..2 = TPn, ticks, buy side?, label).</summary>
    public Func<IReadOnlyList<(int Kind, long Ticks, bool Buy, string Label)>> PreviewLegs { get; set; } = () => Array.Empty<(int, long, bool, string)>();

    public event Action<bool, long, bool>? PlaceRequested;
    public event Action<WorkingOrder>? CancelRequested;
    public event Action<WorkingOrder, long>? MoveRequested;
    public event Action<int, long>? PreviewDropped;
    public event Action<int>? GroupStep;

    public LadderControl(DomState state, Settings settings)
    {
        s = state;
        cfg = settings;
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    private double RowH => Math.Clamp(cfg.RowHeight, 12, 32);
    private int G => Math.Max(1, cfg.Group);
    public void Recenter() { center = null; InvalidateVisual(); }

    private int Rows => Math.Max(1, (int)((Bounds.Height - RowH) / RowH));
    private long Row(long ticks) => LadderMath.BucketOf(ticks, G);
    private long Mid => Row(s.LastTick ?? (s.Book.BestBid is long b && s.Book.BestAsk is long a ? (a + b) / 2 : s.Book.BestBid ?? s.Book.BestAsk ?? 0));

    private long Top()
    {
        var mid = Mid;
        if (center is null || center.Value % G != 0) center = mid;
        var off = Math.Abs(mid - center.Value) / G;
        var limit = pointerOver || dragOrder != null || dragPreview != null ? Rows / 2 - 3 : Math.Max(2, Rows / 6);
        if (mid != 0 && off > limit) center = mid;
        return center.Value + Rows / 2 * G;
    }

    private void DoLayout()
    {
        var c = cfg.Columns;
        var spec = new List<(Col, double)>();
        if (c.Orders) spec.Add((Col.Buy, 0.07));
        if (c.Footprint) spec.Add((Col.Sold, 0.07));
        spec.Add((Col.Bid, 0.11));
        spec.Add((Col.Price, 0.10));
        spec.Add((Col.Ask, 0.11));
        if (c.Footprint) spec.Add((Col.Bought, 0.07));
        if (c.Orders) spec.Add((Col.Sell, 0.07));
        if (c.Pnl) spec.Add((Col.Pnl, 0.08));
        if (c.Heat) spec.Add((Col.Heat, 0.14));
        if (c.Profile) spec.Add((Col.Profile, 0.12));
        if (c.Delta) spec.Add((Col.Delta, 0.06));
        var total = spec.Sum(x => x.Item2);
        var x0 = 0.0;
        cols = spec.Select(t => { var w = t.Item2 / total * Bounds.Width; var r = (t.Item1, x0, w); x0 += w; return r; }).ToArray();
    }

    private (double X, double W)? ColRect(Col c) { foreach (var k in cols) if (k.Col == c) return (k.X, k.W); return null; }
    private Col ColAt(double x) { foreach (var k in cols) if (x < k.X + k.W) return k.Col; return cols.Length > 0 ? cols[^1].Col : Col.Price; }
    private long RowAt(double y) => Top() - (long)Math.Floor((y - RowH) / RowH) * G;
    private double YOf(long row, long top) => RowH + (top - row) / G * RowH;
    private Col BuyCol => cfg.Columns.Orders ? Col.Buy : Col.Bid;
    private Col SellCol => cfg.Columns.Orders ? Col.Sell : Col.Ask;

    private static readonly string[] Heads = { "BUY", "SOLD", "BID", "PRICE", "ASK", "BOUGHT", "SELL", "P&L", "HEAT 5M · PULL/STACK", "PROFILE", "Δ" };

    public override void Render(DrawingContext ctx)
    {
        DoLayout();
        var w = Bounds.Width;
        ctx.FillRectangle(Pal.B(Pal.Bg), new Rect(Bounds.Size));
        var top = Top();
        var rows = Rows;
        var g = G;
        var dec = Decimals(s.TickSize);
        var ind = cfg.IndicatorsFor(s.Instrument);

        ctx.FillRectangle(Pal.B(Pal.Panel), new Rect(0, 0, w, RowH));
        foreach (var (c, x, cw) in cols)
            Text(ctx, Heads[(int)c] + (c == Col.Price && g > 1 ? $" ×{g}" : ""), x, 0, cw, RowH, Pal.Muted, 9.5, TextAlignment.Center);

        var book = s.Book;
        var bids = LadderMath.Aggregate(book.Bids.Ticks, book.Bids.Sizes, g);
        var asks = LadderMath.Aggregate(book.Asks.Ticks, book.Asks.Sizes, g);
        var wallsB = Footprint.Walls(bids);
        var wallsA = Footprint.Walls(asks);
        long maxSize = 1;
        foreach (var v in bids.Values) maxSize = Math.Max(maxSize, v);
        foreach (var v in asks.Values) maxSize = Math.Max(maxSize, v);
        var bought = Grouped(s.Bought, g);
        var sold = Grouped(s.Sold, g);
        var vol = Grouped(s.Vol, g);
        var delta = Grouped(s.Delta, g);
        var prof = Footprint.BuildProfile(vol, delta);
        var maxFp = Math.Max(1, bought.Values.Concat(sold.Values).DefaultIfEmpty(0).Max());
        var imb = ind.FpOn ? Footprint.Find(bought, sold, g, ind.FpRatio, maxFp * ind.FpMinPct / 100.0, ind.FpStack) : Imbalances.Empty;
        var heatScale = HeatScale(top, rows, g);
        var orders = s.OrdersOnScreen.ToList();
        var previews = PreviewLegs();
        var avgRow = s.PosQty != 0 ? (long?)Row((long)Math.Round(s.PosAvg / s.TickSize)) : null;
        long? bestBid = book.BestBid is long bb ? Row(bb) : null;
        long? bestAsk = book.BestAsk is long ba ? Row(ba) : null;
        long? last = s.LastTick is long lt ? Row(lt) : null;

        for (var r = 0; r < rows; r++)
        {
            var t = top - r * g;
            var y = RowH + r * RowH;
            if (prof.Val is long val && prof.Vah is long vah && t >= val && t <= vah && ColRect(Col.Price) is (double px0, double pw0))
                ctx.FillRectangle(Pal.B(Color.Parse("#151b27")), new Rect(px0, y, pw0, RowH));
            ctx.FillRectangle(Pal.B(Pal.RowLine), new Rect(0, y + RowH - 1, w, 1));
            if (hoverRow == t && hoverCol is Col hc && hc is Col.Buy or Col.Sold or Col.Bid or Col.Ask or Col.Bought or Col.Sell && ColRect(hc) is (double hx, double hw))
                ctx.FillRectangle(Pal.B(Pal.Hover), new Rect(hx, y, hw, RowH - 1));

            foreach (var (c, x, cw) in cols)
            {
                switch (c)
                {
                    case Col.Bid when bids.TryGetValue(t, out var bs) && bs > 0:
                    {
                        var bw = cw * bs / maxSize;
                        ctx.FillRectangle(Pal.B(Pal.Bid, 0.28), new Rect(x + cw - bw, y + 2, bw, RowH - 5));
                        Text(ctx, Num(bs), x, y, cw - 4, RowH, wallsB.Contains(t) ? Pal.Warn : Pal.Bid, 11.5, TextAlignment.Right, bold: wallsB.Contains(t));
                        break;
                    }
                    case Col.Ask when asks.TryGetValue(t, out var asz) && asz > 0:
                    {
                        var aw = cw * asz / maxSize;
                        ctx.FillRectangle(Pal.B(Pal.Ask, 0.28), new Rect(x, y + 2, aw, RowH - 5));
                        Text(ctx, Num(asz), x + 4, y, cw - 4, RowH, wallsA.Contains(t) ? Pal.Warn : Pal.Ask, 11.5, TextAlignment.Left, bold: wallsA.Contains(t));
                        break;
                    }
                    case Col.Price:
                    {
                        var col = t == bestBid ? Pal.Bid : t == bestAsk ? Pal.Ask : t == prof.Poc ? Pal.Warn : Pal.Muted;
                        if (t == last) ctx.DrawRectangle(Pal.B(Color.Parse("#2a3142")), new Pen(Pal.B(Pal.Warn), 1.5), new Rect(x + 1, y + 1, cw - 2, RowH - 3));
                        if (t == avgRow) ctx.DrawRectangle(null, new Pen(Pal.B(Pal.Warn), 1, new DashStyle(new double[] { 3, 2 }, 0)), new Rect(x + 1, y + 1, cw - 2, RowH - 3));
                        Text(ctx, (t * s.TickSize).ToString("F" + dec, CultureInfo.InvariantCulture), x, y, cw, RowH, t == last ? Colors.White : col, 11.5, TextAlignment.Center);
                        if (t == prof.Poc) ctx.FillRectangle(Pal.B(Pal.Warn), new Rect(x + 1, y + 2, 2, RowH - 5));
                        break;
                    }
                    case Col.Sold when sold.TryGetValue(t, out var sv) && sv > 0:
                    {
                        var lit = imb.Sell.Contains(t);
                        if (lit) ctx.FillRectangle(Pal.B(Pal.Ask, imb.SellStack.Contains(t) ? 0.45 : 0.25), new Rect(x + 1, y + 1, cw - 2, RowH - 3));
                        Text(ctx, Num(sv), x, y, cw - 4, RowH, lit ? Colors.White : Pal.Ask, 10.5, TextAlignment.Right, bold: lit);
                        break;
                    }
                    case Col.Bought when bought.TryGetValue(t, out var bv) && bv > 0:
                    {
                        var lit = imb.Buy.Contains(t);
                        if (lit) ctx.FillRectangle(Pal.B(Pal.Bid, imb.BuyStack.Contains(t) ? 0.45 : 0.25), new Rect(x + 1, y + 1, cw - 2, RowH - 3));
                        Text(ctx, Num(bv), x + 4, y, cw - 4, RowH, lit ? Colors.White : Pal.Bid, 10.5, TextAlignment.Left, bold: lit);
                        break;
                    }
                    case Col.Pnl when s.PosQty != 0:
                    {
                        var pv = (t * s.TickSize - s.PosAvg) * s.PosQty * s.PointValue;
                        Text(ctx, Usd(pv), x, y, cw - 4, RowH, pv >= 0 ? Pal.Pos : Pal.Neg, 10.5, TextAlignment.Right);
                        break;
                    }
                    case Col.Heat:
                        DrawHeat(ctx, t, g, x, y, cw, heatScale, ind);
                        break;
                    case Col.Profile when vol.TryGetValue(t, out var vv) && vv > 0:
                    {
                        var full = (cw - 28) * vv / Math.Max(1, prof.MaxVol);
                        var inVa = prof.Val is long v0 && prof.Vah is long v1 && t >= v0 && t <= v1;
                        ctx.FillRectangle(Pal.B(t == prof.Poc ? Pal.Warn : Color.Parse("#8b95a8"), t == prof.Poc ? 0.75 : inVa ? 0.5 : 0.28), new Rect(x + 2, y + 3, full, RowH - 7));
                        Text(ctx, Num(vv), x, y, cw - 3, RowH, Pal.Muted, 9.5, TextAlignment.Right);
                        break;
                    }
                    case Col.Delta when delta.TryGetValue(t, out var dv) && dv != 0:
                    {
                        var half = cw / 2 - 2;
                        var dw = half * Math.Min(1, Math.Abs(dv) / Math.Max(1, prof.MaxAbsDelta));
                        var dx = dv > 0 ? x + cw / 2 : x + cw / 2 - dw;
                        ctx.FillRectangle(Pal.B(dv > 0 ? Pal.Bid : Pal.Ask, 0.45), new Rect(dx, y + 3, dw, RowH - 7));
                        Text(ctx, (dv > 0 ? "+" : "") + Num(dv), x, y, cw - 2, RowH, dv > 0 ? Pal.Bid : Pal.Ask, 9.5, TextAlignment.Center);
                        break;
                    }
                }
            }

            foreach (var o in orders)
            {
                if (Row(Book.ToTicks(o.Price, s.TickSize)) != t || ColRect(o.IsBuy ? BuyCol : SellCol) is not (double x, double cw)) continue;
                var col = o.IsTarget ? Pal.Pos : o.IsStopLeg ? Pal.Neg : o.IsBuy ? Pal.Bid : Pal.Ask;
                var pen = o.IsStop ? new Pen(Pal.B(col), 1, new DashStyle(new double[] { 3, 2 }, 0)) : new Pen(Pal.B(col), 1);
                ctx.DrawRectangle(Pal.B(col, dragOrder?.Id == o.Id ? 0.08 : 0.22), pen, new Rect(x + 2, y + 1, cw - 4, RowH - 3));
                var label = o.Name.StartsWith("BDIQ TP", StringComparison.Ordinal) || o.Name.StartsWith("BDIQ SL", StringComparison.Ordinal) ? o.Name[5..] : o.IsStop ? "STP" : "LMT";
                Text(ctx, $"{label} {o.Left}", x, y, cw, RowH, col, 10, TextAlignment.Center, bold: true);
            }
            foreach (var (kind, ticks, buy, lbl) in previews)
            {
                if (Row(ticks) != t || ColRect(buy ? BuyCol : SellCol) is not (double x, double cw)) continue;
                var col = kind < 0 ? Pal.Neg : Pal.Pos;
                ctx.DrawRectangle(Pal.B(col, 0.08), new Pen(Pal.B(col), 1, new DashStyle(new double[] { 2, 2 }, 0)), new Rect(x + 2, y + 1, cw - 4, RowH - 3));
                Text(ctx, lbl, x, y, cw, RowH, col, 9.5, TextAlignment.Center);
            }
            if ((dragOrder != null || dragPreview != null) && dragTo == t)
            {
                var buySide = dragOrder?.IsBuy ?? previews.FirstOrDefault(l => l.Kind == dragPreview).Buy;
                if (ColRect(buySide ? BuyCol : SellCol) is (double x, double cw)) ctx.DrawRectangle(null, new Pen(Pal.B(Pal.Warn), 1.2, new DashStyle(new double[] { 3, 2 }, 0)), new Rect(x + 2, y + 1, cw - 4, RowH - 3));
            }
        }

        var bottom = top - (rows - 1) * g;
        var offs = new List<(LadderLine L, bool Above)>();
        foreach (var l in Lines())
        {
            var row = Row(l.Ticks);
            if (row > top || row < bottom) { offs.Add((l, row > top)); continue; }
            var y = YOf(row, top) + RowH / 2;
            ctx.DrawLine(new Pen(Pal.B(l.Color, 0.85), l.Width, l.Dashed ? new DashStyle(new double[] { 5, 3 }, 0) : null), new Point(0, y), new Point(w, y));
            DrawTag(ctx, l.Label, w - 4, y, l.Color, right: true);
        }
        double upY = RowH + 9, downY = Bounds.Height - 9;
        foreach (var (l, above) in offs.OrderBy(o => Math.Abs(Row(o.L.Ticks) - (o.Above ? top : bottom))).Take(10))
        {
            var dist = Math.Abs(Row(l.Ticks) - (above ? top : bottom)) / g;
            DrawTag(ctx, $"{(above ? "▲" : "▼")} {l.Label} {(l.Ticks * s.TickSize).ToString("F" + dec, CultureInfo.InvariantCulture)} · {dist}", 4, above ? upY : downY, l.Color, right: false);
            if (above) upY += 15; else downY -= 15;
        }

        foreach (var (_, x, _) in cols.Skip(1)) ctx.FillRectangle(Pal.B(Pal.Line), new Rect(x, 0, 1, Bounds.Height));
    }

    /// <summary>90th percentile of |heat| on screen, so colours are relative to what is showing now.</summary>
    private double HeatScale(long top, int rows, int g)
    {
        if (!cfg.Columns.Heat) return 1;
        var vals = new List<double>();
        for (var r = 0; r < rows; r++)
            foreach (var v in s.Liquidity.Heat(top - r * g, g)) if (v != 0) vals.Add(Math.Abs(v));
        if (vals.Count == 0) return 1;
        vals.Sort();
        return Math.Max(1, vals[(int)Math.Min(vals.Count - 1, Math.Floor(vals.Count * 0.9))]);
    }

    private void DrawHeat(DrawingContext ctx, long t, int g, double x, double y, double cw, double p90, IndicatorCfg ind)
    {
        var h = s.Liquidity.Heat(t, g);
        var cellW = (cw - 30) / h.Length;
        for (var i = 0; i < h.Length; i++)
        {
            var v = h[i];
            if (v == 0) continue;
            var a = Math.Min(1, Math.Abs(v) / p90);
            // Yellow = a wall: 3× the typical (median-ish) size showing, as in the Vest panel.
            var c = Math.Abs(v) >= 3 * p90 / 2 ? Pal.Warn : v > 0 ? Pal.Bid : Pal.Ask;
            ctx.FillRectangle(Pal.B(c, 0.15 + 0.75 * a), new Rect(x + 1 + i * cellW, y + 1, Math.Max(1, cellW - 0.5), RowH - 3));
        }
        var f = s.Liquidity.FlowAt(t, g, Math.Max(1, ind.BigMin / 2));
        if (f.Absorb != null) Text(ctx, "ABS", x + cw - 29, y, 28, RowH, Pal.Warn, 9, TextAlignment.Right, bold: true);
        else if (f.Signal) Text(ctx, (f.Net > 0 ? "+" : "") + f.Net, x + cw - 29, y, 28, RowH, f.Net > 0 ? Pal.Pos : Pal.Neg, 9.5, TextAlignment.Right, bold: true);
    }

    private static Dictionary<long, double> Grouped(Dictionary<long, double> m, int g)
    {
        if (g == 1) return m;
        var o = new Dictionary<long, double>();
        foreach (var (k, v) in m) { var b = LadderMath.BucketOf(k, g); o[b] = (o.TryGetValue(b, out var x) ? x : 0) + v; }
        return o;
    }

    private static readonly Typeface Tf = new(Pal.Mono);
    private static readonly Typeface TfBold = new(Pal.Mono, FontStyle.Normal, FontWeight.SemiBold);

    private static void Text(DrawingContext ctx, string text, double x, double y, double w, double h, Color c, double size, TextAlignment align, bool bold = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? TfBold : Tf, size, Pal.B(c)) { MaxTextWidth = Math.Max(1, w), TextAlignment = align, MaxLineCount = 1 };
        ctx.DrawText(ft, new Point(x, y + (h - ft.Height) / 2));
    }

    private static void DrawTag(DrawingContext ctx, string text, double x, double y, Color c, bool right)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Tf, 9.5, Pal.B(c));
        var bx = right ? x - ft.Width - 6 : x;
        ctx.DrawRectangle(Pal.B(Pal.Bg, 0.92), new Pen(Pal.B(c, 0.8), 1), new Rect(bx, y - 7, ft.Width + 6, 14), 2, 2);
        ctx.DrawText(ft, new Point(bx + 3, y - ft.Height / 2));
    }

    private static string Num(double v) => Math.Abs(v) >= 10000 ? (v / 1000).ToString("0.#k", CultureInfo.InvariantCulture) : v.ToString("0", CultureInfo.InvariantCulture);
    public static string Usd(double v) => (v < 0 ? "-$" : "+$") + Math.Abs(v).ToString(Math.Abs(v) >= 1000 ? "N0" : "N2", CultureInfo.InvariantCulture);
    public static int Decimals(double tick)
    {
        var str = tick.ToString("0.########", CultureInfo.InvariantCulture);
        var i = str.IndexOf('.');
        return i < 0 ? 0 : str.Length - i - 1;
    }

    // ───────────── input ─────────────

    private WorkingOrder? OrderAt(long row, Col col)
    {
        var buyCol = col == BuyCol;
        if (!buyCol && col != SellCol) return null;
        return s.OrdersOnScreen.FirstOrDefault(o => Row(Book.ToTicks(o.Price, s.TickSize)) == row && o.IsBuy == buyCol);
    }

    private int? PreviewAt(long row, Col col)
    {
        foreach (var (kind, ticks, buy, _) in PreviewLegs())
            if (Row(ticks) == row && (buy ? BuyCol : SellCol) == col) return kind;
        return null;
    }

    protected override void OnPointerEntered(PointerEventArgs e) { pointerOver = true; base.OnPointerEntered(e); }
    protected override void OnPointerExited(PointerEventArgs e) { pointerOver = false; hoverRow = null; InvalidateVisual(); base.OnPointerExited(e); }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p.Y < RowH) { hoverRow = null; InvalidateVisual(); return; }
        hoverRow = RowAt(p.Y);
        hoverCol = ColAt(p.X);
        if (dragOrder != null || dragPreview != null) dragTo = hoverRow;
        InvalidateVisual();
        base.OnPointerMoved(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        if (p.Y < RowH) return;
        var row = RowAt(p.Y);
        var col = ColAt(p.X);
        if (OrderAt(row, col) is { } o) { dragOrder = o; dragTo = row; e.Pointer.Capture(this); return; }
        if (PreviewAt(row, col) is int k) { dragPreview = k; dragTo = row; e.Pointer.Capture(this); return; }
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (col is Col.Buy or Col.Sold or Col.Bid) PlaceRequested?.Invoke(true, row, shift);
        else if (col is Col.Ask or Col.Bought or Col.Sell) PlaceRequested?.Invoke(false, row, shift);
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (dragOrder != null)
        {
            var o = dragOrder;
            var to = dragTo;
            dragOrder = null;
            dragTo = null;
            e.Pointer.Capture(null);
            if (to is null || to == Row(Book.ToTicks(o.Price, s.TickSize))) CancelRequested?.Invoke(o); // dropped on its own row = cancel
            else MoveRequested?.Invoke(o, to.Value);
            InvalidateVisual();
        }
        else if (dragPreview is int k)
        {
            dragPreview = null;
            var to = dragTo;
            dragTo = null;
            e.Pointer.Capture(null);
            if (to is long t) PreviewDropped?.Invoke(k, t);
            InvalidateVisual();
        }
        base.OnPointerReleased(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) GroupStep?.Invoke(e.Delta.Y > 0 ? -1 : 1);
        else center = (center ?? Mid) + (long)Math.Round(e.Delta.Y * 3) * G;
        InvalidateVisual();
        e.Handled = true;
    }
}
