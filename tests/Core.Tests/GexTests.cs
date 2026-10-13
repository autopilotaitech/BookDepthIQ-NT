using System.Text.Json;
using BookDepthIQ.Core;
using Xunit;

// GEX port (BookDepthIQ gex.rs / gex/tests.rs): parsing, sign, walls, flip, levels, basis, transition rule.
public class GexTests
{
    private static GexContract C(double strike, bool call, double gamma, double oi, int exp = 261016, double iv = 0.2, double delta = 0.5) =>
        new(strike, exp, call, gamma, oi, iv, call ? delta : -delta);

    [Fact]
    public void Osi_symbols_parse_both_roots_and_reject_junk()
    {
        Assert.Equal((260821, true, 4000.0), Gex.ParseOsi("NDX260821C04000000"));
        Assert.Equal((261013, false, 725.0), Gex.ParseOsi("QQQ261013P00725000"));
        Assert.Equal((260821, true, 7800.0), Gex.ParseOsi("SPXW260821C07800000"));
        Assert.Null(Gex.ParseOsi("QQQ261013X00725000"));
        Assert.Null(Gex.ParseOsi("261013C00725000"));
        Assert.Null(Gex.ParseOsi("short"));
    }

    [Fact]
    public void Puts_reduce_net_gex()
    {
        var calls = Gex.BuildProfile(new[] { C(730, true, 0.05, 1000) }, 730);
        var both = Gex.BuildProfile(new[] { C(730, true, 0.05, 1000), C(725, false, 0.05, 1000) }, 730);
        Assert.True(calls.Net > 0);
        Assert.True(both.Net < calls.Net);
        Assert.Equal(0.05 * 1000 * 100 * 730 * 730 * 0.01, calls.Net, 6);
    }

    [Fact]
    public void Walls_are_the_heaviest_call_above_and_put_below_spot_and_magnet_the_combined_peak()
    {
        var side = new[]
        {
            C(735, true, 0.04, 3000), C(740, true, 0.03, 1000), C(728, true, 0.02, 500),
            C(725, false, 0.04, 2500), C(720, false, 0.02, 800), C(731, false, 0.01, 100),
        };
        var p = Gex.BuildProfile(side, 730.5);
        var (cw, pw) = Gex.Walls(p, 730.5);
        Assert.Equal(735, cw);
        Assert.Equal(725, pw);
        var top = Gex.TopLevels(p, 3, cw, pw);
        Assert.DoesNotContain(top, l => l.Price is 735 or 725);

        // A heavy at-the-money pair is the magnet — and, with no ATM exclusion (algo v4), the put wall
        // too when it sits just below spot (the live QQQ chain shows the same: put wall = magnet).
        var atm = Gex.BuildProfile(side.Concat(new[] { C(730, true, 0.09, 4000), C(730, false, 0.09, 4000) }), 730.5);
        Assert.Equal(730, Gex.Magnet(atm));
        Assert.Equal(730, Gex.Walls(atm, 730.5).Put);
    }

    [Fact]
    public void Cumulative_flip_interpolates_between_strikes()
    {
        var p = Gex.BuildProfile(new[] { C(720, false, 0.05, 1000), C(740, true, 0.05, 3000) }, 730);
        // cum: −X at 720, +2X at 740 → crosses a third of the way.
        Assert.Equal(720 + 20.0 / 3, Gex.CumulativeFlip(p)!.Value, 6);
    }

    [Fact]
    public void Zero_gamma_spot_sits_between_put_and_call_heavy_strikes()
    {
        var cs = new[] { C(700, false, 0.03, 20000, iv: 0.25, delta: 0.3), C(760, true, 0.03, 20000, iv: 0.25, delta: 0.3) };
        var flip = Gex.ZeroGammaSpot(cs, 730, 261013);
        Assert.NotNull(flip);
        Assert.InRange(flip!.Value, 715, 745);
    }

