using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace BookDepthIQ.Dom;

/// <summary>Two compact telemetry rows. Each stat wraps independently instead of moving a whole group.</summary>
public sealed class TelemetryBand : StackPanel
{
    public TelemetryBand(IEnumerable<Control> position,IEnumerable<Control> account,IEnumerable<Control> market)
    {
        Spacing=0;
        var primary=new WrapPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(8,4,0,3)};
        Add(primary,position);Add(primary,account);
        Children.Add(primary);
        var secondary=new WrapPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(8,3,0,4)};
        Add(secondary,market);
        Children.Add(new Border {BorderBrush=Pal.B(Pal.Line),BorderThickness=new Thickness(0,1,0,0),Child=secondary});
    }
    static void Add(WrapPanel row,IEnumerable<Control> controls)
    {
        foreach(var control in controls)
        {
            control.Margin=new Thickness(0,0,12,2);
            row.Children.Add(control);
        }
    }
}
