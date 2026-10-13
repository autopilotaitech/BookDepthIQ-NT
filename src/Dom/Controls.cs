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
