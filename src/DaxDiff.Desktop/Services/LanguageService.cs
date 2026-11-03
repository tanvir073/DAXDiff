using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DaxDiff.Desktop.Resources;

namespace DaxDiff.Desktop.Services;

/// <summary>A user-interface language; an empty <see cref="Code"/> follows the Windows display language.</summary>
public sealed record AppLanguage(string Code, string Name);

/// <summary>
/// Chooses the user-interface language (English or Swedish). Only the UI culture changes: numbers and dates keep the
/// Windows regional format, and logs stay in English. Applied once at startup, so a change needs a restart.
/// </summary>
public static class LanguageService
{
    /// <summary>Languages with translated text. English is the neutral language, used for anything not translated.</summary>
    public static IReadOnlyList<AppLanguage> Languages { get; } =
    [
        new("", Strings.Language_System),
        new("en", "English"),
        new("sv", "Svenska"),
    ];

    /// <summary>The saved choice ("" = same as Windows).</summary>
    public static string Current { get; private set; } = "";

    public static void Apply(string? code)
    {
        Current = Languages.Any(l => l.Code == code) ? code! : "";
        if (Current.Length == 0)
            return;
        var culture = CultureInfo.GetCultureInfo(Current);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }

    /// <summary>Remembers the language in the user settings file next to the theme.</summary>
    public static void Save(string code) => UserSettings.Set("Appearance", "Language", code);
}

/// <summary>Writes single values to the user settings file, keeping everything else in it.</summary>
public static class UserSettings
{
    public static void Set(string section, string key, string value)
    {
        JsonObject root;
        try
        {
            root = File.Exists(AppPaths.UserSettings) ? JsonNode.Parse(File.ReadAllText(AppPaths.UserSettings)) as JsonObject ?? [] : [];
        }
        catch (JsonException)
        {
            root = [];
        }

        var node = root[section] as JsonObject ?? [];
        node[key] = value;
        root[section] = node;
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.UserSettings, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
