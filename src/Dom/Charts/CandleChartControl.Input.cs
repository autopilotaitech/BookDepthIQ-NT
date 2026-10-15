using Avalonia;
using Avalonia.Input;

namespace BookDepthIQ.Dom.Charts;

public sealed partial class CandleChartControl
{
    Point? drag;
    bool priceDrag;
    Rect? lastPriceTag;
    void WireInput()
    {
        PointerWheelChanged+=(_,e)=> {
            var p=e.GetPosition(this);var plot=Geometry;var factor=Math.Pow(.85,e.Delta.Y);
            if(p.X>=plot.PlotWidth || e.KeyModifiers.HasFlag(KeyModifiers.Shift)) ScalePrice(factor,Math.Clamp(p.Y/plot.PlotHeight,0,1));
            else Navigate(()=>Viewport.Zoom(factor,Math.Clamp(p.X/plot.PlotWidth,0,1),snapshot.Bars.Count));
            e.Handled=true;
        };
        PointerPressed+=(_,e)=> {
            if(!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus();var p=e.GetPosition(this);var plot=Geometry;
            if(settings.ShowLastPrice && lastPriceTag is {} tag && tag.Contains(p)) {Recenter();e.Handled=true;return;}
            if(e.ClickCount==2) {if(p.X>=plot.PlotWidth) AutoScale();else ResetView();e.Handled=true;return;}
            drag=p;priceDrag=p.X>=plot.PlotWidth;e.Pointer.Capture(this);e.Handled=true;
        };
        PointerMoved+=(_,e)=> {
            var p=e.GetPosition(this);var plot=Geometry;
            if(drag is {} previous)
            {
                if(priceDrag)
                {
                    if(e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                    {
                        var d=(p.Y-previous.Y)/plot.PlotHeight*(plot.High-plot.Low);
                        Navigate(()=>Viewport.SetPriceRange(plot.Low+d,plot.High+d));
                    }
                    else ScalePrice(Math.Exp(Math.Clamp((p.Y-previous.Y)/plot.PlotHeight*3,-2,2)),.5);
                }
                else Navigate(()=>Viewport.Pan(-(p.X-previous.X)/plot.PlotWidth*Viewport.Count,snapshot.Bars.Count));
                drag=p;
            }
            Cursor=new Cursor(p.X>=plot.PlotWidth?StandardCursorType.SizeNorthSouth:p.Y>=plot.PlotHeight?StandardCursorType.SizeWestEast:StandardCursorType.Cross);
            SetCursor(p);
        };
        PointerReleased+=(_,e)=> {drag=null;e.Pointer.Capture(null);e.Handled=true;};
        PointerCaptureLost+=(_,_)=>drag=null;
        PointerExited+=(_,_)=>SetCursor(null);
        KeyDown+=(_,e)=> {
            switch(e.Key)
            {
                case Key.Space:Recenter();break;
                case Key.Home:ResetView();break;
                case Key.End:GoLive();break;
                case Key.Left:Navigate(()=>Viewport.Pan(-10,snapshot.Bars.Count));break;
                case Key.Right:Navigate(()=>Viewport.Pan(10,snapshot.Bars.Count));break;
                case Key.Add:case Key.OemPlus:Navigate(()=>Viewport.Zoom(.8,.5,snapshot.Bars.Count));break;
                case Key.Subtract:case Key.OemMinus:Navigate(()=>Viewport.Zoom(1.25,.5,snapshot.Bars.Count));break;
                default:return;
            }
            e.Handled=true;
        };
    }
    void ScalePrice(double factor,double fraction)
    {
        var plot=Geometry;var anchor=plot.High-(plot.High-plot.Low)*fraction;
        var span=Math.Clamp((plot.High-plot.Low)*factor,4,1e12);
        Navigate(()=>Viewport.SetPriceRange(anchor-span*(1-fraction),anchor+span*fraction));
    }
}
