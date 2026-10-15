using System.Text.Json;
using BookDepthIQ.Core;
using BookDepthIQ.Dom;
using Xunit;

public class JournalResetTests
{
    [Fact]
    public void Reset_backs_up_and_persists_only_selected_sim_history()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        var state=new DomState(new Settings(),path);
        state.Journal.AddRange(new[] { "Sim101","Sim102","Live123" }.Select(a=>new JournalTrade
            { Account=a, Inst="MNQ", Exit=101, Close=DomState.NowMs(), Pnl=20 }));
        state.SaveJournal();
        var original=File.ReadAllText(path);
        var backup=state.ResetSimJournal("Sim101");
        Assert.Equal(original,File.ReadAllText(backup));
        Assert.Equal(new[]{"Sim102","Live123"},new DomState(new Settings(),path).Journal.Select(t=>t.Account));
        Assert.Equal(0,state.RecordedToday(DomState.NowMs()).Net);
        Assert.Equal(0,state.RecordedToday(DomState.NowMs()).Trades);
        File.Delete(path); File.Delete(backup);
    }

    [Fact]
    public void Reset_rejects_live_and_preserves_memory_when_storage_fails()
    {
        var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
        var state=new DomState(new Settings(),path);
        state.Journal.Add(new JournalTrade {Account="Sim101",Pnl=20});
        Assert.Throws<InvalidOperationException>(()=>state.ResetSimJournal("Live123"));
        using(var locked=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            Assert.ThrowsAny<IOException>(()=>state.ResetSimJournal("Sim101"));
        Assert.Single(state.Journal);
        File.Delete(path);
    }
}
