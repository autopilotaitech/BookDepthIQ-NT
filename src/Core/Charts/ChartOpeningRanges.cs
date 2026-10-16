namespace BookDepthIQ.Core.Charts;

public sealed record ChartOrLine(DateTimeOffset Start,DateTimeOffset End,long Ticks,string Label,bool Upper,DateOnly SessionDate=default,bool IsCurrent=false);

/// <summary>Independent chart adapter. NT thirty-second bars are end-stamped; incomplete ORs are omitted.</summary>
public static class ChartOpeningRanges
{
    public static IReadOnlyList<ChartOrLine> Build(IReadOnlyList<ChartBar> bars,PaxOrCfg cfg,string instrument,double tickSize,int frame,int days,DateTimeOffset? now=null)
    {
        if(!ChartFrame.SupportsPax(frame)||tickSize<=0||cfg.DurationSec!=30) return Array.Empty<ChartOrLine>();
        var root=PaxOr.OrRoot(instrument);var step=PaxOr.LevelFactorTicks(root,cfg,tickSize);
        var sessions=new SortedDictionary<long,List<ChartBar>>();
        var zone=BookDepthIQ.Core.Session.Zone(cfg.Timezone);
        var today=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now??DateTimeOffset.UtcNow,zone).DateTime);
        if(!TimeSpan.TryParse(cfg.EndTime,out var plotEnd)) return Array.Empty<ChartOrLine>();
        foreach(var bar in bars)
        {
            var session=SessionFor(bar.Time.AddTicks(-1),cfg);
            if(!sessions.TryGetValue(session.Start,out var list)) sessions[session.Start]=list=new();list.Add(bar);
        }
        var result=new List<ChartOrLine>();
        var previous=sessions.Where(p=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(p.Key),zone).DateTime)<today).TakeLast(Math.Clamp(days,0,8));
        var current=sessions.Where(p=>DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(p.Key),zone).DateTime)==today);
        foreach(var pair in previous.Concat(current))
        {
            var list=pair.Value.OrderBy(b=>b.Time).ToArray();var opening=list.FirstOrDefault(b=>!b.Forming&&b.Time.ToUnixTimeMilliseconds()==pair.Key+30000);
            if(opening==null) continue;
            var local=TimeZoneInfo.ConvertTime(opening.Time,zone).Date;
            var end=new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local+plotEnd,DateTimeKind.Unspecified),zone));
            var start=opening.Time;long high=opening.High,low=opening.Low;
            if(end<=start) continue;
            var date=DateOnly.FromDateTime(local);bool isCurrent=date==today;
            void Add(DateTimeOffset from,long ticks,string label,bool upper)=>result.Add(new(from,end,ticks,label,upper,date,isCurrent));
            Add(start,high,"OR H",true);Add(start,low,"OR L",false);
            if(cfg.ShowMid) Add(start,(long)Math.Round((high+low)/2.0),"OR MID",true);
            if(step<=0) continue;
            int up=1,down=1;
            Add(start,high+step,"OR EXT1",true);Add(start,low-step,"OR EXT1",false);
            foreach(var bar in list.Where(b=>!b.Forming&&b.Time>opening.Time&&b.Time<=end))
            {
                if(bar.High>high+step*up && up<200) {up++;Add(bar.Time,high+step*up,"OR EXT"+up,true);}
                if(bar.Low<low-step*down && down<200) {down++;Add(bar.Time,low-step*down,"OR EXT"+down,false);}
            }
        }
        return result;
    }
    static OrSession SessionFor(DateTimeOffset time,PaxOrCfg cfg)
    {
        var zone=BookDepthIQ.Core.Session.Zone(cfg.Timezone);
        if(!TimeSpan.TryParse(cfg.StartTime,out var bell)) bell=new TimeSpan(8,30,0);
        var local=TimeZoneInfo.ConvertTime(time,zone).DateTime;
        var date=local.Date;if(local.TimeOfDay<bell) date=date.AddDays(-1);
        while(date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) date=date.AddDays(-1);
        var next=date.AddDays(1);while(next.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) next=next.AddDays(1);
        long Utc(DateTime d)=>new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(d+bell,DateTimeKind.Unspecified),zone)).ToUnixTimeMilliseconds();
        return new(Utc(date),Utc(date)+30000,Utc(next));
    }
}
