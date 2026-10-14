using BookDepthIQ.Core;
using Xunit;

// Initial Balance port (BookDepthIQ ib_store.rs / ib_history.rs tests, same cases and numbers). The Rust
// store is armed by hand; here the tracker arms itself from the trade clock, so a print just before
// the open stands in for "the open was crossed live".
public class IbTests
{
    // Tuesday 2026-10-13 08:30:00 Chicago (CDT, UTC-5) = 13:30Z.
    private static readonly long A = new DateTimeOffset(2026, 10, 13, 13, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private const long S = 1000;

    private static IbTracker Live()
    {
        var ib = new IbTracker("08:30");
        ib.OnTrade(A - S, 50, 1, 'b'); // before the open: the open is then crossed live
        return ib;
    }

    [Fact]
    public void Lengths_are_exactly_the_six_settings() => Assert.Equal(new[] { 1, 3, 5, 15, 30, 60 }, Ib.Lengths);

    [Fact]
    public void Anchors_skip_weekends_and_the_missing_globex_bells()
    {
        Assert.Equal(A, Ib.RthAnchor(A, "08:30"));
        Assert.Equal(A - 86_400_000, Ib.RthAnchor(A - S, "08:30")); // before today's bell: Monday's
        // Sunday 2026-10-11 12:00 CT: the last RTH open is Friday's, and Globex hasn't opened since Thursday 17:00.
        var sunNoon = new DateTimeOffset(2026, 10, 11, 17, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal(new DateTimeOffset(2026, 10, 9, 13, 30, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), Ib.RthAnchor(sunNoon, "08:30"));
        Assert.Equal(new DateTimeOffset(2026, 10, 8, 22, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(), Ib.GlobexAnchor(sunNoon));
        // Sunday 17:00 CT is a real open.
        var sunOpen = new DateTimeOffset(2026, 10, 11, 22, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        Assert.Equal(sunOpen, Ib.GlobexAnchor(sunOpen + S));
        Assert.Equal("08:00", Ib.RthStart("CL", "08:30:00"));
        Assert.Equal("07:20", Ib.RthStart("GC", "08:30:00"));
        Assert.Equal("08:30:00", Ib.RthStart("NQ", "08:30:00"));
    }

    [Fact]
    public void Each_length_forms_at_its_own_end_and_the_end_is_exclusive()
    {
        var ib = Live();
        ib.OnTrade(A + 10 * S, 100, 1, 'b');
        ib.OnTrade(A + 30 * S, 104, 1, 'b');
        // Exactly at A+60s: closes the 1-minute range and is NOT part of it.
        var formed = ib.OnTrade(A + 60 * S, 110, 1, 'b').Formed.Where(f => f.Session == IbSession.Rth).ToList();
        Assert.Single(formed);
        Assert.Equal(new IbFormed(IbSession.Rth, 1, A, 104, 100, false), formed[0]);
        var one = ib.View(IbSession.Rth, 1);
        Assert.True(one.Formed);
        Assert.Equal((104L, 100L), (one.High!.Value, one.Low!.Value));
        Assert.Equal(IbState.Above, one.State);
        var three = ib.View(IbSession.Rth, 3);
        Assert.False(three.Formed);
        Assert.Equal(IbState.Building, three.State);
        Assert.Equal((110L, 100L), (three.High!.Value, three.Low!.Value));
        Assert.Equal(A + 180 * S, three.WindowEnd);
    }

    [Fact]
    public void Break_back_inside_and_delta_since_break()
    {
        var ib = Live();
        ib.OnTrade(A + S, 100, 1, 'b');
        ib.OnTrade(A + 2 * S, 104, 1, 'b');
        ib.OnTrade(A + 61 * S, 102, 1, 'b'); // forms 1m at 100..104, inside
        ib.OnTrade(A + 62 * S, 105, 3, 'b'); // break high, +3
        ib.OnTrade(A + 63 * S, 106, 1, 's'); // -1 -> 2
        ib.OnTrade(A + 64 * S, 103, 2, 's'); // back inside, -2 -> 0
        var one = ib.View(IbSession.Rth, 1);
        Assert.Equal(new[] { IbEventKind.BrokeHigh, IbEventKind.BackInside }, one.Events.Select(e => e.Kind));
        Assert.Equal(new IbEvent(IbEventKind.BrokeHigh, A + 62 * S, 105), one.Events[0]);
        Assert.Equal(0, one.DeltaSinceBreak);
        Assert.Equal(IbState.Inside, one.State);
    }

    [Fact]
    public void A_gap_straight_through_the_range_is_one_break_each_way()
    {
        var ib = Live();
        ib.OnTrade(A + S, 100, 1, 'b');
        ib.OnTrade(A + 2 * S, 104, 1, 'b');
        ib.OnTrade(A + 61 * S, 99, 1, 's'); // first post-IB trade already below
        ib.OnTrade(A + 62 * S, 107, 1, 'b'); // straight to above, never trades inside
        var one = ib.View(IbSession.Rth, 1);
        Assert.Equal(new[] { IbEventKind.BrokeLow, IbEventKind.BrokeHigh }, one.Events.Select(e => e.Kind));
        Assert.Equal(IbState.Above, one.State);
        Assert.Equal(1, one.DeltaSinceBreak);
    }

    [Fact]
    public void Repeat_attempts_are_all_recorded()
    {
        var ib = Live();
        ib.OnTrade(A + S, 100, 1, 'b');
        ib.OnTrade(A + 2 * S, 104, 1, 'b');
        ib.OnTrade(A + 61 * S, 102, 1, 'b');
        var px = new long[] { 105, 103, 105, 103 };
        for (var i = 0; i < px.Length; i++) ib.OnTrade(A + (62 + i) * S, px[i], 1, 'b');
        Assert.Equal(4, ib.View(IbSession.Rth, 1).Events.Count);
    }

    [Fact]
    public void Joining_late_marks_closed_lengths_missed_and_open_ones_partial()
    {
        var ib = new IbTracker("08:30"); // first print 2 minutes after the open
        ib.OnTrade(A + 121 * S, 100, 1, 'b');
        var formed = ib.OnTrade(A + 180 * S, 101, 1, 'b').Formed.Where(f => f.Session == IbSession.Rth).ToList(); // 3m closes
        Assert.Single(formed);
        Assert.True(formed[0].Partial);
        var one = ib.View(IbSession.Rth, 1);
        Assert.Equal(IbState.Missed, one.State);
        Assert.Null(one.High);
        Assert.True(ib.View(IbSession.Rth, 3).Partial);
        Assert.Equal(new[] { 1 }, ib.MissedLengths(IbSession.Rth));
    }

    [Fact]
    public void Restore_fills_a_missed_range()
    {
        var ib = new IbTracker("08:30");
        ib.OnTrade(A + 120 * S, 102, 1, 'b');
        ib.RestoreFormed(IbSession.Rth, 1, 104, 100);
        var one = ib.View(IbSession.Rth, 1);
        Assert.True(one.Formed && one.Restored && !one.Partial);
        Assert.Equal((104L, 100L), (one.High!.Value, one.Low!.Value));
        Assert.Empty(ib.MissedLengths(IbSession.Rth));
        ib.OnTrade(A + 121 * S, 106, 1, 'b');
        Assert.Equal(IbState.Above, ib.View(IbSession.Rth, 1).State);
    }

    [Fact]
    public void A_new_open_resets_the_session()
    {
        var ib = Live();
        ib.OnTrade(A + S, 100, 1, 'b');
        Assert.Equal(100, ib.View(IbSession.Rth, 5).High);
        var next = A + 86_400_000; // Wednesday's bell
        var r = ib.OnTrade(next + S, 120, 1, 'b');
        Assert.Contains(IbSession.Rth, r.Armed);
        var five = ib.View(IbSession.Rth, 5);
        Assert.Equal(120, five.High);
        Assert.Equal(next, five.WindowStart);
        Assert.False(five.Partial); // crossed live: joined at the open
    }

    [Fact]
    public void Sessions_are_independent()
    {
        // 17:00 CT Monday is the Globex open; 08:30 Tuesday the RTH open. A print at 08:30:01 is in both
        // RTH's windows and (long after its 60 minutes) none of Globex's.
        var ib = Live();
        ib.OnTrade(A + S, 90, 1, 'b');
        Assert.Equal(90, ib.View(IbSession.Rth, 1).Low);
        Assert.Equal(A - 55_800_000, ib.View(IbSession.Globex, 1).WindowStart); // 15.5 h earlier
        Assert.Equal(IbState.Missed, ib.View(IbSession.Globex, 60).State); // joined long after the Globex open
    }

    [Fact]
    public void Poc_and_value_area_come_from_the_ib_window_only()
    {
        var ib = Live();
        ib.OnTrade(A + S, 100, 10, 'b');
        ib.OnTrade(A + 2 * S, 101, 1, 'b');
        ib.OnTrade(A + 61 * S, 105, 500, 'b'); // after the 1m window: not in its profile
        var one = ib.View(IbSession.Rth, 1);
        Assert.Equal(100, one.Poc);
        Assert.NotNull(one.Val);
        Assert.NotNull(one.Vah);
    }

    [Fact]
    public void Value_area_expands_from_the_poc_two_rows_at_a_time()
    {
        // volume_profile.rs: symmetric around 100, total 30, target 21 -> VAL 98, VAH 102.
        var vol = new Dictionary<long, long> { [97] = 1, [98] = 3, [99] = 6, [100] = 10, [101] = 6, [102] = 3, [103] = 1 };
        Assert.Equal((100L, 98L, 102L), Ib.ValueArea(vol, 0.70));
        Assert.Null(Ib.ValueArea(new Dictionary<long, long>(), 0.70));
    }

    [Fact]
    public void A_window_that_closes_with_no_trades_is_missed_not_inside()
    {
        var ib = Live();
        // First trade after the open lands after the 1/3/5-minute windows: they closed with no trades.
        var formed = ib.OnTrade(A + 6 * 60 * S, 100, 1, 'b').Formed.Where(f => f.Session == IbSession.Rth);
        Assert.Empty(formed);
        foreach (var len in new[] { 1, 3, 5 })
        {
            var r = ib.View(IbSession.Rth, len);
            Assert.Equal(IbState.Missed, r.State);
            Assert.Null(r.High);
        }
        Assert.Equal(IbState.Building, ib.View(IbSession.Rth, 15).State);
    }

    private static IbHistoryRow Row(string d, string root, int len, long hi, long lo) => new(d, root, IbSession.Rth, len, "08:30", hi, lo, 0.25);

    [Fact]
    public void Stats_use_only_past_days_of_the_same_key()
    {
        var h = IbHistory.InMemory();
        foreach (var (d, r) in new[] { ("2026-10-01", 10), ("2026-10-02", 20), ("2026-10-03", 30), ("2026-10-04", 40), ("2026-10-05", 50) })
            h.Record(Row(d, "NQ", 15, 100 + r, 100));
        h.Record(Row("2026-10-07", "NQ", 15, 999, 100)); // today: excluded from its own stats
        h.Record(Row("2026-10-05", "ES", 15, 900, 100)); // other root
        h.Record(Row("2026-10-05", "NQ", 5, 900, 100)); // other length
        h.Record(Row("2026-10-05", "NQ", 15, 150, 100) with { OpenHhmm = "08:00" }); // other open time
        var s = h.Stats("NQ", IbSession.Rth, 15, "08:30", "2026-10-07", 30);
        Assert.Equal(5, s.Days);
        Assert.Equal(30.0, s.MedianRange);
        Assert.Equal(60.0, s.Percentile); // 10, 20, 30 are <= 30: 3 of 5
        Assert.Equal(new IbPrev("2026-10-05", 150, 100), s.Prev);
    }

    [Fact]
    public void Percentile_needs_five_days()
    {
        var h = IbHistory.InMemory();
        foreach (var d in new[] { "2026-10-01", "2026-10-02", "2026-10-03", "2026-10-04" }) h.Record(Row(d, "NQ", 15, 110, 100));
        var s = h.Stats("NQ", IbSession.Rth, 15, "08:30", "2026-10-07", 10);
        Assert.Equal(4, s.Days);
        Assert.Null(s.Percentile);
        Assert.Equal(10.0, s.MedianRange);
    }

    [Fact]
    public void A_re_formed_ib_replaces_its_row_and_survives_a_reload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"ib-{Guid.NewGuid():N}.jsonl");
        try
        {
            var h = new IbHistory(path);
            h.Record(Row("2026-10-05", "NQ", 15, 110, 100));
            h.Record(Row("2026-10-05", "NQ", 15, 120, 100));
            Assert.Equal(1, h.Count);
            var again = new IbHistory(path);
            Assert.Equal(120, again.Get("2026-10-05", "NQ", IbSession.Rth, 15, "08:30")!.High);
            Assert.Single(File.ReadAllLines(path));
        }
        finally { File.Delete(path); }
    }
}
