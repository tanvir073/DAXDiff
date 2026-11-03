using System.Globalization;
using System.Runtime.CompilerServices;

namespace DaxDiff.Tests.Unit.Fixtures;

/// <summary>
/// Tests assert English messages, so they run with the English (neutral) UI text whatever the machine's display
/// language. Formatting culture is left alone.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void UseEnglishText()
    {
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
