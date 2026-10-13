using Avalonia.Media;

namespace BookDepthIQ.Dom;

/// <summary>The Vest panel's colour tokens (web/styles.css :root), so both DOMs look the same.</summary>
public static class Pal
{
    public static readonly Color Bg = Color.Parse("#0b0e13");
    public static readonly Color Panel = Color.Parse("#121722");
    public static readonly Color Line = Color.Parse("#1f2633");
    public static readonly Color Text = Color.Parse("#d7dde8");
    public static readonly Color Muted = Color.Parse("#7b8597");
    public static readonly Color Bid = Color.Parse("#22d3ee");
    public static readonly Color Ask = Color.Parse("#fb923c");
    public static readonly Color Pos = Color.Parse("#34d399");
    public static readonly Color Neg = Color.Parse("#f87171");
    public static readonly Color Warn = Color.Parse("#fbbf24");
    public static readonly Color RowLine = Color.Parse("#10141c");
    public static readonly Color Hover = Color.Parse("#1a2130");
    public static readonly Color BuyBtn = Color.Parse("#0e3a44");
    public static readonly Color SellBtn = Color.Parse("#44250e");

    public static IBrush B(Color c) => new SolidColorBrush(c);
    public static IBrush B(Color c, double opacity) => new SolidColorBrush(c, opacity);
    public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Courier New");
}
