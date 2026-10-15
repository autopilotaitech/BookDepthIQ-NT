using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

/// <summary>
/// The tape, drawn like the Vest panel's (web/styles.css .trow): time | price | size, 16 px rows coloured by
/// the aggressor. A big print (≥ your big-trade size) is bold white on its side's tint with a 4 px bar on the
/// left and a size bar behind "B 12" / "S 12"; a huge one (≥ 3×) is brighter and taller. A big print flashes
/// for 1.2 s when it arrives. Wheel scrolls back; it snaps to the newest on the next print when at the top.
/// </summary>
public sealed class TapeControl : Control
{
    private static Color BigBuy => Pal.BigBuy;
    private static Color BigSell => Pal.BigSell;
    private readonly DomState s;
    private readonly Settings cfg;
    private int scroll;
    private readonly Dictionary<Trade, long> firstSeen = new(ReferenceEqualityComparer.Instance);
    private long lastCleanup;

    public TapeControl(DomState state, Settings settings)
    {
        s = state;
        cfg = settings;
        ClipToBounds = true;
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        ctx.FillRectangle(Pal.B(Pal.Bg), new Rect(Bounds.Size));
        var ind = cfg.IndicatorsFor(s.Instrument);
        var dec = LadderControl.Decimals(s.TickSize);
        var now = Environment.TickCount64;
        double y = 0;
        var c1 = w * 1 / 2.8; // grid 1fr 1fr 0.8fr
        var c2 = w * 2 / 2.8;
        // Text size is the user's (Settings › Tape & log, or Ctrl+wheel here); rows scale with it.
        var scale = cfg.TapeFont / Settings.DefaultTapeFont;
        var min = Math.Max(1, cfg.TapeMinSize);
        foreach (var t in Shown().Skip(scroll))
        {
            if (y > Bounds.Height) break;
            if (!firstSeen.ContainsKey(t)) firstSeen[t] = scroll == 0 ? now : 0;
            var big = ind.BigOn && t.Size >= ind.BigMin;
            var huge = big && t.Size >= 3 * ind.BigMin;
            var h = Math.Round((huge ? 24 : big ? 20 : 16) * scale);
            var side = t.Side == 'b' ? 1 : t.Side == 's' ? -1 : 0;
            var col = side > 0 ? Pal.Bid : side < 0 ? Pal.Ask : Pal.Muted;
            if (big && side != 0)
            {
                var tint = side > 0 ? BigBuy : BigSell;
                var age = now - firstSeen[t];
                var flash = age < 1200 ? 0.35 * (1 - age / 1200.0) : 0;
                ctx.FillRectangle(Pal.B(tint, (huge ? 0.42 : 0.2) + flash), new Rect(0, y, w, h));
                ctx.FillRectangle(Pal.B(tint), new Rect(0, y, 4, h));
                var barW = Math.Min(1, t.Size / (4.0 * ind.BigMin)) * (w - c2 - 6);
                ctx.DrawRectangle(Pal.B(tint, 0.55), null, new Rect(w - 6 - barW, y + 2, barW, h - 4), 2, 2);
                col = Colors.White;
            }
            var size = (huge ? 14 : big ? 13 : 11.5) * scale;
            var bold = big;
            Text(ctx, DateTimeOffset.FromUnixTimeMilliseconds(t.TimeMs).LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture), 6, y, c1 - 6, h, col, size, TextAlignment.Left, bold);
            Text(ctx, (t.Tick * s.TickSize).ToString("F" + dec, CultureInfo.InvariantCulture), c1, y, c2 - c1, h, col, size, TextAlignment.Left, bold);
            Text(ctx, big && side != 0 ? $"{(side > 0 ? "B" : "S")} {t.Size}" : t.Size.ToString(CultureInfo.InvariantCulture), c2, y, w - c2 - 8, h, col, size, TextAlignment.Right, bold);
            y += h;
        }
        if (firstSeen.Count > 600 && now-lastCleanup>=1000)
        {
            lastCleanup=now;
            var retained=new HashSet<Trade>(s.Tape,ReferenceEqualityComparer.Instance);
            foreach (var k in firstSeen.Keys.Where(k => !retained.Contains(k)).ToList()) firstSeen.Remove(k);
        }
        if (s.Tape.Count == 0) Text(ctx, "waiting for trades…", 6, 4, w - 12, 16, Pal.Muted, 11.5, TextAlignment.Left, false);
        else if (y == 0) Text(ctx, $"no prints of {min}+ contracts yet (Settings › Tape & log)", 6, 4, w - 12, 16, Pal.Muted, 11.5, TextAlignment.Left, false);
    }

    /// <summary>The prints the tape shows: those at or above the user's minimum size.</summary>
    private IEnumerable<Trade> Shown()
    {
        var min = Math.Max(1, cfg.TapeMinSize);
        return min <= 1 ? s.Tape : s.Tape.Where(t => t.Size >= min);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            cfg.TapeFont = Settings.ClampFont(cfg.TapeFont + Math.Sign(e.Delta.Y) * 0.5);
            cfg.Save();
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        scroll = Math.Clamp(scroll - (int)Math.Round(e.Delta.Y * 3), 0, Math.Max(0, Shown().Count() - 5));
        InvalidateVisual();
        e.Handled = true;
    }

    private static readonly Typeface Tf = new(Pal.Mono);
    private static readonly Typeface TfBold = new(Pal.Mono, FontStyle.Normal, FontWeight.Bold);

    private static void Text(DrawingContext ctx, string text, double x, double y, double w, double h, Color c, double size, TextAlignment align, bool bold)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? TfBold : Tf, size, Pal.B(c)) { MaxTextWidth = Math.Max(1, w), TextAlignment = align, MaxLineCount = 1 };
        ctx.DrawText(ft, new Point(x, y + (h - ft.Height) / 2));
    }
}

