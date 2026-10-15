using System.Globalization;

namespace BookDepthIQ.Core.Charts;

/// <summary>Readable, tick-aligned axis graduations. No fractional prices invented between native ticks.</summary>
public static class ChartAxis
{
    public static string Format(double price,double tickSize)
    {
        if(!double.IsFinite(price)) return "—";
        int digits=0;
        if(double.IsFinite(tickSize)&&tickSize>0)
            while(digits<8 && Math.Abs(tickSize*Math.Pow(10,digits)-Math.Round(tickSize*Math.Pow(10,digits)))>1e-8) digits++;
        return price.ToString("F"+digits,CultureInfo.InvariantCulture);
    }
    public static IReadOnlyList<long> PriceTicks(double low,double high,double tickSize,double height)
    {
        if(!double.IsFinite(low)||!double.IsFinite(high)||high<=low||!double.IsFinite(tickSize)||tickSize<=0) return Array.Empty<long>();
        var target=Math.Clamp((int)(Math.Max(1,height)/65),2,14);
        var raw=(high-low)*tickSize/target;
        if(!double.IsFinite(raw)||raw<=0) return Array.Empty<long>();
        var magnitude=Math.Pow(10,Math.Floor(Math.Log10(raw)));var unit=raw/magnitude;
        var nice=(unit<=1?1:unit<=2?2:unit<=2.5?2.5:unit<=5?5:10)*magnitude;
        var step=Math.Max(1,Math.Ceiling(nice/tickSize-1e-8));
        var first=Math.Ceiling(low/step)*step;var result=new List<long>();
        for(var value=first;value<=high&&result.Count<128;value+=step)
        {if(value>=long.MinValue&&value<long.MaxValue) result.Add((long)Math.Round(value));else break;}
        return result;
    }
}
