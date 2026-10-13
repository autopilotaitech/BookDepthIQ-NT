using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

/// <summary>
/// The price ladder, drawn directly (no per-cell controls): BUY | BID | PRICE | ASK | SELL | P&L |
/// VOLUME. Same behaviour as the Vest panel: click the BUY/BID side for a buy limit, the SELL/ASK
/// side for a sell limit, shift-click for a stop; click a working order to cancel it, drag it to
/// another row to move it. The ladder holds still and re-centres only when price drifts a sixth of
/// the screen (calm follow), never while the pointer is over it unless price nears the edge.
/// </summary>
public sealed class LadderControl : Control
{
    public const double RowH = 18;
    private static readonly string[] Heads = { "BUY", "BID", "PRICE", "ASK", "SELL", "P&L", "VOLUME" };
    private static readonly double[] Widths = { 0.11, 0.17, 0.15, 0.17, 0.11, 0.12, 0.17 };

    private readonly DomState s;
    private long? center;
    private bool pointerOver;
    private long? hoverTick;
    private int hoverCol = -1;
    private WorkingOrder? dragOrder;
    private long? dragTo;

    /// <summary>A click asking for an entry: (buy?, tick, stop?).</summary>
    public event Action<bool, long, bool>? PlaceRequested;
    public event Action<WorkingOrder>? CancelRequested;
    public event Action<WorkingOrder, long>? MoveRequested;

