using System.Text.Json;
using System.Text.Json.Serialization;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

public enum StopMode { Ticks, Atr, Swing }
public enum TrailMode { Off, R, St1, St2 }
public enum PreviewMode { Off, Long, Short }

/// <summary>GEX levels (BookDepthIQ's GEX Levels indicator): which expiries, frozen daily map or the
/// recalculated one, and what to draw.</summary>
public sealed class GexCfg
{
    public bool On { get; set; } = true;
    public GexStore.Scope Scope { get; set; } = GexStore.Scope.Near;
    /// <summary>false = the daily open-interest map (moves once a day); true = recalculated every poll.</summary>
    public bool Live { get; set; }
    public int TopN { get; set; } = 5;
    public bool ShowRange { get; set; } = true;
    public bool ShowMagnet { get; set; } = true;
    public double LineWidth { get; set; } = 1.5;
}

/// <summary>Initial Balance on the ladder (BookDepthIQ's IB layer). Off by default, as there: a new layer must
/// not appear uninvited. All 12 ranges are always tracked; these only choose which one to draw.</summary>
public sealed class IbCfg
{
    public bool On { get; set; }
    public IbSession Session { get; set; } = IbSession.Rth;
    /// <summary>1, 3, 5, 15, 30 or 60.</summary>
    public int LengthMin { get; set; } = 15;
    public bool ShowMid { get; set; } = true;
    public bool ShowExtensions { get; set; } = true;
    public bool ShowValueArea { get; set; }
    public double LineWidth { get; set; } = 1.5;
}

/// <summary>Which ladder columns show (the Vest panel's set).</summary>
public sealed class Columns
{
    public bool Orders { get; set; } = true;
    public bool Footprint { get; set; } = true;
    public bool Pnl { get; set; } = true;
    public bool Heat { get; set; } = true;
    public bool Profile { get; set; } = true;
    public bool Delta { get; set; } = true;
}

/// <summary>The user's settings, saved to %APPDATA%\BookDepthIQ-NT\settings.json. Every risk number is the
/// user's own; nothing is hard-coded into an order.</summary>
public sealed class Settings
{
    // ── market / account ──
    public string Instrument { get; set; } = "NQ 12-26";
    public string Account { get; set; } = "Sim101";
    public int HistoryHours { get; set; } = 8;

    // ── ticket ──
    public SizeMode SizeMode { get; set; } = SizeMode.Contracts;
    public double SizeInput { get; set; } = 1;
    public int[] TpTicks { get; set; } = { 32, 0, 0 };
    public string TpSplit { get; set; } = "50/30/20";
    public int SlTicks { get; set; } = 16;
    public StopMode StopMode { get; set; } = StopMode.Ticks;
    /// <summary>ATR stop: ST1 ATR × this. Swing stop: buffer ticks past the swing.</summary>
    public double AtrMult { get; set; } = 1.5;
    public int SwingMinutes { get; set; } = 10;
    public int SwingBufTicks { get; set; } = 2;
    /// <summary>ATR / swing mode: targets as R multiples of the stop.</summary>
    public double[] TpR { get; set; } = { 2, 0, 0 };
    public bool BracketOn { get; set; } = true;
    public bool BeAfterTp1 { get; set; }
    public int BeOffsetTicks { get; set; } = 1;
    public TrailMode Trail { get; set; } = TrailMode.Off;
    public double TrailStartR { get; set; } = 1;
    public bool StopGuard { get; set; }
    public string Setup { get; set; } = "pullback";
    public PreviewMode Preview { get; set; } = PreviewMode.Off;
    public bool ScaleOn { get; set; }
    public int ScaleCount { get; set; } = 4;
    public int ScaleSpanTicks { get; set; } = 16;
    public double ScaleSkew { get; set; } = 1;
    public SkewDir ScaleDir { get; set; } = SkewDir.Even;
    public bool HotkeysArmed { get; set; }

    // ── fees / limits ──
    /// <summary>Commission + exchange/NFA fees per contract per side, $ (your broker's).</summary>
    public double FeePerSide { get; set; } = 2.09;
    public int MaxContracts { get; set; }
    /// <summary>Fat-finger warnings (never block): size × normal, far from market (ticks, 0 = 4 × ATR).</summary>
    public double SizeMult { get; set; } = 2;
    public int FarTicks { get; set; }

    // ── prop account rules ──
    public double Start { get; set; } = 50000;
    public double MaxDrawdown { get; set; } = 2500;
    public DrawdownKind DrawdownKind { get; set; } = DrawdownKind.TrailingEod;
    public double LockAt { get; set; } = 50100;
    public double DailyLoss { get; set; }
    public Dictionary<string, double> Peaks { get; set; } = new();

