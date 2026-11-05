using DaxDiff.Presentation.Resources;

namespace DaxDiff.Presentation.Services;

/// <summary>
/// Colour tokens of one theme, as defined on the DAXDiff redesign canvas. Values are CSS colours (#rrggbb or rgba()).
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
    public const string DefaultId = "midnight";

    public static IReadOnlyList<AppTheme> Themes { get; } =
    [
        new("midnight", Strings.Theme_midnight, Strings.Theme_midnight_Description, new(
            "#0a162b", "#10233a", "#0d1d33", "#e6f1e2", "#a3b9cf", "#ffffff",
            "rgba(255,255,255,0.10)", "rgba(255,255,255,0.24)", "#62809a",
            "#061024", "#e6f1e2", "rgba(230,241,226,0.72)", "rgba(255,255,255,0.14)", "rgba(255,255,255,0.06)",
            "#2fc7e8", "#0d1937", "#5fd6f3", "#7fddf6",
            "#17354f", "#9fe7fa", "#17354f", "#9fe7fa",
            "#e6f1e2", "#0a162b",
            "#133a55", "#ffffff", "rgba(255,255,255,0.76)",
            "#5ad6aa", "#ff8f90")),
        new("daylight", Strings.Theme_daylight, Strings.Theme_daylight_Description, new(
            "#f6f8e9", "#ffffff", "#ffffff", "#1b2338", "#56637a", "#1b2338",
            "rgba(27,35,56,0.10)", "rgba(27,35,56,0.22)", "#8795ac",
            "#ffffff", "#1b2338", "#56637a", "rgba(27,35,56,0.14)", "#f1f4e5",
            "#03608a", "#ffffff", "#024b70", "#03608a",
            "#e3f1e4", "#03608a", "#e3f1e4", "#03608a",
            "#1b2338", "#ffffff",
            "#03608a", "#ffffff", "rgba(255,255,255,0.84)",
            "#147a5b", "#b42328")),
        new("amber", Strings.Theme_amber, Strings.Theme_amber_Description, new(
            "#f7f5e0", "#ffffff", "#ffffff", "#1f1d2a", "#5f5a62", "#1f1d2a",
            "rgba(31,29,42,0.10)", "rgba(31,29,42,0.22)", "#8d868a",
            "#23212e", "#ffffff", "rgba(255,255,255,0.72)", "rgba(255,255,255,0.18)", "rgba(255,255,255,0.07)",
            "#f2b715", "#1f1d2a", "#d9a314", "#7a4f10",
            "#fbf0df", "#5c3d10", "#fbf0df", "#5c3d10",
            "#23212e", "#ffffff",
            "#23212e", "#ffffff", "rgba(255,255,255,0.76)",
            "#2e7d42", "#b42328")),
        new("graphite", Strings.Theme_graphite, Strings.Theme_graphite_Description, new(
            "#161616", "#202020", "#1b1b1b", "#ededed", "#a8a8a8", "#ffffff",
            "rgba(255,255,255,0.10)", "rgba(255,255,255,0.24)", "#707070",
            "#0f0f0f", "#ededed", "rgba(237,237,237,0.70)", "rgba(255,255,255,0.14)", "rgba(255,255,255,0.06)",
            "#ff9a3c", "#1a1a1a", "#ffb066", "#ffb066",
            "#3a2c1f", "#ffd2a6", "#2e2620", "#ffd2a6",
            "#ededed", "#161616",
            "#2c2c2c", "#ffffff", "rgba(255,255,255,0.74)",
            "#5bcf8a", "#ff8a80")),
        new("contrast", Strings.Theme_contrast, Strings.Theme_contrast_Description, new(
            "#ffffff", "#ffffff", "#ffffff", "#000000", "#2b2b3b", "#000000",
            "#000000", "#000000", "#000000",
            "#000000", "#ffffff", "#ffffff", "#ffffff", "#000000",
            "#0038c8", "#ffffff", "#002a9a", "#0038c8",
            "#000000", "#ffffff", "#000000", "#ffffff",
            "#000000", "#ffffff",
            "#000000", "#ffffff", "#ffffff",
            "#006b3e", "#b00030")),
        new("steel", Strings.Theme_steel, Strings.Theme_steel_Description, new(
            "#f3f5e8", "#ffffff", "#ffffff", "#0d1b3a", "#4c5d82", "#1b264b",
            "rgba(27,38,75,0.10)", "rgba(27,38,75,0.22)", "#778db9",
            "#778db9", "#0d1b3a", "rgba(13,27,58,0.8)", "rgba(13,27,58,0.2)", "rgba(255,255,255,0.28)",
            "#778db9", "#0d1b3a", "#647aa8", "#3d5480",
            "#e4e9e0", "#1b264b", "#e4e9e0", "#1b264b",
            "#1b264b", "#ffffff",
            "#1b264b", "#ffffff", "rgba(255,255,255,0.78)",
            "#1f7a5d", "#b42328")),
        new("deepsea", Strings.Theme_deepsea, Strings.Theme_deepsea_Description, new(
            "#f2f4e7", "#ffffff", "#ffffff", "#0d1b3a", "#556387", "#2c3e65",
            "rgba(44,62,101,0.10)", "rgba(44,62,101,0.22)", "#8592b3",
            "#415a87", "#ffffff", "rgba(255,255,255,0.76)", "rgba(255,255,255,0.22)", "rgba(255,255,255,0.1)",
            "#a9bce3", "#0d1b3a", "#95abd6", "#415a87",
            "#e3e8ff", "#2c3e65", "#e3e8ff", "#2c3e65",
            "#415a87", "#ffffff",
            "#415a87", "#ffffff", "rgba(255,255,255,0.8)",
            "#1f7a5d", "#b42328")),
        new("dusk", Strings.Theme_dusk, Strings.Theme_dusk_Description, new(
            "#f5f4e8", "#ffffff", "#ffffff", "#1c1c3e", "#5c5f85", "#22224b",
            "rgba(34,34,75,0.10)", "rgba(34,34,75,0.22)", "#8a8cb0",
            "#4a4e79", "#ffffff", "rgba(255,255,255,0.76)", "rgba(255,255,255,0.22)", "rgba(255,255,255,0.1)",
            "#c9adb7", "#22224b", "#bb9ba4", "#4a4e79",
            "#ebe9e1", "#22224b", "#ebe9e1", "#22224b",
            "#4a4e79", "#ffffff",
            "#4a4e79", "#ffffff", "rgba(255,255,255,0.8)",
            "#1f7a5d", "#b42328")),
        new("ink", Strings.Theme_ink, Strings.Theme_ink_Description, new(
            "#040313", "#121020", "#0b0919", "#f2effd", "#a8a2ae", "#ffffff",
            "rgba(255,255,255,0.10)", "rgba(255,255,255,0.24)", "#6e6773",
            "#040313", "#f2effd", "rgba(242,239,253,0.7)", "rgba(255,255,255,0.14)", "rgba(255,255,255,0.06)",
            "#f2effd", "#040313", "#ffffff", "#e0dbe7",
            "#221e2d", "#ffffff", "#1d1a29", "#f2effd",
            "#6e6773", "#ffffff",
            "#1d1a29", "#ffffff", "rgba(255,255,255,0.74)",
            "#5ad6aa", "#ff8f90")),
        new("blush", Strings.Theme_blush, Strings.Theme_blush_Description, new(
            "#fff8e8", "#ffffff", "#ffffff", "#2a1429", "#6e4f66", "#3b1a34",
            "rgba(59,26,52,0.10)", "rgba(59,26,52,0.22)", "#9c7c93",
            "#ffe1f3", "#3b1a34", "#6e4f66", "rgba(59,26,52,0.16)", "#ffffff",
            "#ffa5bb", "#3b1a34", "#ff8fa7", "#a3304d",
            "#ffe8fa", "#7a1f3b", "#ffe8fa", "#7a1f3b",
            "#3b1a34", "#ffffff",
            "#3b1a34", "#ffffff", "rgba(255,255,255,0.8)",
            "#1f7a5d", "#b42328")),
        new("plum", Strings.Theme_plum, Strings.Theme_plum_Description, new(
            "#faf6e9", "#ffffff", "#ffffff", "#24122f", "#6a5273", "#3f1f47",
            "rgba(88,44,93,0.12)", "rgba(88,44,93,0.24)", "#9a82a3",
            "#582c5d", "#ffffff", "rgba(255,255,255,0.76)", "rgba(255,255,255,0.2)", "rgba(255,255,255,0.08)",
            "#e7b8e6", "#2e1638", "#dca3d8", "#7a3a78",
            "#f5e8e1", "#582c5d", "#f5e8e1", "#582c5d",
            "#582c5d", "#ffffff",
            "#582c5d", "#ffffff", "rgba(255,255,255,0.8)",
            "#1f7a5d", "#b42328")),
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