using System.Globalization;
using System.Text;
using DaxDiff.Domain;

namespace DaxDiff.Comparison;

/// <summary>Decimal-safe canonical forms for keys and numeric values (§4.3, §4.4).</summary>
public static class ValueCanonicalizer
{
    private const string NullToken = "∅";

    /// <summary>Converts any CLR numeric to decimal. Doubles outside decimal range, NaN and infinity return false.</summary>
    public static bool TryToDecimal(object? value, out decimal result)
    {
        result = 0;
        switch (value)
        {
            case decimal d:
                result = d;
                return true;
            case int or long or short or byte or sbyte or ushort or uint or ulong:
                result = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                return true;
            case double dbl when double.IsFinite(dbl) && Math.Abs(dbl) < 7.9e28:
                result = (decimal)dbl;
                return true;
            case float f when float.IsFinite(f) && Math.Abs(f) < 7.9e28f:
                result = (decimal)f;
                return true;
            default:
                return false;
        }
    }

    public static bool IsNumeric(object? value) =>
        value is decimal or double or float or int or long or short or byte or sbyte or ushort or uint or ulong;

    /// <summary>Removes trailing zeros so 1.20 and 1.2 canonicalize identically.</summary>
    public static decimal Normalize(decimal value) => value / 1.0000000000000000000000000000m;

    /// <summary>Canonical text for one key component. Text identifiers such as "0012" are never cast to numbers.</summary>
    public static string CanonicalKeyPart(object? value, KeyNormalization policy)
    {
        switch (value)
        {
            case null or DBNull:
                return policy.BlankEqualsEmpty ? "s:" : NullToken;
            case string s:
                if (policy.TrimWhitespace)
                    s = s.Trim();
                s = policy.Unicode switch
                {
                    UnicodeNormalization.FormC => s.Normalize(NormalizationForm.FormC),
                    UnicodeNormalization.FormKC => s.Normalize(NormalizationForm.FormKC),
                    _ => s,
                };
                if (policy.CaseInsensitive)
                    s = s.ToUpperInvariant();
                if (s.Length == 0 && policy.BlankEqualsEmpty)
                    return "s:";
                return "s:" + s;
            case bool b:
                return b ? "b:1" : "b:0";
            case DateTime dt:
                return "d:" + (policy.DateOnly ? dt.Date : dt).ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            case DateTimeOffset dto:
                return "d:" + (policy.DateOnly ? dto.UtcDateTime.Date : dto.UtcDateTime).ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);
            default:
                if (TryToDecimal(value, out var d))
                    return "n:" + Normalize(d).ToString(CultureInfo.InvariantCulture);
                if (value is double dbl)
                    return "n:" + dbl.ToString("R", CultureInfo.InvariantCulture);
                return "o:" + Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Unambiguous composite key: each part is length-prefixed, so separators inside values cannot collide.</summary>
    public static string CompositeKey(IReadOnlyList<object?> parts, KeyNormalization policy)
    {
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            var canonical = CanonicalKeyPart(part, policy);
            sb.Append(canonical.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(canonical).Append('|');
        }

        return sb.ToString();
    }

    /// <summary>Human-readable form of a key component for grids and exports.</summary>
    public static string Display(object? value) => value switch
    {
        null or DBNull => "(Blank)",
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };
}
