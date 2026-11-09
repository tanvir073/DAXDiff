using System.Globalization;

namespace DaxDiff.Presentation.Resources;

/// <summary>Fills a localized composite format from <see cref="Strings"/> with the user's regional number and date formats.</summary>
public static class Localized
{
    public static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);
}
