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
            "#0a161b", "#10232a", "#0d1d23", "#e6f1f2", "#a3b9bf", "#ffffff",
            "rgba(255,255,255,0.10)", "rgba(255,255,255,0.24)", "#62808a",
            "#061014", "#e6f1f2", "rgba(230,241,242,0.72)", "rgba(255,255,255,0.14)", "rgba(255,255,255,0.06)",
            "#2fc7d8", "#0d1927", "#5fd6e3", "#7fdde6",
            "#17353f", "#9fe7ea", "#17353f", "#9fe7ea",
            "#e6f1f2", "#0a161b",
            "#133a45", "#ffffff", "rgba(255,255,255,0.76)",
            "#5ad69a", "#ff8f80")),
        new("daylight", Strings.Theme_daylight, Strings.Theme_daylight_Description, new(
            "#f6f8f9", "#ffffff", "#ffffff", "#1b2328", "#56636a", "#1b2328",
            "rgba(27,35,40,0.10)", "rgba(27,35,40,0.22)", "#87959c",
            "#ffffff", "#1b2328", "#56636a", "rgba(27,35,40,0.14)", "#f1f4f5",
            "#03607a", "#ffffff", "#024b60", "#03607a",
            "#e3f1f4", "#03607a", "#e3f1f4", "#03607a",
            "#1b2328", "#ffffff",
            "#03607a", "#ffffff", "rgba(255,255,255,0.84)",
            "#147a4b", "#b42318")),
        new("amber", Strings.Theme_amber, Strings.Theme_amber_Description, new(
            "#f7f5f0", "#ffffff", "#ffffff", "#1f1d1a", "#5f5a52", "#1f1d1a",
            "rgba(31,29,26,0.10)", "rgba(31,29,26,0.22)", "#8d867a",
            "#23211e", "#ffffff", "rgba(255,255,255,0.72)", "rgba(255,255,255,0.18)", "rgba(255,255,255,0.07)",
            "#f2b705", "#1f1d1a", "#d9a304", "#7a4f00",
            "#fbf0cf", "#5c3d00", "#fbf0cf", "#5c3d00",
            "#23211e", "#ffffff",
            "#23211e", "#ffffff", "rgba(255,255,255,0.76)",
            "#2e7d32", "#b42318")),
        new("contrast", Strings.Theme_contrast, Strings.Theme_contrast_Description, new(
            "#ffffff", "#ffffff", "#ffffff", "#000000", "#2b2b2b", "#000000",
            "#000000", "#000000", "#000000",
            "#000000", "#ffffff", "#ffffff", "#ffffff", "#000000",
            "#0038b8", "#ffffff", "#002a8a", "#0038b8",
            "#000000", "#ffffff", "#000000", "#ffffff",
            "#000000", "#ffffff",
            "#000000", "#ffffff", "#ffffff",
            "#006b2e", "#b00020")),
        new("steel", Strings.Theme_steel, Strings.Theme_steel_Description, new(
            "#f3f5f8", "#ffffff", "#ffffff", "#0d1b2a", "#4c5d72", "#1b263b",
            "rgba(27,38,59,0.10)", "rgba(27,38,59,0.22)", "#778da9",
            "#778da9", "#0d1b2a", "rgba(13,27,42,0.8)", "rgba(13,27,42,0.2)", "rgba(255,255,255,0.28)",
            "#778da9", "#0d1b2a", "#647a98", "#3d5470",
            "#e4e9f0", "#1b263b", "#e4e9f0", "#1b263b",
            "#1b263b", "#ffffff",
            "#1b263b", "#ffffff", "rgba(255,255,255,0.78)",
            "#1f7a4d", "#b42318")),
        new("deepsea", Strings.Theme_deepsea, Strings.Theme_deepsea_Description, new(
            "#f2f4f7", "#ffffff", "#ffffff", "#0d1b2a", "#556377", "#2c3e55",
            "rgba(44,62,85,0.10)", "rgba(44,62,85,0.22)", "#8592a3",
            "#415a77", "#ffffff", "rgba(255,255,255,0.76)", "rgba(255,255,255,0.22)", "rgba(255,255,255,0.1)",
            "#a9bcd3", "#0d1b2a", "#95abc6", "#415a77",
            "#e3e8ef", "#2c3e55", "#e3e8ef", "#2c3e55",
            "#415a77", "#ffffff",
            "#415a77", "#ffffff", "rgba(255,255,255,0.8)",
            "#1f7a4d", "#b42318")),
        new("dusk", Strings.Theme_dusk, Strings.Theme_dusk_Description, new(
            "#f5f4f8", "#ffffff", "#ffffff", "#1c1c2e", "#5c5f75", "#22223b",
            "rgba(34,34,59,0.10)", "rgba(34,34,59,0.22)", "#8a8ca0",
            "#4a4e69", "#ffffff", "rgba(255,255,255,0.76)", "rgba(255,255,255,0.22)", "rgba(255,255,255,0.1)",
            "#c9ada7", "#22223b", "#bb9b94", "#4a4e69",
            "#ebe9f1", "#22223b", "#ebe9f1", "#22223b",
            "#4a4e69", "#ffffff",
            "#4a4e69", "#ffffff", "rgba(255,255,255,0.8)",
            "#1f7a4d", "#b42318")),
        new("ink", Strings.Theme_ink, Strings.Theme_ink_Description, new(
            "#040303", "#121010", "#0b0909", "#f2efed", "#a8a29e", "#ffffff",
            "rgba(255,255,255,0.10)", "rgba(255,255,255,0.24)", "#6e6763",
            "#040303", "#f2efed", "rgba(242,239,237,0.7)", "rgba(255,255,255,0.14)", "rgba(255,255,255,0.06)",
            "#f2efed", "#040303", "#ffffff", "#e0dbd7",
            "#221e1d", "#ffffff", "#1d1a19", "#f2efed",
            "#6e6763", "#ffffff",
            "#1d1a19", "#ffffff", "rgba(255,255,255,0.74)",
            "#5ad69a", "#ff8f80")),
        new("blush", Strings.Theme_blush, Strings.Theme_blush_Description, new(
            "#fff8f8", "#ffffff", "#ffffff", "#2a1419", "#6e4f56", "#3b1a24",
            "rgba(59,26,36,0.10)", "rgba(59,26,36,0.22)", "#9c7c83",
            "#ffe1e3", "#3b1a24", "#6e4f56", "rgba(59,26,36,0.16)", "#ffffff",
            "#ffa5ab", "#3b1a24", "#ff8f97", "#a3303d",
            "#ffe8ea", "#7a1f2b", "#ffe8ea", "#7a1f2b",
            "#3b1a24", "#ffffff",
            "#3b1a24", "#ffffff", "rgba(255,255,255,0.8)",
            "#1f7a4d", "#b42318")),
        new("plum", Strings.Theme_plum, Strings.Theme_plum_Description, new(
            "#faf6f9", "#ffffff", "#ffffff", "#24121f", "#6a5263", "#3f1f37",
            "rgba(88,44,77,0.12)", "rgba(88,44,77,0.24)", "#9a8293",
            "#582c4d", "#ffffff", "rgba(255,255,255,0.76)", "rgba(255,255,255,0.2)", "rgba(255,255,255,0.08)",
            "#e7b8d6", "#2e1628", "#dca3c8", "#7a3a68",
            "#f5e8f1", "#582c4d", "#f5e8f1", "#582c4d",
            "#582c4d", "#ffffff",
            "#582c4d", "#ffffff", "rgba(255,255,255,0.8)",
            "#1f7a4d", "#b42318")),
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