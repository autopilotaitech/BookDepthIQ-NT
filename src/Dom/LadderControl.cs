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
/// The price ladder, drawn directly (no per-cell controls): HEAT · PULL/STACK | BUY | SOLD | BID |
/// PRICE | ASK | BOUGHT | SELL | P&L | PROFILE | Δ, each switchable. Click the
/// buy side (BUY, SOLD, BID) for a buy limit, the sell side (ASK, BOUGHT, SELL) for a sell limit,
/// shift-click for a stop. A click on a row that has orders adds another; right-click cancels them, drag
/// moves them; drag a preview leg to
/// set the stop or a target. Calm follow: the ladder re-centres only when price drifts a sixth of the
/// screen (never while hovered unless price nears the edge). Zoom: wheel or drag on PRICE (anchored at the
/// mouse), double-click PRICE resets; wheel elsewhere scrolls (Shift = a page) and holds the view 15 s.
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
    /// <summary>Every order on the dragged order's side and row: they move together.</summary>
    private List<WorkingOrder> dragGroup = new();
    private bool dragShift;
    private int? dragPreview; // -1 = SL, 0..2 = TP
    private long? dragTo;
    private (Col Col, double X, double W)[] cols = Array.Empty<(Col, double, double)>();

    public Func<IReadOnlyList<LadderLine>> Lines { get; set; } = () => Array.Empty<LadderLine>();
    /// <summary>Preview legs while flat: (kind −1 = SL / 0..2 = TPn, ticks, buy side?, label).</summary>
    public Func<IReadOnlyList<(int Kind, long Ticks, bool Buy, string Label)>> PreviewLegs { get; set; } = () => Array.Empty<(int, long, bool, string)>();

    /// <summary>The profile of the rows on screen (POC / VA for the position strip), in row ticks.</summary>
    public Profile VisibleProfile { get; private set; } = Profile.Empty;

    public event Action<bool, long, bool>? PlaceRequested;
    public event Action<WorkingOrder>? CancelRequested;
    public event Action<WorkingOrder, long>? MoveRequested;
    public event Action<int, long>? PreviewDropped;

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
        // After a manual scroll/zoom the view holds for 15 s, moving only if price is about to leave the screen.
        var held = Environment.TickCount64 < holdUntil;
        var limit = held ? Rows / 2 - 1 : pointerOver || dragOrder != null || dragPreview != null || zoomDragY != null ? Rows / 2 - 3 : Math.Max(2, Rows / 6);
        if (mid != 0 && off > limit) center = mid;
        return center.Value + Rows / 2 * G;
    }

    private void DoLayout()
    {
        var c = cfg.Columns;
        var spec = new List<(Col, double)>();
        // Heat + pull/stack at the far left, next to BUY (owner, 2026-10-13).
        void Add(Col col, double dflt) => spec.Add((col, cfg.ColWidths.TryGetValue(col.ToString(), out var w) && w > 0 ? w : dflt));
        if (c.Heat) Add(Col.Heat, 0.14);
        if (c.Orders) Add(Col.Buy, 0.07);
        if (c.Footprint) Add(Col.Sold, 0.07);
        Add(Col.Bid, 0.11);
        Add(Col.Price, 0.10);
        Add(Col.Ask, 0.11);
        if (c.Footprint) Add(Col.Bought, 0.07);
        if (c.Orders) Add(Col.Sell, 0.07);
        if (c.Pnl) Add(Col.Pnl, 0.08);
        if (c.Profile) Add(Col.Profile, 0.12);
        if (c.Delta) Add(Col.Delta, 0.06);
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
            Text(ctx, c == Col.Heat ? $"HEAT {cfg.HeatMinutes}M ▾ · PULL/STACK" : Heads[(int)c] + (c == Col.Price && g > 1 ? $" ×{g}" : ""), x, 0, cw, RowH, Pal.Muted, 9.5, TextAlignment.Center);

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
        // Visible-range profile: POC, value area and the bar scale come from the rows on screen only, so
        // they follow scrolling and zoom; Δ scales to the visible rows too. Both update on every print.
        var bottomRow = top - (rows - 1) * g;
        var vol = Grouped(s.Vol, g).Where(kv => kv.Key >= bottomRow && kv.Key <= top).ToDictionary(kv => kv.Key, kv => kv.Value);
        var delta = Grouped(s.Delta, g).Where(kv => kv.Key >= bottomRow && kv.Key <= top).ToDictionary(kv => kv.Key, kv => kv.Value);
        var prof = Footprint.BuildProfile(vol, delta);
        VisibleProfile = prof;
        var maxFp = Math.Max(1, bought.Values.Concat(sold.Values).DefaultIfEmpty(0).Max());
        var imb = ind.FpOn ? Footprint.Find(bought, sold, g, ind.FpRatio, maxFp * ind.FpMinPct / 100.0, ind.FpStack) : Imbalances.Empty;
        var heatScale = HeatScale(top, rows, g);
        var orders = s.OrdersOnScreen.ToList();
        var previews = PreviewLegs();
        var avgRow = s.PosQty != 0 ? (long?)Row((long)Math.Round(s.PosAvg / s.TickSize)) : null;
        long? bestBid = book.BestBid is long bb ? Row(bb) : null;
        long? bestAsk = book.BestAsk is long ba ? Row(ba) : null;
        long? last = s.LastTick is long lt ? Row(lt) : null;

        bool InVa(long t) => prof.TotalVol > 0 && prof.Val is long val && prof.Vah is long vah && t + g / 2.0 >= val && t + g / 2.0 <= vah + g;

        // Z-order, back to front: row backgrounds → indicator LINES → bars, numbers, orders → line TAGS
        // and off-screen pins. A line never covers a price or a size.
        for (var r = 0; r < rows; r++)
        {
            var t = top - r * g;
            var y = RowH + r * RowH;
            // Value area on the PRICE column: BookDepthIQ's faint magenta band between VAL and VAH.
            if (InVa(t) && ColRect(Col.Price) is (double px0, double pw0))
                ctx.FillRectangle(Pal.B(Pal.VaBand, 15 / 255.0), new Rect(px0, y, pw0, RowH));
            ctx.FillRectangle(Pal.B(Pal.RowLine), new Rect(0, y + RowH - 1, w, 1));
            if (hoverRow == t && hoverCol is Col hc && hc is Col.Buy or Col.Sold or Col.Bid or Col.Ask or Col.Bought or Col.Sell && ColRect(hc) is (double hx, double hw))
                ctx.FillRectangle(Pal.B(Pal.Hover), new Rect(hx, y, hw, RowH - 1));
        }

        var bottom = top - (rows - 1) * g;
        var lines = Lines();
        var onScreen = new List<(LadderLine L, double Y)>();
        var offs = new List<(LadderLine L, bool Above)>();
        var lineRows = new HashSet<long>();
        foreach (var l in lines)
        {
            var row = Row(l.Ticks);
            if (row > top || row < bottom) { offs.Add((l, row > top)); continue; }
            var ly = YOf(row, top) + RowH / 2;
            ctx.DrawLine(new Pen(Pal.B(l.Color, 0.85), l.Width, l.Dashed ? new DashStyle(new double[] { 5, 3 }, 0) : null), new Point(0, ly), new Point(w, ly));
            onScreen.Add((l, ly));
            lineRows.Add(row);
        }

        // On a line's row, wipe the line from behind a number's glyphs first; the bar and the number then
        // draw over that, so the line passes BEHIND every number and the bars stay whole.
        void Clear(long t, string text, double x, double y, double tw, double size, TextAlignment align, bool bold = false)
        {
            if (!lineRows.Contains(t)) return;
            var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? TfBold : Tf, size, Pal.B(Pal.Text));
            var left = align == TextAlignment.Right ? x + tw - ft.Width : align == TextAlignment.Center ? x + (tw - ft.Width) / 2 : x;
            ctx.FillRectangle(Pal.B(Pal.Bg), new Rect(left - 2, y + (RowH - ft.Height) / 2, ft.Width + 4, ft.Height));
        }

        for (var r = 0; r < rows; r++)
        {
            var t = top - r * g;
            var y = RowH + r * RowH;
            var inVa = InVa(t);
            foreach (var (c, x, cw) in cols)
            {
                switch (c)
                {
                    case Col.Bid when bids.TryGetValue(t, out var bs) && bs > 0:
                    {
                        var bw = cw * bs / maxSize;
                        Clear(t, Num(bs), x, y, cw - 4, 11.5, TextAlignment.Right, wallsB.Contains(t));
                        ctx.FillRectangle(Pal.B(Pal.Bid, 0.28), new Rect(x + cw - bw, y + 2, bw, RowH - 5));
                        Text(ctx, Num(bs), x, y, cw - 4, RowH, wallsB.Contains(t) ? Pal.Warn : Pal.Bid, 11.5, TextAlignment.Right, bold: wallsB.Contains(t));
                        break;
                    }
                    case Col.Ask when asks.TryGetValue(t, out var asz) && asz > 0:
                    {
                        var aw = cw * asz / maxSize;
                        Clear(t, Num(asz), x + 4, y, cw - 4, 11.5, TextAlignment.Left, wallsA.Contains(t));
                        ctx.FillRectangle(Pal.B(Pal.Ask, 0.28), new Rect(x, y + 2, aw, RowH - 5));
                        Text(ctx, Num(asz), x + 4, y, cw - 4, RowH, wallsA.Contains(t) ? Pal.Warn : Pal.Ask, 11.5, TextAlignment.Left, bold: wallsA.Contains(t));
                        break;
                    }
                    case Col.Price:
                    {
                        var col = t == bestBid ? Pal.Bid : t == bestAsk ? Pal.Ask : Pal.Muted;
                        // A line through this price: clear a backing behind the number so it reads cleanly.
                        if (lineRows.Contains(t) && t != last)
                        {
                            ctx.FillRectangle(Pal.B(Pal.Bg), new Rect(x + cw * 0.12, y + 1, cw * 0.76, RowH - 2));
                            if (inVa) ctx.FillRectangle(Pal.B(Pal.VaBand, 15 / 255.0), new Rect(x + cw * 0.12, y + 1, cw * 0.76, RowH - 2));
                        }
                        if (t == last) ctx.DrawRectangle(Pal.B(Pal.LastBox), new Pen(Pal.B(Pal.Warn), 1.5), new Rect(x + 1, y + 1, cw - 2, RowH - 3));
                        if (t == avgRow) ctx.DrawRectangle(null, new Pen(Pal.B(Pal.Warn), 1, new DashStyle(new double[] { 3, 2 }, 0)), new Rect(x + 1, y + 1, cw - 2, RowH - 3));
                        Text(ctx, (t * s.TickSize).ToString("F" + dec, CultureInfo.InvariantCulture), x, y, cw, RowH, t == last ? Colors.White : col, 11.5, TextAlignment.Center);
                        break;
                    }
                    case Col.Sold when sold.TryGetValue(t, out var sv) && sv > 0:
                    {
                        var lit = imb.Sell.Contains(t);
                        Clear(t, Num(sv), x, y, cw - 4, 10.5, TextAlignment.Right, lit);
                        if (lit) ctx.FillRectangle(Pal.B(Pal.Ask, imb.SellStack.Contains(t) ? 0.45 : 0.25), new Rect(x + 1, y + 1, cw - 2, RowH - 3));
                        Text(ctx, Num(sv), x, y, cw - 4, RowH, lit ? Colors.White : Pal.Ask, 10.5, TextAlignment.Right, bold: lit);
                        break;
                    }
                    case Col.Bought when bought.TryGetValue(t, out var bv) && bv > 0:
                    {
                        var lit = imb.Buy.Contains(t);
                        Clear(t, Num(bv), x + 4, y, cw - 4, 10.5, TextAlignment.Left, lit);
                        if (lit) ctx.FillRectangle(Pal.B(Pal.Bid, imb.BuyStack.Contains(t) ? 0.45 : 0.25), new Rect(x + 1, y + 1, cw - 2, RowH - 3));
                        Text(ctx, Num(bv), x + 4, y, cw - 4, RowH, lit ? Colors.White : Pal.Bid, 10.5, TextAlignment.Left, bold: lit);
                        break;
                    }
                    case Col.Pnl when s.PosQty != 0:
                    {
                        var pv = (t * s.TickSize - s.PosAvg) * s.PosQty * s.PointValue;
                        Clear(t, Usd(pv), x, y, cw - 4, 10.5, TextAlignment.Right);
                        Text(ctx, Usd(pv), x, y, cw - 4, RowH, pv >= 0 ? Pal.Pos : Pal.Neg, 10.5, TextAlignment.Right);
                        break;
                    }
                    case Col.Heat:
                        DrawHeat(ctx, t, g, x, y, cw, heatScale, ind);
                        break;
                    // PROFILE — BookDepthIQ ladder.ts: neutral GREY volume histogram (Bookmap/Sierra convention).
                    // POC brightest (near-white), value area (70%) mid-grey, outside VA dim-grey. Not buy/sell
                    // coloured: buy/sell imbalance is the Δ column; a profile is total volume at price.
                    case Col.Profile when vol.TryGetValue(t, out var vv) && vv > 0:
                    {
                        var wv = Math.Min(1, vv / Math.Max(1, prof.MaxVol)) * (cw - 2);
                        var fill = t == prof.Poc ? Pal.B(Pal.ProfPoc, 0.95) : inVa ? Pal.B(Pal.ProfVa, 0.85) : Pal.B(Pal.ProfBar, 0.5);
                        ctx.FillRectangle(fill,new Rect(x + 1, y + 0.5, wv, RowH - 1));
                        break;
                    }
                    // Δ — BookDepthIQ ladder.ts: signed bar from the column centre, cyan for +, orange for −,
                    // scaled to the largest |Δ| row.
                    case Col.Delta when delta.TryGetValue(t, out var dv) && dv != 0:
                    {
                        var cx = x + cw / 2;
                        var dw = Math.Min(1, Math.Abs(dv) / Math.Max(1, prof.MaxAbsDelta)) * (cw / 2 - 1);
                        var fill = Pal.B(dv >= 0 ? Pal.DeltaUp : Pal.DeltaDn, 0.8);
                        ctx.FillRectangle(fill,new Rect(dv >= 0 ? cx : cx - dw, y + 0.5, dw, RowH - 1));
                        break;
                    }
                }
            }

            // Several orders on one side of a row draw as one chip: total contracts, ×count.
            foreach (var grp in orders.Where(o => Row(Book.ToTicks(o.Price, s.TickSize)) == t).GroupBy(o => o.IsBuy))
            {
                if (ColRect(grp.Key ? BuyCol : SellCol) is not (double x, double cw)) continue;
                var o = grp.First();
                var n = grp.Count();
                var col = o.IsTarget ? Pal.Pos : o.IsStopLeg ? Pal.Neg : o.IsBuy ? Pal.Bid : Pal.Ask;
                var pen = o.IsStop ? new Pen(Pal.B(col), 1, new DashStyle(new double[] { 3, 2 }, 0)) : new Pen(Pal.B(col), 1);
                ctx.DrawRectangle(Pal.B(col, grp.Any(g => dragGroup.Any(d => d.Id == g.Id)) ? 0.08 : 0.22), pen, new Rect(x + 2, y + 1, cw - 4, RowH - 3));
                var label = n > 1 ? (grp.All(g => g.IsStop) ? "STP" : grp.All(g => !g.IsStop) ? "LMT" : "ORD")
                    : o.Name.StartsWith("BDIQ TP", StringComparison.Ordinal) || o.Name.StartsWith("BDIQ SL", StringComparison.Ordinal) ? o.Name[5..] : o.IsStop ? "STP" : "LMT";
                Text(ctx, n > 1 ? $"{label} {grp.Sum(g => g.Left)} ×{n}" : $"{label} {o.Left}", x, y, cw, RowH, col, 10, TextAlignment.Center, bold: true);
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

        // Off-screen markers: right-aligned in the Δ column (the far right), top for above, bottom for
        // below, nearest first — never over BUY or the heatmap. On-screen tags that would land inside a
        // pin stack step left of it.
        var pinRight = (ColRect(Col.Delta) is (double dcx, double dcw) ? dcx + dcw : w) - 4;
        double upY = RowH + 9, downY = Bounds.Height - 9;
        double upLeft = pinRight, downLeft = pinRight;
        foreach (var (l, above) in offs.OrderBy(o => Math.Abs(Row(o.L.Ticks) - (o.Above ? top : bottom))).Take(10))
        {
            var dist = Math.Abs(Row(l.Ticks) - (above ? top : bottom)) / g;
            var left = DrawTag(ctx, $"{(above ? "▲" : "▼")} {l.Label} {(l.Ticks * s.TickSize).ToString("F" + dec, CultureInfo.InvariantCulture)} · {dist}", pinRight, above ? upY : downY, l.Color, right: true);
            if (above) { upLeft = Math.Min(upLeft, left); upY += 15; } else { downLeft = Math.Min(downLeft, left); downY -= 15; }
        }
        var placed = new List<(double Y, double Left)>();
        foreach (var (l, ly) in onScreen)
        {
            var x = ly <= upY - 8 ? upLeft - 4 : ly >= downY + 8 ? downLeft - 4 : w - 4;
            // Two tags on (nearly) the same row sit side by side, never on top of each other.
            foreach (var (py, pl) in placed) if (Math.Abs(py - ly) < 14) x = Math.Min(x, pl - 4);
            placed.Add((ly, DrawTag(ctx, l.Label, x, ly, l.Color, right: true)));
        }

        foreach (var (_, x, _) in cols.Skip(1)) ctx.FillRectangle(Pal.B(Pal.Line), new Rect(x, 0, 1, Bounds.Height));
        DrawZoomBadge(ctx);
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
        // The PULL/STACK / ABS read needs ~30 px; a column dragged narrower shows the heat only, so the
        // read never spills into the next column.
        var readW = cw >= 60 ? 30 : 0;
        var cellW = Math.Max(0, cw - readW) / h.Length;
        for (var i = 0; i < h.Length; i++)
        {
            var v = h[i];
            if (v == 0) continue;
            var a = Math.Min(1, Math.Abs(v) / p90);
            // Yellow = a wall: 3× the typical (median-ish) size showing, as in the Vest panel.
            var c = Math.Abs(v) >= 3 * p90 / 2 ? Pal.HeatHot : v > 0 ? Pal.HeatBid : Pal.HeatAsk;
            ctx.FillRectangle(Pal.B(c, 0.15 + 0.75 * a), new Rect(x + 1 + i * cellW, y + 1, Math.Max(1, cellW - 0.5), RowH - 3));
        }
        if (readW == 0) return;
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

    /// <summary>A boxed label; returns its left edge.</summary>
    private static double DrawTag(DrawingContext ctx, string text, double x, double y, Color c, bool right)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Tf, 9.5, Pal.B(c));
        var bx = right ? x - ft.Width - 6 : x;
        ctx.DrawRectangle(Pal.B(Pal.Bg, 0.92), new Pen(Pal.B(c, 0.8), 1), new Rect(bx, y - 7, ft.Width + 6, 14), 2, 2);
        ctx.DrawText(ft, new Point(bx + 3, y - ft.Height / 2));
        return bx;
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

    /// <summary>Every working order drawn in this BUY or SELL cell.</summary>
    private List<WorkingOrder> OrdersAt(long row, Col col)
    {
        var buyCol = col == BuyCol;
        if (!buyCol && col != SellCol) return new();
        return s.OrdersOnScreen.Where(o => Row(Book.ToTicks(o.Price, s.TickSize)) == row && o.IsBuy == buyCol).ToList();
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
        if (resizeAt != null) { ResizeTo(p.X); return; }
        if (p.Y < RowH)
        {
            hoverRow = null;
            Cursor = DividerAt(p.X) != null ? new Cursor(StandardCursorType.SizeWestEast) : ColAt(p.X) == Col.Heat ? new Cursor(StandardCursorType.Hand) : new Cursor(StandardCursorType.Arrow);
            InvalidateVisual();
            return;
        }
        if (zoomDragY is double z)
        {
            // Drag on PRICE: every 22 px up zooms in one step, down zooms out.
            var d = z - p.Y;
            if (Math.Abs(d) >= 22) { Zoom(Math.Sign(d), p.Y); zoomDragY = p.Y; }
            return;
        }
        hoverRow = RowAt(p.Y);
        hoverCol = ColAt(p.X);
        Cursor = hoverCol == Col.Price ? new Cursor(StandardCursorType.SizeNorthSouth) : new Cursor(StandardCursorType.Cross);
        if (dragOrder != null || dragPreview != null) dragTo = hoverRow;
        InvalidateVisual();
        base.OnPointerMoved(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        if (p.Y < RowH)
        {
            if (DividerAt(p.X) is int d)
            {
                if (e.ClickCount >= 2)
                {
                    // Double-click a divider: every column back to its default width.
                    cfg.ColWidths.Clear();
                    cfg.Save();
                    InvalidateVisual();
                    return;
                }
                resizeAt = d;
                e.Pointer.Capture(this);
            }
            else if (ColAt(p.X) == Col.Heat)
            {
                // Click the heat header: the window steps 5 → 15 → 30 → 60 min (a new window starts empty).
                var w = new[] { 5, 15, 30, 60 };
                cfg.HeatMinutes = w[(Array.IndexOf(w, cfg.HeatMinutes) + 1) % w.Length];
                cfg.Save();
                s.SetHeatWindow(cfg.HeatMinutes);
                InvalidateVisual();
            }
            return;
        }
        var row = RowAt(p.Y);
        var col = ColAt(p.X);
        if (col == Col.Price)
        {
            if (e.ClickCount >= 2) { zoomDragY = null; ResetZoom(); return; }
            zoomDragY = p.Y;
            e.Pointer.Capture(this);
            return;
        }
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        var here = OrdersAt(row, col);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            // Right-click an order: cancel every order on that side at that price.
            foreach (var x in here) CancelRequested?.Invoke(x);
            return;
        }
        if (here.Count > 0)
        {
            // Pick the orders up: dragged to another row they move; released on their own row it is a
            // plain click, which places another order there (NT SuperDOM style).
            dragOrder = here[0];
            dragGroup = here;
            dragShift = shift;
            dragTo = row;
            e.Pointer.Capture(this);
            return;
        }
        if (PreviewAt(row, col) is int k) { dragPreview = k; dragTo = row; e.Pointer.Capture(this); return; }
        if (col is Col.Buy or Col.Sold or Col.Bid) PlaceRequested?.Invoke(true, row, shift);
        else if (col is Col.Ask or Col.Bought or Col.Sell) PlaceRequested?.Invoke(false, row, shift);
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (resizeAt != null)
        {
            resizeAt = null;
            e.Pointer.Capture(null);
            cfg.Save();
        }
        else if (zoomDragY != null)
        {
            zoomDragY = null;
            e.Pointer.Capture(null);
        }
        else if (dragOrder != null)
        {
            var o = dragOrder;
            var group = dragGroup;
            var to = dragTo;
            dragOrder = null;
            dragGroup = new();
            dragTo = null;
            e.Pointer.Capture(null);
            var home = Row(Book.ToTicks(o.Price, s.TickSize));
            if (to is null || to == home) PlaceRequested?.Invoke(o.IsBuy, home, dragShift); // a click: add another order here
            else foreach (var x in group) MoveRequested?.Invoke(x, to.Value);
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
        var p = e.GetPosition(this);
        var steps = Math.Sign(e.Delta.Y);
        if (steps == 0) return;
        // Over PRICE (or with Ctrl): zoom around the price under the mouse. Elsewhere: scroll;
        // Shift scrolls a page.
        if (ColAt(p.X) == Col.Price || e.KeyModifiers.HasFlag(KeyModifiers.Control)) Zoom(steps, p.Y);
        else
        {
            var rows = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? Math.Max(1, Rows - 2) : 3;
            center = (center ?? Mid) + steps * rows * G;
            Hold();
        }
        InvalidateVisual();
        e.Handled = true;
    }

    // ───────────── zoom: row height first, then ticks per row; the price under the mouse stays put ─────────────

    /// <summary>Zoom levels, finest first: (row height px, ticks per row).</summary>
    private static readonly (double H, int G)[] Levels =
    {
        (26, 1), (22, 1), (18, 1), (15, 1), (13, 1), (16, 2), (16, 4), (16, 8), (16, 20), (16, 40), (16, 100),
    };
    private const int DefaultLevel = 2;
    private long holdUntil;
    // ── column resize: drag a divider in the header row; double-click resets all widths ──
    private int? resizeAt;          // index of the column left of the divider being dragged

    /// <summary>Divider (index of the column to its left) within 5 px of x, in the header row.</summary>
    private int? DividerAt(double x)
    {
        for (var i = 0; i < cols.Length - 1; i++)
            if (Math.Abs(cols[i].X + cols[i].W - x) <= 5) return i;
        return null;
    }

    private void ResizeTo(double x)
    {
        if (resizeAt is not int i || Bounds.Width <= 0) return;
        var a = cols[i];
        var b = cols[i + 1];
        var min = 18.0;
        var newA = Math.Clamp(x - a.X, min, a.W + b.W - min);
        var newB = a.W + b.W - newA;
        // Store as weights relative to the ladder width, so sizes hold when the window changes.
        cfg.ColWidths[a.Col.ToString()] = newA / Bounds.Width;
        cfg.ColWidths[b.Col.ToString()] = newB / Bounds.Width;
        foreach (var k in cols) if (!cfg.ColWidths.ContainsKey(k.Col.ToString())) cfg.ColWidths[k.Col.ToString()] = k.W / Bounds.Width;
        InvalidateVisual();
    }
    private double? zoomDragY;
    private long badgeUntil;

    private int LevelNow()
    {
        var best = 0;
        var bestScore = double.MaxValue;
        for (var i = 0; i < Levels.Length; i++)
        {
            var score = Math.Abs(Math.Log(Levels[i].G) - Math.Log(G)) * 100 + Math.Abs(Levels[i].H - RowH);
            if (score < bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    /// <summary>A manual scroll or zoom holds the view for 15 s (unless price nears the edge).</summary>
    private void Hold() => holdUntil = Environment.TickCount64 + 15_000;

    /// <summary>dir > 0 zooms in (taller rows, fewer ticks per row), dir < 0 zooms out, anchored at y.</summary>
    public void Zoom(int dir, double y)
    {
        var from = LevelNow();
        var to = Math.Clamp(from - Math.Sign(dir), 0, Levels.Length - 1);
        if (to == from) return;
        y = Math.Clamp(y, RowH, Math.Max(RowH, Bounds.Height - 1));
        var anchor = RowAt(y);
        cfg.RowHeight = Levels[to].H;
        cfg.Group = Levels[to].G;
        // Put the anchor price back under the mouse at the new scale.
        var k = (long)Math.Floor((y - RowH) / RowH);
        var top = LadderMath.BucketOf(anchor, G) + k * G;
        center = top - Rows / 2 * G;
        Hold();
        badgeUntil = Environment.TickCount64 + 1500;
        cfg.Save();
        InvalidateVisual();
    }

    /// <summary>Back to 1 tick per row at the default height, centred on price.</summary>
    public void ResetZoom()
    {
        cfg.RowHeight = Levels[DefaultLevel].H;
        cfg.Group = Levels[DefaultLevel].G;
        holdUntil = 0;
        badgeUntil = Environment.TickCount64 + 1500;
        cfg.Save();
        Recenter();
    }

    /// <summary>The zoom badge over the price column for 1.5 s after a zoom.</summary>
    private void DrawZoomBadge(DrawingContext ctx)
    {
        if (Environment.TickCount64 > badgeUntil || ColRect(Col.Price) is not (double x, double w)) return;
        var text = $"{G} tick{(G > 1 ? "s" : "")}/row · {RowH:0}px";
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, TfBold, 10.5, Pal.B(Pal.Warn));
        var bx = x + (w - ft.Width) / 2 - 5;
        ctx.DrawRectangle(Pal.B(Pal.Bg, 0.92), new Pen(Pal.B(Pal.Warn), 1), new Rect(bx, RowH + 4, ft.Width + 10, 18), 3, 3);
        ctx.DrawText(ft, new Point(bx + 5, RowH + 4 + (18 - ft.Height) / 2));
    }
}
