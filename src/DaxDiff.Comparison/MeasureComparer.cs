using DaxDiff.Domain;
using System.Globalization;
using DaxDiff.Comparison.Resources;

namespace DaxDiff.Comparison;

/// <summary>Compares one paired measure value under the configured null, numeric and text policies (§4.3).</summary>
public static class MeasureComparer
{
    public static MeasureCell Compare(string alias, object? a, object? b, ComparisonOptions options)
    {
        a = a is DBNull ? null : a;
        b = b is DBNull ? null : b;

        if (a is null && b is null)
            return new MeasureCell { Alias = alias, IsMatch = true };

        if (a is null || b is null)
            return CompareWithBlank(alias, a, b, options);

        if (ValueCanonicalizer.TryToDecimal(a, out var da) && ValueCanonicalizer.TryToDecimal(b, out var db))
            return CompareNumbers(alias, a, b, da, db, options);

        if (ValueCanonicalizer.IsNumeric(a) && ValueCanonicalizer.IsNumeric(b))
            return CompareDoubles(alias, a, b, options);

        return (a, b) switch
        {
            (string sa, string sb) => Cell(alias, a, b,
                string.Equals(sa, sb, options.TextComparison == TextComparison.OrdinalIgnoreCase
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal),
                Strings.Val_TextDiffers),
            (bool ba, bool bb) => Cell(alias, a, b, ba == bb, Strings.Val_BooleanDiffers),
            (DateTime ta, DateTime tb) => Cell(alias, a, b, ta == tb, Strings.Val_DateDiffers),
            _ => Cell(alias, a, b, false, string.Format(CultureInfo.CurrentCulture, Strings.Val_TypeDiffers, a.GetType().Name, b.GetType().Name)),
        };
    }

    /// <summary>
    /// The suggested predicate: abs(A−B) ≤ max(absTolerance, relativeTolerance × max(|A|, |B|)).
    /// In Exact mode only identical canonical decimals match.
    /// </summary>
    public static bool WithinTolerance(decimal a, decimal b, ToleranceSettings tolerance)
    {
        var diff = Math.Abs(a - b);
        if (tolerance.Mode == ToleranceMode.Exact)
            return diff == 0;

        var allowed = Math.Max(tolerance.Absolute, tolerance.Relative * Math.Max(Math.Abs(a), Math.Abs(b)));
        return diff <= allowed;
    }

    /// <summary>Percentage difference relative to the baseline; N/A on zero denominator except both zero → 0.</summary>
    public static decimal? PercentDifference(decimal a, decimal b, PercentBaseline baseline)
    {
        if (a == 0 && b == 0)
            return 0;
        var denominator = Math.Abs(baseline == PercentBaseline.A ? a : b);
        if (denominator == 0)
            return null;
        return (a - b) / denominator;
    }

    private static MeasureCell CompareNumbers(string alias, object a, object b, decimal da, decimal db, ComparisonOptions options)
    {
        var match = WithinTolerance(da, db, options.Tolerance);
        string? reason = null;
        if (!match)
            reason = options.Tolerance.Mode == ToleranceMode.Exact ? Strings.Val_ValuesDiffer : Strings.Val_ExceedsTolerance;

        return new MeasureCell
        {
            Alias = alias,
            A = a,
            B = b,
            Delta = da - db,
            DeltaPercent = PercentDifference(da, db, options.PercentBaseline),
            IsMatch = match,
            Reason = reason,
        };
    }

    private static MeasureCell CompareDoubles(string alias, object a, object b, ComparisonOptions options)
    {
        var xa = Convert.ToDouble(a, System.Globalization.CultureInfo.InvariantCulture);
        var xb = Convert.ToDouble(b, System.Globalization.CultureInfo.InvariantCulture);
        bool match;
        if (double.IsNaN(xa) || double.IsNaN(xb))
            match = double.IsNaN(xa) && double.IsNaN(xb);
        else if (options.Tolerance.Mode == ToleranceMode.Exact || double.IsInfinity(xa) || double.IsInfinity(xb))
            match = xa.Equals(xb);
        else
        {
            var allowed = Math.Max((double)options.Tolerance.Absolute,
                (double)options.Tolerance.Relative * Math.Max(Math.Abs(xa), Math.Abs(xb)));
            match = Math.Abs(xa - xb) <= allowed;
        }

        return Cell(alias, a, b, match, Strings.Val_ValuesDifferOutsideDecimal);
    }

    private static MeasureCell CompareWithBlank(string alias, object? a, object? b, ComparisonOptions options)
    {
        var other = a ?? b;
        var blankSide = a is null ? "A" : "B";
        var otherIsNumber = ValueCanonicalizer.TryToDecimal(other, out var value);

        var match = options.NullPolicy == NullPolicy.BlankEqualsZero && otherIsNumber && value == 0;
        decimal? delta = null;
        decimal? pct = null;
        if (otherIsNumber)
        {
            var da = a is null ? 0 : value;
            var db = b is null ? 0 : value;
            delta = da - db;
            pct = PercentDifference(da, db, options.PercentBaseline);
        }

        return new MeasureCell
        {
            Alias = alias,
            A = a,
            B = b,
            Delta = delta,
            DeltaPercent = pct,
            IsMatch = match,
            Reason = match ? null : string.Format(CultureInfo.CurrentCulture, Strings.Val_BlankIn, blankSide),
        };
    }

    private static MeasureCell Cell(string alias, object a, object b, bool match, string reason) => new()
    {
        Alias = alias,
        A = a,
        B = b,
        IsMatch = match,
        Reason = match ? null : reason,
    };
}
