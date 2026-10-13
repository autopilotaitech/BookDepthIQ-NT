using System.Text.Json;
using BookDepthIQ.Core;

namespace BookDepthIQ.Dom;

/// <summary>The user's settings, saved to %APPDATA%\BookDepthIQ-NT\settings.json. Every risk number
/// here is the user's own; nothing is hard-coded into an order.</summary>
public sealed class Settings
{
    public string Instrument { get; set; } = "NQ 12-26";
    public string Account { get; set; } = "Sim101";
    public SizeMode SizeMode { get; set; } = SizeMode.Contracts;
    public double SizeInput { get; set; } = 1;
    public int TpTicks { get; set; } = 32;
    public int SlTicks { get; set; } = 16;
    public bool BracketOn { get; set; } = true;
    /// <summary>Commission + exchange/NFA fees per contract per side, $ (check your broker's).</summary>
    public double FeePerSide { get; set; } = 2.09;
    public int MaxContracts { get; set; }

    // Prop account rules (the user's numbers): start, max drawdown, trailing type, lock, daily loss.
    public double Start { get; set; } = 50000;
    public double MaxDrawdown { get; set; } = 2500;
    public DrawdownKind DrawdownKind { get; set; } = DrawdownKind.TrailingEod;
    public double LockAt { get; set; } = 50100;
    public double DailyLoss { get; set; }
    /// <summary>Peak equity/balance per account, kept for trailing drawdown.</summary>
    public Dictionary<string, double> Peaks { get; set; } = new();

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BookDepthIQ-NT", "settings.json");
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };

    public static Settings Load()
    {
        try { return File.Exists(FilePath) ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Opts) ?? new() : new(); }
        catch { return new(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
        }
        catch { /* settings are a convenience; the DOM works without them */ }
    }

    public DrawdownRules Rules => new(Start, MaxDrawdown, DrawdownKind, LockAt, DailyLoss);
}
