using System.Text.RegularExpressions;

namespace BookDepthIQ.Core;

// Multiple take-profit targets and scale orders, in whole contracts. Pure.
// Port of BookDepthIQ-Vest src/live/targets.ts. Distances, split and scale range are the user's
// numbers (CLAUDE.md rule 3); nothing here picks one.

public sealed record Target(int Ticks, double Pct);

public sealed record Piece(long Ticks, int Qty);

public sealed record Split(bool Ok, List<Piece> Legs, string Message = "")
{
    public static Split Fail(string m) => new(false, new(), m);
}

public enum SkewDir { Start, Even, End }

public static class Targets
{
    public const int ScaleMin = 2, ScaleMax = 10;

    /// <summary>"50/30/20" → [50, 30, 20]. Blank or bad parts read as 0.</summary>
    public static double[] ParseSplit(string text) =>
        Regex.Split(text ?? "", @"[\s/,]+").Where(x => x.Length > 0)
            .Select(x => double.TryParse(x, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? Math.Max(0, v) : 0).ToArray();

    /// <summary>The take-profit legs for qty contracts: one per target that is on, each further than the
    /// last. The last leg takes the rounding left over, so the legs add up to the full size.</summary>
    public static Split SplitTargets(int qty, IReadOnlyList<Target> targets)
    {
        var on = targets.Where(t => t.Ticks > 0).ToList();
        if (on.Count == 0) return new(true, new());
        for (var i = 1; i < on.Count; i++) if (on[i].Ticks <= on[i - 1].Ticks) return Split.Fail($"TP{i + 1} must be further than TP{i}");
        if (on.Count == 1) return new(true, new() { new Piece(on[0].Ticks, qty) });
        var sum = on.Sum(t => t.Pct);
        double Share(Target t) => sum > 0 ? t.Pct / sum : 1.0 / on.Count;
        var n = on.Select((t, i) => i < on.Count - 1 ? (int)Math.Floor(qty * Share(t)) : 0).ToArray();
        n[^1] = qty - n.Sum();
        if (n.Any(x => x <= 0)) return Split.Fail($"{qty} ct is too small to split into {on.Count} targets at this split");
        return new(true, on.Select((t, i) => new Piece(t.Ticks, n[i])).ToList());
    }

    /// <summary>A scale order: count limits evenly spaced from startTicks to endTicks; sizes even, or
    /// weighted by skew^(i/(count−1)) so the largest sits at the start or the end. The rounding remainder
    /// goes to the largest order so the total is exactly qty. Start → end order.</summary>
    public static Split ScaleOrders(int qty, long startTicks, long endTicks, int count, double skew, SkewDir dir)
    {
        if (count < ScaleMin || count > ScaleMax) return Split.Fail($"scale count must be {ScaleMin}–{ScaleMax}");
        if (!(skew >= 1)) return Split.Fail("size skew must be 1 or more (1 = even)");
        if (Math.Abs(endTicks - startTicks) < count - 1) return Split.Fail($"the scale range is too narrow for {count} orders on different prices");
        var w = Enumerable.Range(0, count).Select(i => dir == SkewDir.Even ? 1 : Math.Pow(skew, i / (double)(count - 1))).ToArray();
        if (dir == SkewDir.Start) Array.Reverse(w);
        var sum = w.Sum();
        var n = w.Select(x => (int)Math.Floor(qty * x / sum)).ToArray();
        var big = Array.IndexOf(w, w.Max());
        n[big] += qty - n.Sum();
        if (n.Any(x => x <= 0)) return Split.Fail($"{qty} ct is too small for {count} scale orders");
        return new(true, n.Select((q, i) => new Piece(startTicks + (long)Math.Round((endTicks - startTicks) * i / (double)(count - 1)), q)).ToList());
    }
}
