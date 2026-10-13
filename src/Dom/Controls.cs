using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace BookDepthIQ.Dom;

/// <summary>Flight-panel style controls for the ticket: one clear state per control, large hit areas, a
/// 4 px grid, sans labels and mono numbers.</summary>
public static class Flight
{
    public static readonly FontFamily Sans = new("Segoe UI Variable Text, Segoe UI, Inter, Arial");
    public static readonly Color CardBg = Color.Parse("#0f141d");
    public static readonly Color CardEdge = Color.Parse("#1c2433");
    public static readonly Color Field = Color.Parse("#0a0e15");
    public static readonly Color Accent = Color.Parse("#38bdf8");
    public static readonly Color SegOn = Color.Parse("#16324a");

    /// <summary>A section card: SMALL CAPS title, then its content.</summary>
    public static Control Card(string title, Control content)
    {
        var sp = new StackPanel { Spacing = 6 };
        sp.Children.Add(new TextBlock { Text = title, FontFamily = Sans, FontSize = 10, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.2, Foreground = Pal.B(Pal.Muted) });
        sp.Children.Add(content);
        return new Border
        {
            Background = Pal.B(CardBg), BorderBrush = Pal.B(CardEdge), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8), Margin = new Thickness(8, 4), Child = sp,
        };
    }

    /// <summary>A labelled field: the label small above, the control below.</summary>
    public static Control Labeled(string label, Control c, double width = double.NaN)
    {
        var sp = new StackPanel { Spacing = 2, Width = width };
        sp.Children.Add(new TextBlock { Text = label, FontFamily = Sans, FontSize = 9.5, Foreground = Pal.B(Pal.Muted) });
        sp.Children.Add(c);
        return sp;
    }

    /// <summary>A number box: mono, right-aligned, roomy.</summary>
    public static TextBox Num(double width, string? tip = null)
    {
        var t = new TextBox
        {
            Width = width, Height = 30, MinHeight = 30, FontFamily = Pal.Mono, FontSize = 14, Padding = new Thickness(8, 4),
            Background = Pal.B(Field), BorderBrush = Pal.B(CardEdge), CornerRadius = new CornerRadius(4),
            HorizontalContentAlignment = HorizontalAlignment.Right, VerticalContentAlignment = VerticalAlignment.Center,
        };
        if (tip != null) ToolTip.SetTip(t, tip);
        return t;
    }

    /// <summary>A text box (setup tag).</summary>
    public static TextBox Text(double width, string? tip = null)
    {
        var t = Num(width, tip);
        t.FontFamily = Sans;
        t.FontSize = 13;
        t.HorizontalContentAlignment = HorizontalAlignment.Left;
        return t;
    }

    /// <summary>Restyles a status/read badge (Ui.Pill) for the header: rounded, sans, roomy.</summary>
    public static Border Badge(Border b, bool big = false)
    {
        b.CornerRadius = new CornerRadius(big ? 15 : 13);
        b.Padding = new Thickness(big ? 11 : 9, big ? 5 : 4);
        b.Margin = new Thickness(0, 0, 6, 0);
        b.VerticalAlignment = VerticalAlignment.Center;
        b.Background = Pal.B(Field);
        if (b.Child is TextBlock t) { t.FontFamily = Sans; t.FontSize = big ? 12.5 : 11.5; t.FontWeight = FontWeight.SemiBold; }
        return b;
    }

    /// <summary>A telemetry stat: small-caps label over a big mono value.</summary>
    public static (Control Box, TextBlock Value) Stat(string label, string? tip = null, double minWidth = 0)
    {
        var v = new TextBlock { FontFamily = Pal.Mono, FontSize = 15, FontWeight = FontWeight.SemiBold, Foreground = Pal.B(Pal.Text), Text = "—" };
        var sp = new StackPanel { Spacing = 1, Margin = new Thickness(0, 0, 14, 0), MinWidth = minWidth };
        sp.Children.Add(new TextBlock { Text = label, FontFamily = Sans, FontSize = 9.5, LetterSpacing = 1, Foreground = Pal.B(Pal.Muted) });
        sp.Children.Add(v);
        if (tip != null) ToolTip.SetTip(sp, tip);
        return (sp, v);
    }

    /// <summary>A telemetry group: a small title over a row of stats, with a divider on its right.</summary>
    public static Control Group(string title, params Control[] stats)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in stats) row.Children.Add(c);
        var sp = new StackPanel { Spacing = 4 };
        sp.Children.Add(new TextBlock { Text = title, FontFamily = Sans, FontSize = 9, FontWeight = FontWeight.SemiBold, LetterSpacing = 1.6, Foreground = Pal.B(Accent), Opacity = 0.8 });
        sp.Children.Add(row);
        return new Border { BorderBrush = Pal.B(CardEdge), BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(12, 6, 6, 6), Child = sp };
    }

    /// <summary>A header button: ghost (outline) or solid danger.</summary>
    public static Button HeadBtn(string text, Action click, string? tip = null, bool danger = false)
    {
        var b = new Button
        {
            Content = text, FontFamily = Sans, FontSize = 12, FontWeight = FontWeight.SemiBold, Height = 30, Padding = new Thickness(14, 0), Margin = new Thickness(0, 0, 6, 0),
            CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), VerticalContentAlignment = VerticalAlignment.Center,
            Background = Pal.B(danger ? Color.Parse("#5a1414") : Field), BorderBrush = Pal.B(danger ? Pal.Neg : CardEdge), Foreground = Pal.B(danger ? Color.Parse("#ffd6d6") : Pal.Text),
        };
        if (tip != null) ToolTip.SetTip(b, tip);
        b.Click += (_, _) => click();
        return b;
    }

    /// <summary>A pill toggle: off = outline, on = filled accent.</summary>
    public static ToggleButton Pill(string text, string? tip = null)
    {
        var b = new ToggleButton
        {
            Content = text, FontFamily = Sans, FontSize = 12, FontWeight = FontWeight.SemiBold, Height = 30, MinWidth = 64, Padding = new Thickness(12, 0),
            CornerRadius = new CornerRadius(15), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
        };
        void Paint()
        {
            var on = b.IsChecked == true;
            b.Background = Pal.B(on ? SegOn : Field);
            b.BorderBrush = Pal.B(on ? Accent : CardEdge);
            b.BorderThickness = new Thickness(1);
            b.Foreground = Pal.B(on ? Accent : Pal.Muted);
        }
        b.IsCheckedChanged += (_, _) => Paint();
        b.AttachedToVisualTree += (_, _) => Paint();
        Paint();
        if (tip != null) ToolTip.SetTip(b, tip);
        return b;
    }
}

