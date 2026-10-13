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
    /// <summary>Naked POC lines: each period's POC, drawn until price trades there again.</summary>
    public bool NpocOn { get; set; } = true;
    /// <summary>nPOC period in minutes: 5, 15, 30 or 60.</summary>
    public int NpocMinutes { get; set; } = 15;
    public bool ShowLog { get; set; } = true;
    /// <summary>The ticket settings under the tape; hidden = tape + order buttons only.</summary>
    public bool ShowTicket { get; set; } = true;
    public bool ShowJournal { get; set; }
    /// <summary>Tape and order-log text size, px (rows scale with it). Ctrl+wheel over either changes it.</summary>
    public double TapeFont { get; set; } = DefaultTapeFont;
    public double LogFont { get; set; } = 11;
    public const double DefaultTapeFont = 11.5;
    public static double ClampFont(double v) => Math.Clamp(Math.Round(v * 2) / 2, 8, 22);
    /// <summary>Colour scheme: token → #rrggbb, only where it differs from the default (Pal.Tokens).</summary>
    public Dictionary<string, string> Colors { get; set; } = new();
    public TrendConfig St1 { get; set; } = TrendConfig.DefaultT1;
    public TrendConfig St2 { get; set; } = TrendConfig.DefaultT2;
    public PaxOrCfg PaxOr { get; set; } = new();
    public GexCfg Gex { get; set; } = new();
    /// <summary>Per-instrument indicator settings (key: instrument root, e.g. NQ).</summary>
    public Dictionary<string, IndicatorCfg> Indicators { get; set; } = new();

    public IndicatorCfg IndicatorsFor(string inst) => Indicators.TryGetValue(CmeSpecs.RootOf(inst), out var c) ? c : new IndicatorCfg();
    public void SetIndicators(string inst, IndicatorCfg c) => Indicators[CmeSpecs.RootOf(inst)] = c;

    public DrawdownRules Rules => new(Start, MaxDrawdown, DrawdownKind, LockAt, DailyLoss);
    public DayLimits Limits => new(DayLossUsd, DayMaxTrades, CooldownSec);

    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BookDepthIQ-NT");
    private static string FilePath => Path.Combine(Dir, "settings.json");
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Settings Load()
    {
        Settings s;
        try { s = File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Opts) ?? new() : new(); }
        catch { s = new(); }
        Pal.Apply(s.Colors);
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
