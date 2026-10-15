using BookDepthIQ.Core;
using BookDepthIQ.Dom;
using Xunit;
using System.Text.Json;

public class TodayPnlTests
{
    [Fact]
    public void Account_switch_and_reconnect_require_a_fresh_equity_snapshot()
    {
        var s=new DomState(new Settings(),Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json"));
        using var hello=JsonDocument.Parse("{\"t\":\"hello\",\"accounts\":[]}");
        using var account=JsonDocument.Parse("{\"t\":\"account\",\"name\":\"Sim101\",\"cash\":100000}");
        s.Apply(hello.RootElement); Assert.False(s.HasAccountSnapshot);
        s.Apply(account.RootElement); Assert.True(s.HasAccountSnapshot);
        s.Account="APEX-123"; Assert.False(s.HasAccountSnapshot);
        s.Account="Sim101"; s.Apply(hello.RootElement); Assert.False(s.HasAccountSnapshot);
    }
    [Fact]
    public void Risk_counter_reconciles_after_journal_reset_without_rebasing_equity()
    {
        var stale=new DayState("2026-10-15",104950,15,Utc(15,2));
        var current=Protect.WithRecordedActivity(stale,new RecordedDayTotals("2026-10-15",5187,8,null));
        Assert.Equal(8,current.Trades); Assert.Equal(104950,current.StartEquity);
        var cleared=Protect.WithRecordedActivity(current,new RecordedDayTotals("2026-10-15",0,0,null));
        Assert.Equal(0,cleared.Trades); Assert.Null(cleared.LastLossAt);
        Assert.Equal(104950,cleared.StartEquity);
    }
    [Theory]
    [InlineData("2026-01-15T22:59:59Z","2026-01-15")]
    [InlineData("2026-01-15T23:00:00Z","2026-01-16")]
    [InlineData("2026-07-15T21:59:59Z","2026-07-15")]
    [InlineData("2026-07-15T22:00:00Z","2026-07-16")]
    public void Journal_and_header_share_dst_aware_session_dates(string utc,string expected)
    {
        var at=DateTimeOffset.Parse(utc).ToUnixTimeMilliseconds();
        Assert.Equal(expected,JournalAnalytics.TradingDate(at).ToString("yyyy-MM-dd"));
        Assert.Equal(expected,JournalAnalytics.RecordedDay(Array.Empty<JournalTrade>(),"Sim101",at).Day);
    }
    private static long Utc(int day,int hour,int minute=0) => new DateTimeOffset(2026,10,day,hour,minute,0,TimeSpan.Zero).ToUnixTimeMilliseconds();
    [Fact]
    public void Today_ignores_stale_equity_baseline_and_trade_counter()
    {
        var cfg=new Settings(); cfg.Days["Sim101"]=new("2026-10-15",104950,15);
        var s=new DomState(cfg,Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json")) { Account="Sim101",Cash=105187 };
        for(var i=0;i<8;i++) s.Journal.Add(new JournalTrade { Account="Sim101",Close=Utc(15,2),Exit=30000,Pnl=i==7 ? 987 : 600 });
        var today=s.RecordedToday(Utc(15,3));
        Assert.Equal(5187,today.Net); Assert.Equal(8,today.Trades);
        Assert.Equal(104950,cfg.Days["Sim101"].StartEquity);
        s.Account="APEX-123"; Assert.Equal(0,s.RecordedToday(Utc(15,3)).Net);
        s.Account="Sim101"; s.Journal.Clear();
        Assert.Equal(0,s.RecordedToday(Utc(15,3)).Trades);
        Assert.Equal(0,s.RecordedToday(Utc(15,3)).Net);
    }
    [Fact]
    public void Session_rolls_at_seventeen_chicago_not_midnight()
    {
        var rows=new[] { new JournalTrade { Account="Sim101",Close=Utc(14,21,59),Exit=1,Pnl=100 },
            new JournalTrade { Account="Sim101",Close=Utc(14,22),Exit=1,Pnl=20 },
            new JournalTrade { Account="Sim101",Close=Utc(15,5),Exit=1,Pnl=-5,FeesUsd=2 },
            new JournalTrade { Account="Other",Close=Utc(15,5),Exit=1,Pnl=999 },
            new JournalTrade { Account="Sim101",Close=Utc(15,5),Exit=null,Pnl=999 } };
        var result=JournalAnalytics.RecordedDay(rows,"Sim101",Utc(15,6));
        Assert.Equal("2026-10-15",result.Day); Assert.Equal(15,result.Net); Assert.Equal(2,result.Trades);
        Assert.Equal(0,JournalAnalytics.RecordedDay(rows,"Sim101",Utc(15,22)).Trades);
    }
    [Fact]
    public void Restart_uses_recorded_trades_even_when_saved_counter_is_stale()
    {
        var folder=Path.Combine(Path.GetTempPath(),"today-test-"+Guid.NewGuid()); var path=Path.Combine(folder,"journal.json");
        try
        {
            var cfg=new Settings(); cfg.Days["Sim101"]=new("2026-10-15",104950,15);
            var s=new DomState(cfg,path);
            s.Journal.Add(new JournalTrade { Account="Sim101",Close=Utc(15,2),Exit=30000,Pnl=237 }); s.SaveJournal();
            var restarted=new DomState(cfg,path);
            Assert.Equal(237,restarted.RecordedToday(Utc(15,3)).Net);
            Assert.Equal(1,restarted.RecordedToday(Utc(15,3)).Trades);
            restarted.Journal.Clear(); restarted.SaveJournal();
            var afterReset=new DomState(cfg,path);
            Assert.Equal(0,afterReset.RecordedToday(Utc(15,3)).Trades);
            Assert.Equal(0,afterReset.RecordedToday(Utc(15,3)).Net);
        }
        finally { if(File.Exists(path)) File.Delete(path); if(Directory.Exists(folder)) Directory.Delete(folder); }
    }
}
