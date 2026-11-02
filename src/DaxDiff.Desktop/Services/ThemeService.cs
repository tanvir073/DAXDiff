using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;

namespace DaxDiff.Desktop.Services;

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

public sealed record AppTheme(string Id, string Name, string Description, ThemeTokens Tokens)
{
    public Brush HeaderPreview => ThemeService.Brush(Tokens.Header);
    public Brush PagePreview => ThemeService.Brush(Tokens.Page);
    public Brush SurfacePreview => ThemeService.Brush(Tokens.Surface);
    public Brush AccentPreview => ThemeService.Brush(Tokens.Accent);
    public Brush HeroPreview => ThemeService.Brush(Tokens.Hero);
    public Brush BorderPreview => ThemeService.Brush(Tokens.Strong);
}

/// <summary>
/// Applies a colour theme at run time. Every view refers to theme brushes with DynamicResource, so replacing the
/// theme dictionary (the first merged dictionary of App.xaml) recolours the open windows immediately.
/// </summary>
public static class ThemeService
{
    public const string DefaultId = "midnight";

    public static IReadOnlyList<AppTheme> Themes { get; } =
    [
        new("midnight", "Midnight", "Dark teal, the default", new(
            "#0a161b", "#10232a", "#0d1d23", "#e6f1f2", "#a3b9bf", "#ffffff",
            "rgba(255,255,255,0.10)", "rgba(255,255,255,0.24)", "#62808a",
            "#061014", "#e6f1f2", "rgba(230,241,242,0.72)", "rgba(255,255,255,0.14)", "rgba(255,255,255,0.06)",
            "#2fc7d8", "#0d1927", "#5fd6e3", "#7fdde6",
            "#17353f", "#9fe7ea", "#17353f", "#9fe7ea",
            "#e6f1f2", "#0a161b",
            "#133a45", "#ffffff", "rgba(255,255,255,0.76)",
            "#5ad69a", "#ff8f80")),
        new("daylight", "Daylight", "Light header", new(
            "#f6f8f9", "#ffffff", "#ffffff", "#1b2328", "#56636a", "#1b2328",
            "rgba(27,35,40,0.10)", "rgba(27,35,40,0.22)", "#87959c",
            "#ffffff", "#1b2328", "#56636a", "rgba(27,35,40,0.14)", "#f1f4f5",
            "#03607a", "#ffffff", "#024b60", "#03607a",
            "#e3f1f4", "#03607a", "#e3f1f4", "#03607a",
            "#1b2328", "#ffffff",
            "#03607a", "#ffffff", "rgba(255,255,255,0.84)",
            "#147a4b", "#b42318")),
        new("amber", "Amber", "Graphite and gold", new(
            "#f7f5f0", "#ffffff", "#ffffff", "#1f1d1a", "#5f5a52", "#1f1d1a",
            "rgba(31,29,26,0.10)", "rgba(31,29,26,0.22)", "#8d867a",
            "#23211e", "#ffffff", "rgba(255,255,255,0.72)", "rgba(255,255,255,0.18)", "rgba(255,255,255,0.07)",
            "#f2b705", "#1f1d1a", "#d9a304", "#7a4f00",
            "#fbf0cf", "#5c3d00", "#fbf0cf", "#5c3d00",
            "#23211e", "#ffffff",
            "#23211e", "#ffffff", "rgba(255,255,255,0.76)",
            "#2e7d32", "#b42318")),
        new("contrast", "High contrast", "Black and white with strong blue", new(
            "#ffffff", "#ffffff", "#ffffff", "#000000", "#2b2b2b", "#000000",
            "#000000", "#000000", "#000000",
            "#000000", "#ffffff", "#ffffff", "#ffffff", "#000000",
            "#0038b8", "#ffffff", "#002a8a", "#0038b8",
            "#000000", "#ffffff", "#000000", "#ffffff",
            "#000000", "#ffffff",
            "#000000", "#ffffff", "#ffffff",
            "#006b2e", "#b00020")),
        new("steel", "Steel", "Slate blue #778DA9", new(
            "#f3f5f8", "#ffffff", "#ffffff", "#0d1b2a", "#4c5d72", "#1b263b",
            "rgba(27,38,59,0.10)", "rgba(27,38,59,0.22)", "#778da9",
            "#778da9", "#0d1b2a", "rgba(13,27,42,0.8)", "rgba(13,27,42,0.2)", "rgba(255,255,255,0.28)",
            "#778da9", "#0d1b2a", "#647a98", "#3d5470",
            "#e4e9f0", "#1b263b", "#e4e9f0", "#1b263b",
            "#1b263b", "#ffffff",
            "#1b263b", "#ffffff", "rgba(255,255,255,0.78)",
            "#1f7a4d", "#b42318")),
        new("deepsea", "Deep sea", "Navy blue #415A77", new(
            "#f2f4f7", "#ffffff", "#ffffff", "#0d1b2a", "#556377", "#2c3e55",
            "rgba(44,62,85,0.10)", "rgba(44,62,85,0.22)", "#8592a3",
            "#415a77", "#ffffff", "rgba(255,255,255,0.76)", "rgba(255,255,255,0.22)", "rgba(255,255,255,0.1)",
            "#a9bcd3", "#0d1b2a", "#95abc6", "#415a77",
            "#e3e8ef", "#2c3e55", "#e3e8ef", "#2c3e55",
            "#415a77", "#ffffff",
            "#415a77", "#ffffff", "rgba(255,255,255,0.8)",
            "#1f7a4d", "#b42318")),
        new("dusk", "Dusk", "Violet grey #4A4E69", new(
            "#f5f4f8", "#ffffff", "#ffffff", "#1c1c2e", "#5c5f75", "#22223b",
            "rgba(34,34,59,0.10)", "rgba(34,34,59,0.22)", "#8a8ca0",
            "#4a4e69", "#ffffff", "rgba(255,255,255,0.76)", "rgba(255,255,255,0.22)", "rgba(255,255,255,0.1)",
            "#c9ada7", "#22223b", "#bb9b94", "#4a4e69",
            "#ebe9f1", "#22223b", "#ebe9f1", "#22223b",
            "#4a4e69", "#ffffff",
            "#4a4e69", "#ffffff", "rgba(255,255,255,0.8)",
            "#1f7a4d", "#b42318")),
        new("ink", "Dark", "Near black #040303", new(
            "#040303", "#121010", "#0b0909", "#f2efed", "#a8a29e", "#ffffff",
            "rgba(255,255,255,0.10)", "rgba(255,255,255,0.24)", "#6e6763",
            "#040303", "#f2efed", "rgba(242,239,237,0.7)", "rgba(255,255,255,0.14)", "rgba(255,255,255,0.06)",
            "#f2efed", "#040303", "#ffffff", "#e0dbd7",
            "#221e1d", "#ffffff", "#1d1a19", "#f2efed",
            "#6e6763", "#ffffff",
            "#1d1a19", "#ffffff", "rgba(255,255,255,0.74)",
            "#5ad69a", "#ff8f80")),
        new("blush", "Blush", "Soft pink #FFA5AB", new(
            "#fff8f8", "#ffffff", "#ffffff", "#2a1419", "#6e4f56", "#3b1a24",
            "rgba(59,26,36,0.10)", "rgba(59,26,36,0.22)", "#9c7c83",
            "#ffe1e3", "#3b1a24", "#6e4f56", "rgba(59,26,36,0.16)", "#ffffff",
            "#ffa5ab", "#3b1a24", "#ff8f97", "#a3303d",
            "#ffe8ea", "#7a1f2b", "#ffe8ea", "#7a1f2b",
            "#3b1a24", "#ffffff",
            "#3b1a24", "#ffffff", "rgba(255,255,255,0.8)",
            "#1f7a4d", "#b42318")),
        new("plum", "Plum", "Deep purple #582C4D", new(
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

    public static AppTheme Find(string? id) => Themes.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Themes.First(t => t.Id == DefaultId);

    /// <summary>Replaces the theme dictionary in the application resources.</summary>
    public static void Apply(string? id)
    {
        var theme = Find(id);
        var merged = System.Windows.Application.Current.Resources.MergedDictionaries;
        var dictionary = Build(theme.Tokens);
        if (merged.Count > 0)
            merged[0] = dictionary;
        else
            merged.Add(dictionary);
        Current = theme;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Remembers the theme in the user settings file next to the sign-in settings.</summary>
    public static void Save(string id)
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

        root["Appearance"] = new JsonObject { ["Theme"] = id };
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(AppPaths.UserSettings, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static ResourceDictionary Build(ThemeTokens t)
    {
        var surface = Parse(t.Surface);
        var dark = Luminance(surface) < 0.4;
        var warning = Parse(dark ? "#f0b455" : "#8a4b00");
        var success = Parse(t.Success);
        var danger = Parse(t.Danger);
        var header = Over(Parse(t.HeaderField), Parse(t.Header));

        var d = new ResourceDictionary();
        void Set(string key, Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            d[key] = brush;
        }

        Set("PageBrush", Parse(t.Page));
        Set("CloudBrush", Parse(t.Page));
        Set("SubtleBrush", Parse(t.Page));
        Set("SurfaceBrush", surface);
        Set("NavBrush", Parse(t.Nav));
        Set("InkBrush", Parse(t.Text));
        Set("MutedText", Parse(t.Muted));
        Set("TealBrush", Parse(t.Heading));
        Set("AccentBrush", Parse(t.Heading));
        Set("SubtleBorderBrush", Parse(t.Border));
        Set("StrongBorderBrush", Parse(t.Strong));
        Set("BorderBrush", Parse(t.Strong));
        Set("FieldBorderBrush", Parse(t.Field));
        Set("HeaderBrush", Parse(t.Header));
        Set("HeaderTextBrush", Parse(t.HeaderText));
        Set("HeaderMutedBrush", Parse(t.HeaderMuted));
        Set("HeaderLineBrush", Parse(t.HeaderLine));
        Set("HeaderFieldBrush", Parse(t.HeaderField));
        Set("HeaderFieldHoverBrush", Mix(Parse(t.HeaderText), header, 0.16));
        // The "outer join" logo keeps its own colours: the light variant on a light header, the dark one otherwise.
        var lightHeader = Luminance(Parse(t.Header)) >= 0.4;
        Set("LogoABrush", Parse(lightHeader ? "#0284c7" : "#7dd3fc"));
        Set("LogoBBrush", Parse(lightHeader ? "#7dd3fc" : "#e0f2fe"));
        Set("LogoLensBrush", Parse("#024b60"));
        Set("AccentFillBrush", Parse(t.Accent));
        Set("OnAccentBrush", Parse(t.OnAccent));
        Set("AccentDeepBrush", Parse(t.AccentHover));
        Set("AccentTextBrush", Parse(t.Link));
        Set("MintBrush", Parse(t.SelBg));
        Set("SelectionTextBrush", Parse(t.SelText));
        Set("AccentSoftBrush", Parse(t.TileBg));
        Set("TileTextBrush", Parse(t.TileText));
        Set("ChipBrush", Parse(t.ChipA));
        Set("ChipTextBrush", Parse(t.ChipAText));
        Set("HeroBrush", Parse(t.Hero));
        Set("HeroTextBrush", Parse(t.HeroText));
        Set("HeroMutedBrush", Parse(t.HeroMuted));
        Set("SuccessBrush", success);
        Set("SuccessSoftBrush", Mix(success, surface, dark ? 0.2 : 0.12));
        Set("DangerBrush", danger);
        Set("DangerSoftBrush", Mix(danger, surface, dark ? 0.2 : 0.1));
        Set("WarningBrush", warning);
        Set("WarningSoftBrush", Mix(warning, surface, dark ? 0.2 : 0.14));

        // Selection colours used by the built-in list, tree and grid templates.
        d[SystemColors.HighlightBrushKey] = d["MintBrush"];
        d[SystemColors.HighlightTextBrushKey] = d["SelectionTextBrush"];
        d[SystemColors.InactiveSelectionHighlightBrushKey] = d["MintBrush"];
        d[SystemColors.InactiveSelectionHighlightTextBrushKey] = d["SelectionTextBrush"];
        d[SystemColors.ControlTextBrushKey] = d["InkBrush"];
        return d;
    }

    public static Brush Brush(string css)
    {
        var brush = new SolidColorBrush(Parse(css));
        brush.Freeze();
        return brush;
    }

    /// <summary>Parses #rrggbb and rgba(r,g,b,a).</summary>
    public static Color Parse(string css)
    {
        css = css.Trim();
        if (css.StartsWith('#'))
            return (Color)ColorConverter.ConvertFromString(css);
        var parts = css[(css.IndexOf('(') + 1)..css.IndexOf(')')].Split(',').Select(p => p.Trim()).ToArray();
        var alpha = parts.Length > 3 ? double.Parse(parts[3], CultureInfo.InvariantCulture) : 1;
        return Color.FromArgb((byte)Math.Round(alpha * 255), byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]));
    }

    /// <summary>An opaque colour: <paramref name="color"/> laid over <paramref name="background"/> at the given weight.</summary>
    private static Color Mix(Color color, Color background, double weight) => Color.FromRgb(
        (byte)Math.Round(color.R * weight + background.R * (1 - weight)),
        (byte)Math.Round(color.G * weight + background.G * (1 - weight)),
        (byte)Math.Round(color.B * weight + background.B * (1 - weight)));

    /// <summary>A possibly translucent colour composited over an opaque one.</summary>
    private static Color Over(Color color, Color background) => Mix(Color.FromRgb(color.R, color.G, color.B), background, color.A / 255.0);

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
}
