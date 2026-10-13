using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace BookDepthIQ.Dom;

/// <summary>Small builders so every window looks the same (the Vest panel's dark, mono, dense style).</summary>
public static class Ui
{
    public static TextBlock Label(string text, Color c, bool bold = false, double size = 12, bool wrap = false) =>
        new() { Text = text, Foreground = Pal.B(c), FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal, FontSize = size, VerticalAlignment = VerticalAlignment.Center, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap };

    public static TextBox Input(double w, string? tip = null)
    {
        var t = new TextBox { Width = w, Height = 24, MinHeight = 24, FontSize = 12, Padding = new Thickness(4, 2), Background = Pal.B(Pal.Bg), BorderBrush = Pal.B(Pal.Line), VerticalContentAlignment = VerticalAlignment.Center };
        if (tip != null) ToolTip.SetTip(t, tip);
        return t;
    }

    public static Button Btn(string text, Action click, string? tip = null, Color? fg = null, Color? bg = null)
    {
        var b = new Button { Content = text, FontSize = 11, Padding = new Thickness(8, 2), Height = 24, VerticalContentAlignment = VerticalAlignment.Center };
        if (fg is Color f) b.Foreground = Pal.B(f);
        if (bg is Color g) b.Background = Pal.B(g);
        if (tip != null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    public static Button BigBtn(string text, Color bg, Color fg, Action click, string? tip = null)
    {
        var b = new Button
        {
            Content = text, Background = Pal.B(bg), Foreground = Pal.B(fg), FontWeight = FontWeight.Bold, Height = 40,
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(2),
        };
        if (tip != null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    public static ComboBox Combo(double w, IEnumerable<string> items, string? tip = null)
    {
        var c = new ComboBox { Width = w, Height = 24, MinHeight = 24, FontSize = 12, ItemsSource = items.ToList(), Padding = new Thickness(6, 0) };
        if (tip != null) ToolTip.SetTip(c, tip);
        return c;
    }

    public static CheckBox Check(string text, string? tip = null)
    {
        var c = new CheckBox { Content = text, FontSize = 12, MinHeight = 24, VerticalAlignment = VerticalAlignment.Center };
        if (tip != null) ToolTip.SetTip(c, tip);
        return c;
    }

    public static Control Row(params Control[] items)
    {
        var p = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 3) };
        foreach (var c in items) { c.Margin = new Thickness(0, 1, 8, 1); p.Children.Add(c); }
        return p;
    }

    public static Control Bar(params Control[] items) =>
        new Border { Background = Pal.B(Pal.Panel), BorderBrush = Pal.B(Pal.Line), BorderThickness = new Thickness(0, 0, 0, 1), Child = Row(items) };

    public static Control Pad(Control c, double h = 8, double v = 2) => new Border { Padding = new Thickness(h, v), Child = c };

    /// <summary>A labelled value box for the position strip (LABEL over value).</summary>
    public static (Control Box, TextBlock Value) Stat(string label, string? tip = null)
    {
        var v = new TextBlock { FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = Pal.B(Pal.Text) };
        var sp = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        sp.Children.Add(new TextBlock { Text = label, FontSize = 9.5, Foreground = Pal.B(Pal.Muted) });
        sp.Children.Add(v);
        if (tip != null) ToolTip.SetTip(sp, tip);
        return (sp, v);
    }

    public static Border Pill(string text, Color c, string? tip = null)
    {
        var b = new Border { BorderBrush = Pal.B(c), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1), Child = new TextBlock { Text = text, Foreground = Pal.B(c), FontSize = 11, FontWeight = FontWeight.SemiBold } };
        if (tip != null) ToolTip.SetTip(b, tip);
        return b;
    }

    public static void SetPill(Border b, string text, Color c, string? tip = null)
    {
        b.BorderBrush = Pal.B(c);
        if (b.Child is TextBlock t) { t.Text = text; t.Foreground = Pal.B(c); }
        if (tip != null) ToolTip.SetTip(b, tip);
    }

    public static double Num(TextBox t, double dflt) => double.TryParse(t.Text?.Replace(",", "").Replace("$", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : dflt;
    public static string F(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
