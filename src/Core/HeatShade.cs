namespace BookDepthIQ.Core;

/// <summary>
/// How bright each HEAT cell draws (owner, 2026-10-14: "lower intensity for low liquidity so the user can see higher
/// liquidity"). Brightness is measured from what is TYPICAL on screen, not from zero: a level at or below the median
/// resting size is a faint trace; above it brightness climbs a curve to the 98th percentile, so only real size
/// stands out. A wall (yellow) is ≥ 3× the typical size showing.
/// </summary>
public static class HeatShade
{
    /// <summary>Faint trace for typical / thin levels; full brightness.</summary>
    public const double Floor = 0.05, Ceiling = 0.95;
    public const double WallMultiple = 3;

    /// <summary>Settings › Heat contrast: Soft, Normal, Sharp → the curve's power.</summary>
    public static readonly double[] Gammas = { 1.2, 1.8, 2.6 };

    public readonly record struct Scale(double Typical, double High);

    /// <summary>Median and 98th percentile of the non-zero |sizes| on screen.</summary>
    public static Scale Measure(IEnumerable<double> values)
    {
        var v = values.Where(x => x != 0 && double.IsFinite(x)).Select(Math.Abs).ToList();
        if (v.Count == 0) return new Scale(1, 2);
        v.Sort();
        double At(double q) => v[(int)Math.Min(v.Count - 1, Math.Floor(v.Count * q))];
        var typical = Math.Max(1, At(0.5));
        return new Scale(typical, Math.Max(typical * 1.5, At(0.98)));
    }

    /// <summary>Opacity 0.05–0.95 for a cell of size |v| and whether it is a wall.</summary>
    public static (double Alpha, bool Wall) Shade(double v, Scale s, int contrast = 1)
    {
        var a = Math.Abs(v);
        var n = Math.Clamp((a - s.Typical) / (s.High - s.Typical), 0, 1);
        var gamma = Gammas[Math.Clamp(contrast, 0, Gammas.Length - 1)];
        return (Floor + (Ceiling - Floor) * Math.Pow(n, gamma), a >= WallMultiple * s.Typical);
    }
}