    public LadderControl(DomState state)
    {
        s = state;
        ClipToBounds = true;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    public void Recenter() { center = null; InvalidateVisual(); }

    private int Rows => Math.Max(1, (int)((Bounds.Height - RowH) / RowH));
    private long Mid => s.LastTick ?? (s.Book.BestBid is long b && s.Book.BestAsk is long a ? (a + b) / 2 : s.Book.BestBid ?? s.Book.BestAsk ?? 0);

    /// <summary>Calm follow: move only when price drifts a sixth of the screen (or near the edge while hovered).</summary>
    private long Top()
    {
        var mid = Mid;
        if (center is null) center = mid;
        var off = Math.Abs(mid - center.Value);
        var limit = pointerOver || dragOrder != null ? Rows / 2 - 3 : Math.Max(2, Rows / 6);
        if (mid != 0 && off > limit) center = mid;
        return center.Value + Rows / 2;
    }

    private (double x, double w) Col(int i)
    {
        var x = Widths.Take(i).Sum() * Bounds.Width;
        return (x, Widths[i] * Bounds.Width);
    }

    private int ColAt(double x)
    {
        var acc = 0.0;
        for (var i = 0; i < Widths.Length; i++)
        {
            acc += Widths[i] * Bounds.Width;
            if (x < acc) return i;
        }
        return Widths.Length - 1;
    }

    private long TickAt(double y) => Top() - (long)Math.Floor((y - RowH) / RowH);

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        ctx.FillRectangle(Pal.B(Pal.Bg), new Rect(Bounds.Size));
        var tf = new Typeface(Pal.Mono);
        var top = Top();
        var rows = Rows;
        var dec = Decimals(s.TickSize);

        // header
        ctx.FillRectangle(Pal.B(Pal.Panel), new Rect(0, 0, w, RowH));
        for (var i = 0; i < Heads.Length; i++)
        {
            var (x, cw) = Col(i);
            Text(ctx, tf, Heads[i], x, 0, cw, RowH, Pal.Muted, 10, TextAlignment.Center);
        }

        var book = s.Book;
        long maxSize = 1;
        foreach (var v in book.Bids.Sizes) maxSize = Math.Max(maxSize, v);
        foreach (var v in book.Asks.Sizes) maxSize = Math.Max(maxSize, v);
        long maxVol = 1;
        foreach (var v in s.Volume.Values) maxVol = Math.Max(maxVol, v.Buy + v.Sell);
        var orders = s.OrdersOnScreen.ToList();
        var avgTick = s.PosQty != 0 ? (long?)Math.Round(s.PosAvg / s.TickSize) : null;

        for (var r = 0; r < rows; r++)
        {
            var t = top - r;
            var y = RowH + r * RowH;
            ctx.FillRectangle(Pal.B(Pal.RowLine), new Rect(0, y + RowH - 1, w, 1));
            if (hoverTick == t && (hoverCol is 0 or 1 or 3 or 4))
            {
                var (hx, hw) = Col(hoverCol);
                ctx.FillRectangle(Pal.B(Pal.Hover), new Rect(hx, y, hw, RowH - 1));
            }

            // BID / ASK sizes with bars
            var bs = book.Bids.SizeAt(t);
            var asz = book.Asks.SizeAt(t);
            if (bs > 0)
            {
                var (x, cw) = Col(1);
                var bw = cw * bs / maxSize;
                ctx.FillRectangle(Pal.B(Pal.Bid, 0.28), new Rect(x + cw - bw, y + 2, bw, RowH - 5));
                Text(ctx, tf, bs.ToString(CultureInfo.InvariantCulture), x, y, cw - 4, RowH, Pal.Bid, 12, TextAlignment.Right);
            }
            if (asz > 0)
            {
                var (x, cw) = Col(3);
                var aw = cw * asz / maxSize;
                ctx.FillRectangle(Pal.B(Pal.Ask, 0.28), new Rect(x, y + 2, aw, RowH - 5));
                Text(ctx, tf, asz.ToString(CultureInfo.InvariantCulture), x + 4, y, cw - 4, RowH, Pal.Ask, 12, TextAlignment.Left);
            }

            // PRICE: best bid/ask coloured, last price = yellow box, avg = dashed amber outline
            {
                var (x, cw) = Col(2);
                var col = t == book.BestBid ? Pal.Bid : t == book.BestAsk ? Pal.Ask : Pal.Muted;
                if (t == s.LastTick) ctx.DrawRectangle(Pal.B(Color.Parse("#2a3142")), new Pen(Pal.B(Pal.Warn), 1.5), new Rect(x + 1, y + 1, cw - 2, RowH - 3));
                if (t == avgTick) ctx.DrawRectangle(null, new Pen(Pal.B(Pal.Warn), 1, new DashStyle(new double[] { 3, 2 }, 0)), new Rect(x + 1, y + 1, cw - 2, RowH - 3));
                Text(ctx, tf, (t * s.TickSize).ToString("F" + dec, CultureInfo.InvariantCulture), x, y, cw, RowH, t == s.LastTick ? Colors.White : col, 12, TextAlignment.Center);
            }

            // BUY / SELL: working orders as chips
            foreach (var o in orders)
            {
                if (Book.ToTicks(o.Price, s.TickSize) != t) continue;
                var (x, cw) = Col(o.IsBuy ? 0 : 4);
                var c = o.IsBuy ? Pal.Bid : Pal.Ask;
                var dragging = dragOrder?.Id == o.Id;
                var pen = o.IsStop ? new Pen(Pal.B(c), 1, new DashStyle(new double[] { 3, 2 }, 0)) : new Pen(Pal.B(c), 1);
                ctx.DrawRectangle(Pal.B(c, dragging ? 0.1 : 0.22), pen, new Rect(x + 2, y + 1, cw - 4, RowH - 3));
                var label = $"{(o.IsStop ? "STP" : o.Name == "BDIQ target" ? "TP" : "LMT")} {o.Qty - o.Filled}";
                Text(ctx, tf, label, x, y, cw, RowH, c, 11, TextAlignment.Center, bold: true);
            }
            if (dragOrder != null && dragTo == t)
            {
                var (x, cw) = Col(dragOrder.IsBuy ? 0 : 4);
                ctx.DrawRectangle(null, new Pen(Pal.B(Pal.Warn), 1, new DashStyle(new double[] { 3, 2 }, 0)), new Rect(x + 2, y + 1, cw - 4, RowH - 3));
            }

            // P&L at this price for the open position
            if (s.PosQty != 0)
            {
                var (x, cw) = Col(5);
                var pnl = (t * s.TickSize - s.PosAvg) * s.PosQty * s.PointValue;
                Text(ctx, tf, Usd(pnl), x, y, cw - 4, RowH, pnl >= 0 ? Pal.Pos : Pal.Neg, 11, TextAlignment.Right);
            }

            // VOLUME profile (buy/sell split)
            if (s.Volume.TryGetValue(t, out var vol) && vol.Buy + vol.Sell > 0)
            {
                var (x, cw) = Col(6);
                var tot = vol.Buy + vol.Sell;
                var full = (cw - 30) * tot / maxVol;
                var bw = full * vol.Buy / tot;
                ctx.FillRectangle(Pal.B(Pal.Bid, 0.45), new Rect(x + 2, y + 3, bw, RowH - 7));
                ctx.FillRectangle(Pal.B(Pal.Ask, 0.45), new Rect(x + 2 + bw, y + 3, full - bw, RowH - 7));
                Text(ctx, tf, tot.ToString(CultureInfo.InvariantCulture), x, y, cw - 3, RowH, Pal.Muted, 10, TextAlignment.Right);
            }
        }
        // column dividers
        for (var i = 1; i < Widths.Length; i++)
        {
            var (x, _) = Col(i);
            ctx.FillRectangle(Pal.B(Pal.Line), new Rect(x, 0, 1, Bounds.Height));
        }
    }

