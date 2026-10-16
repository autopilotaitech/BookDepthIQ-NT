using BookDepthIQ.Core.Charts;
namespace BookDepthIQ.Dom.Charts;
public sealed record SessionZoneSettings
{
    public bool Enabled {get;set;}
    public double Opacity {get;set;}=.08;
    public bool ShowLabels {get;set;}=true;
    public bool ExtendLevels {get;set;}=true;
    public int Previous {get;set;}=3;
    public SessionDefinition[] Definitions {get;set;}=Defaults();
    public static SessionDefinition[] Defaults()=>new[]{
        new SessionDefinition("tokyo","TOKYO",true,"#9A85EF","09:00","15:00","Asia/Tokyo"),
        new SessionDefinition("london","LONDON",true,"#00BBD6","08:00","16:00","Europe/London"),
        new SessionDefinition("ny","NEW YORK",true,"#579AFF","09:30","16:00","America/New_York")};
    public SessionZoneSettings Copy()=>this with {Definitions=Definitions.ToArray()};
    public void Normalize()
    {
        Opacity=double.IsFinite(Opacity)?Math.Clamp(Opacity,0,.3):.08;Previous=Math.Clamp(Previous,1,8);
        var defaults=Defaults();var source=Definitions??Array.Empty<SessionDefinition>();
        Definitions=defaults.Select(f=> {
            var d=source.FirstOrDefault(d=>d!=null&&d.Id==f.Id)??f;
            var valid=ChartSessionZones.TryWindow(d,new DateOnly(2026,1,15),out _,out _,out _);
            return d with {Name=f.Name,Color=LargeTradeProfile.ValidColor(d.Color)?d.Color.ToUpperInvariant():f.Color,Enabled=d.Enabled&&valid};
        }).ToArray();
    }
}
