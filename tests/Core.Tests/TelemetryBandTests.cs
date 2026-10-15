using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using BookDepthIQ.Dom;
using Xunit;

public class TelemetryBandTests
{
    static Control Stat(string label,string value,double width=0)
    {var stat=Flight.Stat(label,null,width);stat.Value.Text=value;return stat.Box;}
    static TelemetryBand Band()=>new(
        new[]{Stat("POSITION","flat",70),Stat("AVG","—",80),Stat("OPEN P&L","$0.00",90),Stat("FAIL PRICE","—",80),Stat("TO FLOOR","—",64)},
        new[]{Stat("EQUITY","$108,290",96),Stat("ROOM","$60,790",90),Stat("TODAY","+$6,285 · 9 trades",96),Stat("REALIZED","+$8,290",80)},
        new[]{Stat("POC · VA","30142.00 · 30122.00–30174.00"),Stat("CUM Δ","+5883",56),Stat("ST1 · ST2","▼ 30123.00 · ▼ 30129.00"),Stat("VWAP","29912.42 +1.5σ"),Stat("BID / ASK LIQ","54% / 46%"),Stat("GEX","−γ $11.7B"),Stat("IB","60m ▲ 09:41 Δ +2,082")});

    [AvaloniaTheory] [InlineData(1280)] [InlineData(1380)] [InlineData(1720)]
    public void Typical_dom_widths_fit_all_readouts_in_compact_band(int width)
    {
        var band=Band();band.Measure(new Size(width,double.PositiveInfinity));band.Arrange(new Rect(0,0,width,band.DesiredSize.Height));
        Assert.InRange(band.DesiredSize.Height,50,100);
        Assert.Equal(32,band.GetVisualDescendants().OfType<TextBlock>().Count());
        foreach(var value in band.GetVisualDescendants().OfType<TextBlock>())
        {
            var point=value.TranslatePoint(default,band)!.Value;
            Assert.True(point.X>=0 && point.X+value.Bounds.Width<=width+1,$"Clipped {value.Text} at {point.X}+{value.Bounds.Width}");
        }
        if(Environment.GetEnvironmentVariable("BDIQ_TELEMETRY_IMAGE") is {Length:>0} path && width==1380)
        {using var bmp=new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize(width,(int)Math.Ceiling(band.Bounds.Height)));bmp.Render(band);bmp.Save(path);}
    }
    [AvaloniaFact] public void Narrow_band_wraps_individual_readouts_without_losing_them()
    {
        var band=Band();band.Measure(new Size(760,double.PositiveInfinity));band.Arrange(new Rect(0,0,760,band.DesiredSize.Height));
        Assert.Equal(32,band.GetVisualDescendants().OfType<TextBlock>().Count());Assert.True(band.DesiredSize.Height<170);
        foreach(var stat in band.GetVisualDescendants().OfType<TextBlock>()) Assert.True(stat.Bounds.Height>0);
    }
}
