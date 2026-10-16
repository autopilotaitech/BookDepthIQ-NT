using System.Globalization;
namespace BookDepthIQ.Core.Charts;

public sealed record SessionDefinition(string Id,string Name,bool Enabled,string Color,string Start,string End,string TimeZoneId);
public sealed record SessionZone(string Id,string Name,DateTimeOffset Start,DateTimeOffset End,DateTimeOffset Progress,DateTimeOffset ExtendUntil,long High,long Low,string State,string Color);
public sealed record SessionZoneSnapshot(IReadOnlyList<SessionZone> Zones,IReadOnlyList<string> Notes)
{
    public static readonly SessionZoneSnapshot Empty=new(Array.Empty<SessionZone>(),Array.Empty<string>());
}
public static class ChartSessionZones
{
    public static bool TryZone(string id,out TimeZoneInfo zone)
    {
        zone=TimeZoneInfo.Utc;
        try {zone=TimeZoneInfo.FindSystemTimeZoneById(id);return true;}
        catch(Exception e) when(e is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException) {return false;}
    }
    public static bool TryWindow(SessionDefinition d,DateOnly date,out DateTimeOffset start,out DateTimeOffset end,out string reason)
    {
        start=end=default;reason="";
        if(!TimeOnly.TryParseExact(d.Start,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var a)||
            !TimeOnly.TryParseExact(d.End,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var b)||a==b)
        {reason="Use different start/end HH:mm times.";return false;}
        if(!TryZone(d.TimeZoneId,out var zone)) {reason="Unknown time zone: "+d.TimeZoneId;return false;}
        var localStart=date.ToDateTime(a,DateTimeKind.Unspecified);var localEnd=date.AddDays(b<a?1:0).ToDateTime(b,DateTimeKind.Unspecified);
        if(zone.IsInvalidTime(localStart)||zone.IsInvalidTime(localEnd)||zone.IsAmbiguousTime(localStart)||zone.IsAmbiguousTime(localEnd))
        {reason="DST skipped/repeated boundary; session omitted.";return false;}
        start=new(TimeZoneInfo.ConvertTimeToUtc(localStart,zone));end=new(TimeZoneInfo.ConvertTimeToUtc(localEnd,zone));return true;
    }
    public static SessionZoneSnapshot Build(IReadOnlyList<ChartBar> bars,IReadOnlyList<SessionDefinition> definitions,int previous,DateTimeOffset now,bool sourceLive)
    {
        var output=new List<SessionZone>();var notes=new List<string>();
        if(bars.Count==0) return new(Array.Empty<SessionZone>(),new[]{"No native 30-second session data."});
        var sorted=bars.OrderBy(b=>b.Time).ToArray();
        int Bound(DateTimeOffset time) {int a=0,b=sorted.Length;while(a<b) {int m=(a+b)/2;if(sorted[m].Time<=time) a=m+1;else b=m;}return a;}
        foreach(var d in definitions.Take(3).Where(d=>d.Enabled))
        {
            if(!TryZone(d.TimeZoneId,out var tz)) {notes.Add(d.Name+": unknown time zone.");continue;}
            var current=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now,tz).DateTime);var ranges=new List<SessionZone>();
            for(int day=0;day<=29;day++)
            {
                var date=current.AddDays(-day);
                if(!TryWindow(d,date,out var start,out var end,out var reason)) {notes.Add(d.Name+": "+reason);continue;}
                if(start>=now) continue;
                var next=date.AddDays(1);DateTimeOffset extend=end;
                for(int n=0;n<3;n++,next=next.AddDays(1)) if(TryWindow(d,next,out var nextStart,out _,out _)) {extend=nextStart;break;}
                bool developing=now<end,gap=false;long high=long.MinValue,low=long.MaxValue;DateTimeOffset? first=null,last=null;
                var upper=developing?now:end;
                for(int i=Bound(start);i<sorted.Length;i++)
                {
                    var bar=sorted[i];var from=bar.Time.AddSeconds(-30);
                    if(from>=upper) break;
                    if(from<start) continue;
                    if(bar.Forming)
                    {
                        if(!developing||from>now||bar.Time>end) continue;
                    }
                    else if(bar.Time>upper) continue;
                    if(last!=null&&from>last.Value) gap=true;
                    high=Math.Max(high,bar.High);low=Math.Min(low,bar.Low);first??=from;last=bar.Forming&&now<bar.Time?now:bar.Time;
                }
                if(first==null||last==null) continue;
                bool partial=gap||first>start||(!developing&&last<end);
                bool stale=!sourceLive||(developing&&now-last.Value>TimeSpan.FromSeconds(90));
                var state=stale?(partial?"PARTIAL · STALE":"STALE"):partial?"PARTIAL":developing?"LIVE":"COMPLETE";
                var progress=developing?(last<now?last.Value:now):end;
                ranges.Add(new(d.Id,d.Name,start,end,progress,extend,high,low,state,d.Color));
            }
            var completed=ranges.Where(r=>r.End<=now).OrderByDescending(r=>r.Start).Take(Math.Clamp(previous,1,8));
            output.AddRange(completed.Concat(ranges.Where(r=>r.End>now).Take(1)));
        }
        return new(Array.AsReadOnly(output.OrderBy(r=>r.Start).ToArray()),Array.AsReadOnly(notes.Distinct().Take(20).ToArray()));
    }
}