    [Fact]
    public void Expected_range_reads_iv30_as_a_percentage()
    {
        var (lo, hi) = Gex.ExpectedRange(30125.33, 18.265)!.Value;
        Assert.InRange(hi - 30125.33, 340, 350); // spot × 0.18265 × √(1/252) ≈ 346.6
        Assert.Equal(hi - 30125.33, 30125.33 - lo, 6);
        Assert.Null(Gex.ExpectedRange(100, 0));
    }

    [Fact]
    public void Futures_conversion_by_spread_or_ratio_snaps_to_the_tick()
    {
        Assert.Equal(7814.0, Gex.ToFutures(7774.35, 39.67, GexConversion.Spread, 0.25));
        Assert.Equal(30098.0, Gex.ToFutures(730, 41.23, GexConversion.Ratio, 0.25)); // 30097.9 → nearest 0.25
    }

    [Fact]
    public void Basis_is_a_median_and_absent_until_enough_samples()
    {
        var b = new GexBasis(GexConversion.Ratio, cap: 100, minSamples: 5);
        for (var i = 0; i < 4; i++) b.Sample(41.2 * 730, 730);
        Assert.Null(b.Value);
        b.Sample(99 * 730, 730); // one bad print does not drag a median
        Assert.Equal(41.2, b.Value!.Value, 6);
        b.Sample(double.NaN, 730);
        Assert.Equal(5, b.Count);
    }

    private static List<GexContract> Chain(double oiScale)
    {
        var l = new List<GexContract>();
        for (var k = 700; k <= 760; k++)
        {
            l.Add(C(k, true, 0.02, 1000 * oiScale * (k == 740 ? 3 : 1), exp: 261016, delta: 0.4));
            l.Add(C(k, false, 0.02, 1000 * oiScale * (k == 720 ? 3 : 1), exp: 261016, delta: 0.4));
        }
        for (var i = 0; i < 300; i++) l.Add(C(500 + i, true, 0.001, 1, exp: 261218, delta: 0.1)); // > 500 contracts
        return l;
    }

    [Fact]
    public void Levels_hold_while_the_vintage_holds_and_move_on_a_new_one()
    {
        var st = new GexStore(new GexSource("QQQ", GexConversion.Ratio), minSamples: 3);
        Assert.True(st.Accept(Gex.Compute("QQQ", "2026-10-13 15:00:00", 730, 18, Chain(1))));
        st.NoteSpot(730);
        for (var i = 0; i < 3; i++) st.SampleBasis(730 * 41.2);
        var v1 = st.View(0.25, GexStore.Scope.Near, live: false)!;
        Assert.Equal(Gex.ToFutures(740, 41.2, GexConversion.Ratio, 0.25), v1.Set.CallWall);

        // Same OI, spot and gamma moved: nothing learned, nothing moves (basis is frozen too).
        Assert.False(st.Accept(Gex.Compute("QQQ", "2026-10-13 15:01:00", 735, 18, Chain(1))));
        for (var i = 0; i < 50; i++) st.SampleBasis(730 * 41.9);
        Assert.Equal(v1.Set.CallWall, st.View(0.25, GexStore.Scope.Near, false)!.Set.CallWall);

        // New vintage (OI changed): accepted, with a fresh conversion.
        Assert.True(st.Accept(Gex.Compute("QQQ", "2026-10-14 09:00:00", 730, 18, Chain(2))));
        Assert.NotEqual(v1.Basis, st.View(0.25, GexStore.Scope.Near, false)!.Basis);
    }

    [Fact]
    public void Nothing_is_drawn_without_a_conversion_and_an_empty_computation_never_freezes()
    {
        var st = new GexStore(new GexSource("QQQ", GexConversion.Ratio), minSamples: 3);
        st.SampleBasis(30000); // before any poll: no spot to pair with
        Assert.Equal(0, st.BasisSamples);
        var empty = Gex.Compute("QQQ", "2026-10-13 15:00:00", 730, 18, new[] { C(730, true, 0, 0) });
        Assert.False(st.Accept(empty));
        Assert.Null(st.Frozen);
        Assert.True(st.Accept(Gex.Compute("QQQ", "2026-10-13 15:00:00", 730, 18, Chain(1))));
        Assert.Null(st.View(0.25, GexStore.Scope.Near, false)); // no basis yet
    }

