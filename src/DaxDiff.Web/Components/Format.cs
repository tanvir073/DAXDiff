using System.Globalization;
using DaxDiff.Domain;

namespace DaxDiff.Web.Components;

/// <summary>Display formatting for result values, independent of the browser's locale so A and B always read the same.</summary>
public static class Format
{
    public static string Value(object? value) => value switch
    {
        null => "(blank)",
        string s when s.Length == 0 => "(empty)",
        string s => s,
        DateTime d => d.TimeOfDay == TimeSpan.Zero ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : d.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        decimal m => m.ToString("#,0.############", CultureInfo.InvariantCulture),
        double f => f.ToString("#,0.############", CultureInfo.InvariantCulture),
        long or int => Convert.ToInt64(value, CultureInfo.InvariantCulture).ToString("#,0", CultureInfo.InvariantCulture),
        bool b => b ? "TRUE" : "FALSE",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>Key values are identifiers (years, codes), so numbers are shown without digit grouping.</summary>
    public static string Key(object? value) => value switch
    {
        long or int or decimal or double => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
        _ => Value(value),
    };

    public static string Delta(decimal? value) => value is { } d ? d.ToString("+#,0.############;-#,0.############;0", CultureInfo.InvariantCulture) : "";

    /// <summary>A fraction (0.05) shown as a signed percentage (+5%).</summary>
    public static string Percent(decimal? value) => value is { } p ? p.ToString("+0.##%;-0.##%;0%", CultureInfo.InvariantCulture) : "";

    public static string Rate(decimal value) => value.ToString("0.##%", CultureInfo.InvariantCulture);

    public static string Count(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Utc(DateTimeOffset? value) => value is { } v ? v.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC" : "unknown";

    public static string StatusTone(RowStatus status) => status switch
    {
        RowStatus.Match => "ok",
        RowStatus.Mismatch => "bad",
        RowStatus.InvalidKey => "warn",
        _ => "info",
    };

    public static string RunTone(RunStatus status) => status switch
    {
        RunStatus.Complete => "ok",
        RunStatus.Partial => "warn",
        RunStatus.Failed => "bad",
        _ => "info",
    };
}
