namespace DaxDiff.Desktop.Services;

/// <summary>Per-user locations. Nothing here is shared or roamed.</summary>
public static class AppPaths
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DAXDiff");

    public static string Logs => Path.Combine(Root, "logs");

    /// <summary>User overrides of appsettings.json (client ID, tenant).</summary>
    public static string UserSettings => Path.Combine(Root, "settings.json");

    public static string RecentProjects => Path.Combine(Root, "recent.json");
}