    [Fact]
    public void Near_set_skips_today_and_zero_dte_is_todays_expiry()
    {
        var cs = Chain(1);
        cs.Add(C(731, true, 0.2, 5000, exp: 261013));
        var snap = Gex.Compute("QQQ", "2026-10-13 15:00:00", 730, 18, cs);
        Assert.Equal(261013, snap.ZeroDteExpiry);
        Assert.Equal(731, snap.ZeroDte.Magnet);
        Assert.NotEqual(731, snap.Near.Magnet);
    }

    [Fact]
    public void Payload_parses_and_a_snapshot_round_trips_through_json()
    {
        const string json = """
        {"timestamp": "2026-10-13 20:34:36", "data": {"current_price": 730.5, "iv30": 18.2, "options": [
          {"option": "QQQ261016C00735000", "gamma": 0.04, "open_interest": 3000, "iv": 0.2, "delta": 0.4},
          {"option": "QQQ261016P00725000", "gamma": 0.04, "open_interest": 2500, "iv": 0.2, "delta": -0.4},
          {"option": "garbage", "gamma": 1}]}}
        """;
        var (ts, spot, iv30, cs) = Gex.ParsePayload(json)!.Value;
        Assert.Equal("2026-10-13 20:34:36", ts);
        Assert.Equal(730.5, spot);
        Assert.Equal(18.2, iv30);
        Assert.Equal(2, cs.Count);
        Assert.Null(Gex.ParsePayload("{\"nope\": 1}"));

        var snap = Gex.Compute("QQQ", ts, spot, iv30, cs);
        var opts = new JsonSerializerOptions { IncludeFields = true };
        var back = JsonSerializer.Deserialize<GexSnapshot>(JsonSerializer.Serialize(snap, opts), opts)!;
        Assert.Equal(snap.Near.CallWall, back.Near.CallWall);
        Assert.Equal(snap.TotalOi, back.TotalOi);
        Assert.Equal(snap.All.Levels.Count, back.All.Levels.Count);
    }

    /// <summary>Opt-in: BDIQ_GEX_FIXTURE=path to a real CBOE chain (e.g. QQQ.json) → sane levels near spot.</summary>
    [Fact]
    public void Real_chain_gives_levels_near_spot()
    {
        var path = Environment.GetEnvironmentVariable("BDIQ_GEX_FIXTURE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var (ts, spot, iv30, cs) = Gex.ParsePayload(File.ReadAllText(path))!.Value;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var snap = Gex.Compute("QQQ", ts, spot, iv30, cs);
        var ms = sw.ElapsedMilliseconds;
        var n = snap.Near;
        File.WriteAllText(path + ".levels.txt",
            $"ts {ts} spot {spot} iv30 {iv30} contracts {cs.Count} oi {snap.TotalOi} compute {ms} ms\n" +
            $"NEAR flip {n.Flip} cw {n.CallWall} pw {n.PutWall} magnet {n.Magnet} net {n.NetGex:E2}\n" +
            $"levels {string.Join(", ", n.Levels.Select(l => $"{l.Price}"))}\nrange {snap.RangeLow:0.00}-{snap.RangeHigh:0.00}\n" +
            $"ALL flip {snap.All.Flip} cw {snap.All.CallWall} pw {snap.All.PutWall}\n0DTE {snap.ZeroDteExpiry} flip {snap.ZeroDte.Flip} cw {snap.ZeroDte.CallWall} pw {snap.ZeroDte.PutWall}\n");
        Assert.True(snap.Plausible);
        Assert.InRange(n.CallWall!.Value, spot, spot * 1.2);
        Assert.InRange(n.PutWall!.Value, spot * 0.8, spot);
    }
}