/// <summary>
/// A segmented control: one row of equal buttons, exactly one selected. Same surface as a ComboBox
/// where the ticket uses it (SelectedIndex, SelectedItem, SelectionChanged).
/// </summary>
public sealed class Seg : Border
{
    private readonly string[] items;
    private readonly Button[] buttons;
    private int index;
    public event EventHandler? SelectionChanged;

    public Seg(string[] items, string? tip = null)
    {
        this.items = items;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(string.Join(",", items.Select(_ => "*"))) };
        buttons = items.Select((label, i) =>
        {
            var b = new Button
            {
                Content = label, FontFamily = Flight.Sans, FontSize = 12, FontWeight = FontWeight.SemiBold, Height = 28,
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(6, 0), Margin = new Thickness(1), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(0),
            };
            b.Click += (_, _) => SelectedIndex = i;
            Grid.SetColumn(b, i);
            grid.Children.Add(b);
            return b;
        }).ToArray();
        Child = grid;
        Background = Pal.B(Flight.Field);
        BorderBrush = Pal.B(Flight.CardEdge);
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(5);
        Padding = new Thickness(1);
        if (tip != null) ToolTip.SetTip(this, tip);
        Paint();
    }

    public int SelectedIndex
    {
        get => index;
        set
        {
            var v = Math.Clamp(value, 0, items.Length - 1);
            if (v == index) { Paint(); return; }
            index = v;
            Paint();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public string SelectedItem => items[index];

    private void Paint()
    {
        for (var i = 0; i < buttons.Length; i++)
        {
            var on = i == index;
            buttons[i].Background = Pal.B(on ? Flight.SegOn : Colors.Transparent);
            buttons[i].Foreground = Pal.B(on ? Flight.Accent : Pal.Muted);
        }
    }
}
