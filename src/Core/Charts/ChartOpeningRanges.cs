namespace BookDepthIQ.Core.Charts;

public sealed record ChartOrLine(DateTimeOffset Start,DateTimeOffset End,long Ticks,string Label,bool Upper);

/// <summary>Independent chart adapter. NT thirty-second bars are end-stamped; incomplete ORs are omitted.</summary>
public static class ChartOpeningRanges
{
    public static IReadOnlyList<ChartOrLine> Build(IReadOnlyList<ChartBar> bars,PaxOrCfg cfg,string instrument,double tickSize,int frame,int days)
    {
        if(!ChartFrame.SupportsPax(frame)||tickSize<=0||cfg.DurationSec!=30) return Array.Empty<ChartOrLine>();
        var root=PaxOr.OrRoot(instrument);var step=PaxOr.LevelFactorTicks(root,cfg,tickSize);if(step<=0) return Array.Empty<ChartOrLine>();
        var sessions=new SortedDictionary<long,List<ChartBar>>();
        foreach(var bar in bars)
        {
            var session=SessionFor(bar.Time.AddTicks(-1),cfg);
            if(!sessions.TryGetValue(session.Start,out var list)) sessions[session.Start]=list=new();list.Add(bar);
        }
        var result=new List<ChartOrLine>();
        foreach(var pair in sessions.TakeLast(Math.Clamp(days,1,8)+1))
        {
            var list=pair.Value.OrderBy(b=>b.Time).ToArray();var opening=list.FirstOrDefault(b=>!b.Forming&&b.Time.ToUnixTimeMilliseconds()==pair.Key+30000);
            if(opening==null) continue;
            var end=DateTimeOffset.FromUnixTimeMilliseconds(SessionFor(opening.Time,cfg).End);
            var start=opening.Time;long high=opening.High,low=opening.Low;
            result.Add(new(start,end,high,"OR H",true));result.Add(new(start,end,low,"OR L",false));
            if(cfg.ShowMid) result.Add(new(start,end,(long)Math.Round((high+low)/2.0),"OR MID",true));
            int up=1,down=1;
            result.Add(new(start,end,high+step,"OR EXT1",true));result.Add(new(start,end,low-step,"OR EXT1",false));
            foreach(var bar in list.Where(b=>b.Time>opening.Time))
            {
                while(bar.High>high+step*up && up<200) {up++;result.Add(new(bar.Time,end,high+step*up,"OR EXT"+up,true));}
                while(bar.Low<low-step*down && down<200) {down++;result.Add(new(bar.Time,end,low-step*down,"OR EXT"+down,false));}
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