/// <summary>
/// The order log, drawn like the Vest panel's (.log .olog): 11 px rows, newest first, wrapped. Clicks and what
/// was sent in the text colour, NinjaTrader's OK in teal, blocked in amber, refused and errors in bold red,
/// fills in the side's colour. Wheel scrolls.
/// </summary>
public sealed class LogControl : Control
{
    private static Color Ok => Pal.LogOk;
    private static Color Blocked => Pal.LogBlocked;
    private static Color Bad => Pal.LogBad;
    private readonly DomState s;
    private readonly Settings cfg;
    private int scroll;

    public LogControl(DomState state, Settings settings)
    {
        s = state;
        cfg = settings;
        ClipToBounds = true;
    }

    /// <summary>Order log entries and NinjaTrader fills, newest first.</summary>
    private IEnumerable<(long T, Color C, bool Bold, string Text)> Rows()
    {
        var dec = LadderControl.Decimals(s.TickSize);
        var log = s.OrderLog.Entries.Select(e => (e.T, e.Status switch
        {
            OrderLogStatus.Ok => Ok,
            OrderLogStatus.Blocked => Blocked,
            OrderLogStatus.Click => Pal.Text,
            _ => Bad,
        }, e.Status is OrderLogStatus.Refused or OrderLogStatus.Error, $"{OrderLog.Mark(e.Status)} {e.Text}"));
        var fills = s.Execs.Select(x => (x.Ms, x.Buy ? Pal.Bid : Pal.Ask, false,
            $"fill {(x.Buy ? "buy" : "sell")} {x.Qty} @ {x.Price.ToString("F" + dec, CultureInfo.InvariantCulture)} {CmeSpecs.RootOf(x.Inst)} ({(x.Name.StartsWith("BDIQ ", StringComparison.Ordinal) ? x.Name[5..] : x.Name)}) fee ${x.Fees:0.00}"));
        return log.Concat(fills).OrderByDescending(r => r.Item1);
    }

    public override void Render(DrawingContext ctx)
    {
        var w = Bounds.Width;
        ctx.FillRectangle(Pal.B(Pal.Bg), new Rect(Bounds.Size));
        double y = 1;
        foreach (var (t, c, bold, text) in Rows().Skip(scroll))
        {
            if (y > Bounds.Height) break;
            var line = $"{DateTimeOffset.FromUnixTimeMilliseconds(t).LocalDateTime:HH:mm:ss}  {text}";
            var ft = new FormattedText(line, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, bold ? TfBold : Tf, cfg.LogFont, Pal.B(c)) { MaxTextWidth = Math.Max(1, w - 12) };
            ctx.DrawText(ft, new Point(6, y + 1));
            y += ft.Height + 2;
        }
        if (s.OrderLog.Entries.Count == 0 && s.Execs.Count == 0)
            ctx.DrawText(new FormattedText("every order click, NinjaTrader's answer and each fill shows here", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Tf, 11, Pal.B(Pal.Muted)) { MaxTextWidth = Math.Max(1, w - 12) }, new Point(6, 3));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            cfg.LogFont = Settings.ClampFont(cfg.LogFont + Math.Sign(e.Delta.Y) * 0.5);
            cfg.Save();
            InvalidateVisual();
            e.Handled = true;
            return;
        }
        scroll = Math.Clamp(scroll - (int)Math.Round(e.Delta.Y * 3), 0, Math.Max(0, s.OrderLog.Entries.Count + s.Execs.Count - 3));
        InvalidateVisual();
        e.Handled = true;
    }

    private static readonly Typeface Tf = new(Pal.Mono);
    private static readonly Typeface TfBold = new(Pal.Mono, FontStyle.Normal, FontWeight.Bold);
}
