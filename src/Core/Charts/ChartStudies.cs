namespace BookDepthIQ.Core.Charts;

public static class ChartStudies
{
    public static double?[] Sma(IReadOnlyList<double> values,int period)
    {
        period=Math.Clamp(period,1,500);var result=new double?[values.Count];double sum=0;
        for(int i=0;i<values.Count;i++) {sum+=values[i];if(i>=period) sum-=values[i-period];if(i>=period-1) result[i]=sum/period;}
        return result;
    }
    public static double?[] Ema(IReadOnlyList<double> values,int period)
    {
        period=Math.Clamp(period,1,500);var result=new double?[values.Count];double sum=0,ema=0,alpha=2.0/(period+1);
        for(int i=0;i<values.Count;i++) {if(i<period) sum+=values[i];if(i==period-1) ema=sum/period;else if(i>=period) ema+=alpha*(values[i]-ema);if(i>=period-1) result[i]=ema;}
        return result;
    }
}
