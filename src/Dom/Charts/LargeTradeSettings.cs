using Avalonia.Media;
namespace BookDepthIQ.Dom.Charts;

public enum TradeQuantityLabels {Off,Large,All}
public sealed record LargeTradeProfile
{
    public long MinContracts {get;set;}=50;
    public string BuyColor {get;set;}="#00C8F4";
    public string SellColor {get;set;}="#FF8B22";
    public string UnknownColor {get;set;}="#8C9EB0";
    public double Opacity {get;set;}=.2;
    public double MinDiameter {get;set;}=12;
    public double MaxDiameter {get;set;}=42;
    public TradeQuantityLabels Labels {get;set;}=TradeQuantityLabels.Large;
    public static bool ValidColor(string? value)=>value is {Length:7}&&value[0]=='#'&&value.Skip(1).All(Uri.IsHexDigit);
    public void Normalize()
    {
        MinContracts=Math.Clamp(MinContracts,1,1000000);
        BuyColor=ValidColor(BuyColor)?BuyColor.ToUpperInvariant():"#00C8F4";
        SellColor=ValidColor(SellColor)?SellColor.ToUpperInvariant():"#FF8B22";
        UnknownColor=ValidColor(UnknownColor)?UnknownColor.ToUpperInvariant():"#8C9EB0";
        Opacity=double.IsFinite(Opacity)?Math.Clamp(Opacity,0,1):.2;
        MinDiameter=double.IsFinite(MinDiameter)?Math.Clamp(MinDiameter,6,80):12;
        MaxDiameter=double.IsFinite(MaxDiameter)?Math.Clamp(MaxDiameter,MinDiameter,80):Math.Max(MinDiameter,42);
        if(!Enum.IsDefined(Labels)) Labels=TradeQuantityLabels.Large;
    }
    public double Diameter(long quantity)=>Math.Clamp(MinDiameter*Math.Sqrt(Math.Max(0,quantity)/(double)Math.Max(1,MinContracts)),MinDiameter,MaxDiameter);
    public Color ColorFor(char side)=>Color.Parse(side=='b'?BuyColor:side=='s'?SellColor:UnknownColor);
}
