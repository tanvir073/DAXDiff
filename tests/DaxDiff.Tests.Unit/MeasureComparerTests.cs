using DaxDiff.Comparison;
using DaxDiff.Domain;

namespace DaxDiff.Tests.Unit;

public class MeasureComparerTests
{
    private static readonly ComparisonOptions Strict = new();

    [Theory]
    [InlineData(110, 100, PercentBaseline.B, 0.1)]
    [InlineData(110, 100, PercentBaseline.A, 0.0909090909090909090909090909)]
    [InlineData(0, 0, PercentBaseline.A, 0)]
    public void PercentDifference(double a, double b, PercentBaseline baseline, double expected)
    {
        var pct = MeasureComparer.PercentDifference((decimal)a, (decimal)b, baseline);
        Assert.Equal((decimal)expected, Math.Round(pct!.Value, 12), 12);
    }

    [Fact]
    public void PercentDifference_ZeroDenominator_IsNotAvailable()
    {
        Assert.Null(MeasureComparer.PercentDifference(0m, 5m, PercentBaseline.A));
        Assert.Null(MeasureComparer.PercentDifference(5m, 0m, PercentBaseline.B));
    }

    [Fact]
    public void ExactMode_UsesDecimalSafeCanonicalization()
    {
        // 0.1 + 0.2 as a double is 0.30000000000000004; decimal canonicalization treats it as 0.3.
        var cell = MeasureComparer.Compare("M", 0.1d + 0.2d, 0.3m, Strict);
        Assert.True(cell.IsMatch);
    }

    [Theory]
    [InlineData(TextComparison.Ordinal, false)]
    [InlineData(TextComparison.OrdinalIgnoreCase, true)]
    public void TextMeasures_UseSelectedComparison(TextComparison mode, bool expected)
    {
        var cell = MeasureComparer.Compare("M", "Gold", "GOLD", Strict with { TextComparison = mode });
        Assert.Equal(expected, cell.IsMatch);
    }

    [Fact]
    public void DifferentTypes_AreMismatches()
    {
        var cell = MeasureComparer.Compare("M", "10", 10m, Strict);
        Assert.False(cell.IsMatch);
        Assert.Contains("Type differs", cell.Reason);
    }

    [Fact]
    public void BlankVsNumber_ReportsBlankSideAndDelta()
    {
        var cell = MeasureComparer.Compare("M", null, 5m, Strict);
        Assert.False(cell.IsMatch);
        Assert.Equal("BLANK in A", cell.Reason);
        Assert.Equal(-5m, cell.Delta);
    }

    [Fact]
    public void HugeDoubles_FallBackToDoubleComparison()
    {
        Assert.True(MeasureComparer.Compare("M", 1e300, 1e300, Strict).IsMatch);
        Assert.False(MeasureComparer.Compare("M", 1e300, 2e300, Strict).IsMatch);
        Assert.True(MeasureComparer.Compare("M", double.NaN, double.NaN, Strict).IsMatch);
    }

    [Fact]
    public void TolerancePredicate_MatchesSpecification()
    {
        var tolerance = new ToleranceSettings { Mode = ToleranceMode.Tolerance, Absolute = 1m, Relative = 0.001m };
        Assert.True(MeasureComparer.WithinTolerance(5000m, 5005m, tolerance)); // 5 ≤ max(1, 5.005)
        Assert.False(MeasureComparer.WithinTolerance(5000m, 5006m, tolerance)); // 6 > 5.006
        Assert.True(MeasureComparer.WithinTolerance(1m, 2m, tolerance)); // 1 ≤ max(1, 0.002)
    }
}
