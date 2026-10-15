using BookDepthIQ.Core;
using Xunit;

public class HeatShadeTests
{
    // A book like the owner's screenshot: most levels 40–100 lots, a few big ones.
    private static readonly double[] Book = { 42, 45, 37, 41, 82, 57, 38, 190, 51, 57, 56, 49, 41, 74, 53, 44, 60, 68, 48, 78, 71, 69, 60, 56, 47, 51, 50, 58, 86, 83, 80, 82, 75, 90, 75, 51, 88, 66, 85, 146, 87, 76, 79, 92, 107, 74, 77, 70, 303, 60, 58, -40, -49, -53 };

    [Fact]
    public void ScaleIsTypicalAndHigh()
    {
        var s = HeatShade.Measure(Book);
        Assert.InRange(s.Typical, 55, 70);
        Assert.True(s.High >= 146);
        Assert.Equal(new HeatShade.Scale(1, 2), HeatShade.Measure(new double[] { 0, 0 }));
    }

    [Fact]
    public void TypicalLevelsFadeAndBigOnesStandOut()
    {
        var s = HeatShade.Measure(Book);
        var typical = HeatShade.Shade(60, s).Alpha;
        var big = HeatShade.Shade(190, s).Alpha;
        Assert.True(typical <= 0.08, $"typical {typical}");
        Assert.True(big >= 0.6, $"big {big}");
        Assert.Equal(HeatShade.Floor, HeatShade.Shade(5, s).Alpha);       // thin is a faint trace, still visible
        Assert.True(HeatShade.Shade(303, s).Alpha >= 0.94);
    }

    [Fact]
    public void BrightnessOnlyGrowsWithSize()
    {
        var s = HeatShade.Measure(Book);
        double prev = 0;
        for (var v = 1; v <= 400; v += 3)
        {
            var a = HeatShade.Shade(v, s).Alpha;
            Assert.True(a >= prev);
            prev = a;
        }
    }

    [Fact]
    public void WallIsThreeTimesTypicalOnEitherSide()
    {
        var s = new HeatShade.Scale(60, 200);
        Assert.False(HeatShade.Shade(179, s).Wall);
        Assert.True(HeatShade.Shade(180, s).Wall);
        Assert.True(HeatShade.Shade(-180, s).Wall);
    }

    [Fact]
    public void SharpFadesMoreThanSoft()
    {
        var s = new HeatShade.Scale(60, 200);
        var soft = HeatShade.Shade(110, s, 0).Alpha;
        var normal = HeatShade.Shade(110, s, 1).Alpha;
        var sharp = HeatShade.Shade(110, s, 2).Alpha;
        Assert.True(soft > normal && normal > sharp);
        Assert.Equal(HeatShade.Shade(110, s, 2).Alpha, HeatShade.Shade(110, s, 9).Alpha);   // out of range clamps
    }
}
