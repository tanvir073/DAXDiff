using System.Runtime.InteropServices;

namespace DaxDiff.Application;

/// <summary>
/// Public web addresses of the DAXDiff project: documentation, releases and the bug-report form. The desktop and web
/// apps both open these, so they are kept in one place.
/// </summary>
public static class ProductLinks
{
    public const string Repository = "https://github.com/tanvir073/DAXDiff";

    public const string UserGuide = Repository + "/blob/main/docs/user/README.md";

    /// <summary>Entra ID app registration and tenant settings, for the administrator who sets DAXDiff up.</summary>
    public const string AdminGuide = Repository + "/blob/main/docs/user/administrators.md";

    public const string Releases = Repository + "/releases";

    /// <summary>
    /// A new GitHub issue from the bug-report form (<c>.github/ISSUE_TEMPLATE/bug_report.yml</c>), with the app,
    /// version and operating system filled in. The query keys are the form's field ids.
    /// </summary>
    /// <param name="edition">Which app is reporting, such as <c>Desktop</c> or <c>Web</c>.</param>
    /// <param name="os">Operating system; null or empty leaves the field for the reporter.</param>
    public static string NewBugReport(string edition, string? os = null)
    {
        var url = $"{Repository}/issues/new?template=bug_report.yml&labels=bug" +
                  $"&version={Uri.EscapeDataString($"{edition} {ComparisonRunner.AppVersion}")}";
        return string.IsNullOrWhiteSpace(os) ? url : url + $"&os={Uri.EscapeDataString(os)}";
    }

    /// <summary>The operating system as .NET reports it, such as <c>Microsoft Windows 10.0.26100</c>.</summary>
    public static string CurrentOs => $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})";
}
