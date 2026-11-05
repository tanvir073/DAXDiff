using DaxDiff.Presentation.Resources;

namespace DaxDiff.Presentation.Services;

/// <summary>
/// Colour tokens of one theme. Values are CSS colours (#rrggbb or rgba()).
/// </summary>
public sealed record ThemeTokens(
    string Page, string Surface, string Nav, string Text, string Muted, string Heading,
    string Border, string Strong, string Field,
    string Header, string HeaderText, string HeaderMuted, string HeaderLine, string HeaderField,
    string Accent, string OnAccent, string AccentHover, string Link,
    string SelBg, string SelText, string TileBg, string TileText,
    string ChipA, string ChipAText,
    string Hero, string HeroText, string HeroMuted,
    string Success, string Danger);

/// <summary>A colour theme. Each desktop app turns its tokens into brushes (see <see cref="ThemeCatalog.Applier"/>).</summary>
public sealed record AppTheme(string Id, string Name, string Description, ThemeTokens Tokens);

/// <summary>
/// The colour themes, the current one and the saved choice. Applying a theme to the UI is the app's job: it sets
/// <see cref="Applier"/> at startup, and <see cref="Apply"/> calls it.
/// </summary>
public static class ThemeCatalog
{
    public const string DefaultId = "light";

    public static IReadOnlyList<AppTheme> Themes { get; } =
    [
        new("light", Strings.Theme_light, Strings.Theme_light_Description, new(
            "#f4f5fa", "#ffffff", "#ffffff", "#1a1c2b", "#5b5f78", "#2b2f8f",
            "rgba(26,28,43,0.10)", "rgba(26,28,43,0.22)", "#8a8ea8",
            "#23266b", "#ffffff", "rgba(255,255,255,0.74)", "rgba(255,255,255,0.18)", "rgba(255,255,255,0.08)",
            "#4b4fd6", "#ffffff", "#3a3db8", "#3a3db8",
            "#e8e9fb", "#23266b", "#e8e9fb", "#2b2f8f",
            "#23266b", "#ffffff",
            "#2b2f8f", "#ffffff", "rgba(255,255,255,0.80)",
            "#18794e", "#c0292b")),
        new("dark", Strings.Theme_dark, Strings.Theme_dark_Description, new(
            "#12131c", "#1b1d2a", "#161824", "#e8e9f3", "#a2a5bd", "#ffffff",
            "rgba(255,255,255,0.10)", "rgba(255,255,255,0.24)", "#6f7391",
            "#0d0e16", "#e8e9f3", "rgba(232,233,243,0.72)", "rgba(255,255,255,0.14)", "rgba(255,255,255,0.06)",
            "#8f93ff", "#12131c", "#a9acff", "#a9acff",
            "#2a2d4a", "#c9cbff", "#2a2d4a", "#c9cbff",
            "#e8e9f3", "#12131c",
            "#262a52", "#ffffff", "rgba(255,255,255,0.76)",
            "#4fd08f", "#ff8a80")),
        new("contrast", Strings.Theme_contrast, Strings.Theme_contrast_Description, new(
            "#ffffff", "#ffffff", "#ffffff", "#000000", "#262626", "#000000",
            "#000000", "#000000", "#000000",
            "#000000", "#ffffff", "#ffffff", "#ffffff", "#000000",
            "#1d1db8", "#ffffff", "#14148a", "#1d1db8",
            "#000000", "#ffffff", "#000000", "#ffffff",
            "#000000", "#ffffff",
            "#000000", "#ffffff", "#ffffff",
            "#00692f", "#a8001c")),
        new("graphite", Strings.Theme_graphite, Strings.Theme_graphite_Description, new(
            "#161616", "#202020", "#1b1b1b", "#ededed", "#a8a8a8", "#ffffff",
            "rgba(255,255,255,0.10)", "rgba(255,255,255,0.24)", "#707070",
            "#0f0f0f", "#ededed", "rgba(237,237,237,0.70)", "rgba(255,255,255,0.14)", "rgba(255,255,255,0.06)",
            "#ff9a3c", "#1a1a1a", "#ffb066", "#ffb066",
            "#3a2c1f", "#ffd2a6", "#2e2620", "#ffd2a6",
            "#ededed", "#161616",
            "#2c2c2c", "#ffffff", "rgba(255,255,255,0.74)",
            "#5bcf8a", "#ff8a80")),
        new("forest", Strings.Theme_forest, Strings.Theme_forest_Description, new(
            "#f3f6f3", "#ffffff", "#ffffff", "#17241c", "#536459", "#1f4a2e",
            "rgba(23,36,28,0.10)", "rgba(23,36,28,0.22)", "#86978b",
            "#1f4a2e", "#ffffff", "rgba(255,255,255,0.76)", "rgba(255,255,255,0.20)", "rgba(255,255,255,0.08)",
            "#2f7d4a", "#ffffff", "#25653b", "#25653b",
            "#e2efe5", "#1f4a2e", "#e2efe5", "#1f4a2e",
            "#1f4a2e", "#ffffff",
            "#1f4a2e", "#ffffff", "rgba(255,255,255,0.80)",
            "#1e7a45", "#b3261e")),
        new("sand", Strings.Theme_sand, Strings.Theme_sand_Description, new(
            "#f8f5f0", "#ffffff", "#ffffff", "#2a211b", "#6b5d52", "#5a2e1c",
            "rgba(42,33,27,0.10)", "rgba(42,33,27,0.22)", "#9a8b7f",
            "#fbeee2", "#5a2e1c", "#7a6152", "rgba(90,46,28,0.16)", "#ffffff",
            "#c2562b", "#ffffff", "#a3451f", "#a3451f",
            "#f8e3d6", "#5a2e1c", "#f8e3d6", "#5a2e1c",
            "#5a2e1c", "#ffffff",
            "#5a2e1c", "#ffffff", "rgba(255,255,255,0.80)",
            "#1f7a4d", "#b3261e")),
    ];

    public static AppTheme Current { get; private set; } = Find(DefaultId);

    public static event EventHandler? ThemeChanged;

    /// <summary>Recolours the open windows; set by the app before the first <see cref="Apply"/>.</summary>
    public static Action<AppTheme>? Applier { get; set; }

    public static AppTheme Find(string? id) => Themes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Themes.First(t => t.Id == DefaultId);

    public static void Apply(string? id)
    {
        var theme = Find(id);
        Applier?.Invoke(theme);
        Current = theme;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Remembers the theme in the user settings file next to the sign-in settings.</summary>
    public static void Save(string id) => UserSettings.Set("Appearance", "Theme", id);
}