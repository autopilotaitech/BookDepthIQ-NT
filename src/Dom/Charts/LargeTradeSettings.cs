using Avalonia.Media;
namespace BookDepthIQ.Dom.Charts;

public enum TradeQuantityLabels {Off,Large,All}
public sealed record LargeTradeProfile
{
    public long MinContracts {get;set;}=50;
    public string BuyColor {get;set;}="#00C8F4";
    public string SellColor {get;set;}="#FF8B22";
    public string UnknownColor {get;set;}="#8C9EB0";
    public double Opacity {get;set;}=.4;
    public double MinDiameter {get;set;}=24;
    public double MaxDiameter {get;set;}=48;
    public TradeQuantityLabels Labels {get;set;}=TradeQuantityLabels.All;
    public int ReadabilityVersion {get;set;}
    public static bool ValidColor(string? value)=>value is {Length:7}&&value[0]=='#'&&value.Skip(1).All(Uri.IsHexDigit);
    public void Normalize()
    {
        if(ReadabilityVersion<1)
        {
            if(MinDiameter==12&&MaxDiameter==42&&Opacity==.2&&Labels==TradeQuantityLabels.Large)
            {MinDiameter=24;MaxDiameter=48;Opacity=.4;Labels=TradeQuantityLabels.All;}
            ReadabilityVersion=1;
        }
        MinContracts=Math.Clamp(MinContracts,1,1000000);
        BuyColor=ValidColor(BuyColor)?BuyColor.ToUpperInvariant():"#00C8F4";
        SellColor=ValidColor(SellColor)?SellColor.ToUpperInvariant():"#FF8B22";
        UnknownColor=ValidColor(UnknownColor)?UnknownColor.ToUpperInvariant():"#8C9EB0";
        Opacity=double.IsFinite(Opacity)?Math.Clamp(Opacity,0,1):.4;
        MinDiameter=double.IsFinite(MinDiameter)?Math.Clamp(MinDiameter,6,80):24;
        MaxDiameter=double.IsFinite(MaxDiameter)?Math.Clamp(MaxDiameter,MinDiameter,80):Math.Max(MinDiameter,48);
        if(!Enum.IsDefined(Labels)) Labels=TradeQuantityLabels.All;
    }
    public double Diameter(long quantity)=>Math.Clamp(MinDiameter*Math.Sqrt(Math.Max(0,quantity)/(double)Math.Max(1,MinContracts)),MinDiameter,MaxDiameter);
    public Color ColorFor(char side)=>Color.Parse(side=='b'?BuyColor:side=='s'?SellColor:UnknownColor);
}