    // ── protect my day ──
    public double DayLossUsd { get; set; }
    public int DayMaxTrades { get; set; }
    public int CooldownSec { get; set; }
    public bool CloseAtDayLimit { get; set; }
    public bool WeekendClose { get; set; }
    public string WeekendCloseTime { get; set; } = "16:00";
    public Dictionary<string, DayState> Days { get; set; } = new();

    // ── ladder ──
    public int Group { get; set; } = 1;
    public double RowHeight { get; set; } = 18;
    public Columns Columns { get; set; } = new();
    /// <summary>Ladder column widths (relative weights, by column name), set by dragging the header dividers.</summary>
    public Dictionary<string, double> ColWidths { get; set; } = new();
    /// <summary>Width of the right column (tape, ticket, buttons) in px, set by dragging its edge.</summary>
    public double SideWidth { get; set; } = 372;
    public bool ShowTrend { get; set; } = true;
    /// <summary>Heat · pull/stack window in minutes: 5, 15, 30 or 60.</summary>
    public int HeatMinutes { get; set; } = 5;
    /// <summary>Heat contrast: 0 Soft, 1 Normal, 2 Sharp — how hard typical-size levels fade (HeatShade.Gammas).</summary>
    public int HeatContrast { get; set; } = 1;
    /// <summary>Naked POC lines: each period's POC, drawn until price trades there again.</summary>
    public bool NpocOn { get; set; } = true;
    /// <summary>nPOC period in minutes: 5, 15, 30 or 60.</summary>
    public int NpocMinutes { get; set; } = 15;
    public bool ShowLog { get; set; } = true;
    /// <summary>The ticket settings under the tape; hidden = tape + order buttons only.</summary>
    public bool ShowTicket { get; set; } = true;
    public bool ShowJournal { get; set; }
    public bool ShowOutlook { get; set; } = true;
    public bool OutlookCollapsed { get; set; }
    public int FlowSeconds { get; set; } = 15;
    /// <summary>Tape and order-log text size, px (rows scale with it). Ctrl+wheel over either changes it.</summary>
    public double TapeFont { get; set; } = DefaultTapeFont;
    public double LogFont { get; set; } = 11;
    /// <summary>The tape shows only prints of at least this many contracts (1 = every print).</summary>
    public long TapeMinSize { get; set; } = 1;
    public const double DefaultTapeFont = 11.5;
    public static double ClampFont(double v) => Math.Clamp(Math.Round(v * 2) / 2, 8, 22);
    /// <summary>Colour scheme: token → #rrggbb, only where it differs from the default (Pal.Tokens).</summary>
    public Dictionary<string, string> Colors { get; set; } = new();
    public TrendConfig St1 { get; set; } = TrendConfig.DefaultT1;
    public TrendConfig St2 { get; set; } = TrendConfig.DefaultT2;
    public PaxOrCfg PaxOr { get; set; } = new();
    public GexCfg Gex { get; set; } = new();
    public IbCfg Ib { get; set; } = new();
    /// <summary>Legacy: per-instrument indicator settings from older files, read once by MigrateIndicators.</summary>
    public Dictionary<string, IndicatorCfg> Indicators { get; set; } = new();

    /// <summary>The indicator settings: ONE set for every instrument (owner, 2026-10-13).</summary>
    public IndicatorCfg? Ind { get; set; }
    public IndicatorCfg IndicatorsFor(string inst) => Ind ??= new IndicatorCfg();
    public void SetIndicators(string inst, IndicatorCfg c) => Ind = c;

    /// <summary>Older files kept indicators per instrument root; adopt the current instrument's set (else
    /// any) as the one set, then drop the per-instrument map.</summary>
    private void MigrateIndicators()
    {
        if (Ind == null && Indicators.Count > 0)
            Ind = Indicators.TryGetValue(CmeSpecs.RootOf(Instrument), out var mine) ? mine : Indicators.Values.First();
        Indicators.Clear();
    }

    public DrawdownRules Rules => new(Start, MaxDrawdown, DrawdownKind, LockAt, DailyLoss);
    public DayLimits Limits => new(DayLossUsd, DayMaxTrades, CooldownSec);

    /// <summary>%APPDATA%\BookDepthIQ-NT; a dev run can point BDIQ_SETTINGS_DIR elsewhere so it never touches the owner's settings.</summary>
    public static string Dir => Environment.GetEnvironmentVariable("BDIQ_SETTINGS_DIR") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BookDepthIQ-NT");
    private static string FilePath => Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Settings Load()
    {
        Settings s;
        try { s = File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Opts) ?? new() : new(); }
        catch { s = new(); }
        Pal.Apply(s.Colors);
        s.MigrateIndicators();
        s.ShowJournal = false; // legacy embedded journal was replaced by the dedicated window
        return s;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
        }
        catch { /* settings are a convenience; the DOM works without them */ }
    }
}
