namespace BookDepthIQ.Core;

/// <summary>A futures contract's price grid and money per tick.</summary>
public sealed record CmeSpec(string Root, string Name, double TickSize, double TickValue)
{
    /// <summary>$ per 1.00 of price per contract (NinjaTrader's PointValue).</summary>
    public double PointValue => TickValue / TickSize;
}

/// <summary>
/// Fallback specs for the common CME Group contracts (CME, CBOT, NYMEX, COMEX contract specs).
/// The bridge sends NinjaTrader's own TickSize and PointValue for the instrument on screen; this
/// table is used only before that arrives and for planning in RISK.
/// </summary>
public static class CmeSpecs
{
    public static readonly IReadOnlyList<CmeSpec> All = new[]
    {
        new CmeSpec("NQ", "E-mini Nasdaq-100", 0.25, 5.00),
        new CmeSpec("MNQ", "Micro E-mini Nasdaq-100", 0.25, 0.50),
        new CmeSpec("ES", "E-mini S&P 500", 0.25, 12.50),
        new CmeSpec("MES", "Micro E-mini S&P 500", 0.25, 1.25),
        new CmeSpec("YM", "E-mini Dow", 1.00, 5.00),
        new CmeSpec("MYM", "Micro E-mini Dow", 1.00, 0.50),
        new CmeSpec("RTY", "E-mini Russell 2000", 0.10, 5.00),
        new CmeSpec("M2K", "Micro E-mini Russell 2000", 0.10, 0.50),
        new CmeSpec("GC", "Gold", 0.10, 10.00),
        new CmeSpec("MGC", "Micro Gold", 0.10, 1.00),
        new CmeSpec("SI", "Silver", 0.005, 25.00),
        new CmeSpec("SIL", "Micro Silver", 0.005, 5.00),
        new CmeSpec("HG", "Copper", 0.0005, 12.50),
        new CmeSpec("CL", "Crude Oil", 0.01, 10.00),
        new CmeSpec("MCL", "Micro Crude Oil", 0.01, 1.00),
        new CmeSpec("NG", "Natural Gas", 0.001, 10.00),
        new CmeSpec("6E", "Euro FX", 0.00005, 6.25),
        new CmeSpec("ZB", "30-Year T-Bond", 1.0 / 32, 31.25),
        new CmeSpec("ZN", "10-Year T-Note", 1.0 / 64, 15.625),
    };

    /// <summary>Spec for a root or a NinjaTrader instrument name ("NQ 12-26" → NQ).</summary>
    public static CmeSpec? Find(string nameOrRoot)
    {
        var root = RootOf(nameOrRoot);
        return All.FirstOrDefault(s => string.Equals(s.Root, root, StringComparison.OrdinalIgnoreCase));
    }

    public static string RootOf(string name) => (name ?? "").Trim().Split(' ')[0].ToUpperInvariant();
}
