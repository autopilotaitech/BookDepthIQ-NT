namespace BookDepthIQ.Core.Charts;

public static class ChartFrame
{
    public static readonly int[] Choices={1,3,5,15,30,60,240,1440};
    public static bool SupportsPax(int minutes)=>minutes is 1 or 3 or 5 or 15 or 30;
    public static int DefaultBars(int minutes)=>minutes switch {1=>3000,3 or 5=>2000,15 or 30 or 60=>1500,240=>1000,1440=>500,_=>throw new ArgumentOutOfRangeException(nameof(minutes))};
    public static string Label(int minutes)=>minutes switch {60=>"1h",240=>"4h",1440=>"Day",_=>minutes+"m"};
}