    private static void Text(DrawingContext ctx, Typeface tf, string text, double x, double y, double w, double h, Color c, double size, TextAlignment align, bool bold = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? new Typeface(tf.FontFamily, FontStyle.Normal, FontWeight.Bold) : tf, size, Pal.B(c))
        {
            MaxTextWidth = Math.Max(1, w),
            TextAlignment = align,
        };
        ctx.DrawText(ft, new Point(x, y + (h - ft.Height) / 2));
    }

    public static string Usd(double v) => (v < 0 ? "-$" : "+$") + Math.Abs(v).ToString(Math.Abs(v) >= 1000 ? "N0" : "N2", CultureInfo.InvariantCulture);
    public static int Decimals(double tick)
    {
        var s = tick.ToString("0.########", CultureInfo.InvariantCulture);
        var i = s.IndexOf('.');
        return i < 0 ? 0 : s.Length - i - 1;
    }

    // ───────────── input ─────────────

    private WorkingOrder? OrderAt(long t, int col)
    {
        if (col is not (0 or 4)) return null;
        return s.OrdersOnScreen.FirstOrDefault(o => Book.ToTicks(o.Price, s.TickSize) == t && (col == 0) == o.IsBuy);
    }

    protected override void OnPointerEntered(PointerEventArgs e) { pointerOver = true; base.OnPointerEntered(e); }
    protected override void OnPointerExited(PointerEventArgs e)
    {
        pointerOver = false;
        hoverTick = null;
        InvalidateVisual();
        base.OnPointerExited(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (p.Y < RowH) { hoverTick = null; InvalidateVisual(); return; }
        hoverTick = TickAt(p.Y);
        hoverCol = ColAt(p.X);
        if (dragOrder != null) dragTo = hoverTick;
        InvalidateVisual();
        base.OnPointerMoved(e);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        if (p.Y < RowH) return;
        var t = TickAt(p.Y);
        var col = ColAt(p.X);
        var o = OrderAt(t, col);
        if (o != null)
        {
            dragOrder = o;
            dragTo = t;
            e.Pointer.Capture(this);
            return;
        }
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (col is 0 or 1) PlaceRequested?.Invoke(true, t, shift);
        else if (col is 3 or 4) PlaceRequested?.Invoke(false, t, shift);
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
            if (to is null || to == Book.ToTicks(o.Price, s.TickSize)) CancelRequested?.Invoke(o); // dropped on its own row = cancel
            else MoveRequested?.Invoke(o, to.Value);
            InvalidateVisual();
        }
        base.OnPointerReleased(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        center = (center ?? Mid) + (long)Math.Round(e.Delta.Y * 3);
        InvalidateVisual();
        e.Handled = true;
    }
}
