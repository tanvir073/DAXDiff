using System.Globalization;
using Avalonia.Media;
using Avalonia.Styling;
using DaxDiff.Desktop.Services;

namespace DaxDiff.App.Services;

/// <summary>
/// Turns a <see cref="ThemeCatalog"/> theme into the brushes the views use with DynamicResource, so applying a theme
/// recolours the open windows immediately. The brush names match the WPF app's.
/// </summary>
public static class ThemeBrushes
{
    public static void Apply(AppTheme theme)
    {
        var app = Avalonia.Application.Current ?? throw new InvalidOperationException("The application has not started.");
        var t = theme.Tokens;
        var surface = Parse(t.Surface);
        var dark = Luminance(surface) < 0.4;
        var warning = Parse(dark ? "#f0b455" : "#8a4b00");
        var success = Parse(t.Success);
        var danger = Parse(t.Danger);
        var header = Over(Parse(t.HeaderField), Parse(t.Header));
        var lightHeader = Luminance(Parse(t.Header)) >= 0.4;

        var r = app.Resources;
        void Set(string key, Color color) => r[key] = new SolidColorBrush(color);

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

        // Fluent's own controls (menus, pop-ups, scroll bars) follow the theme's lightness and accent.
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        r["SystemAccentColor"] = Parse(t.Accent);
        // The DataGrid theme paints its header row from its own brush; use the page colour as in WPF.
        Set("DataGridColumnHeaderBackgroundBrush", Parse(t.Page));
        Set("DataGridColumnHeaderForegroundBrush", Parse(t.Muted));
    }

    public static IBrush Brush(string css) => new SolidColorBrush(Parse(css));

    /// <summary>Parses #rrggbb and rgba(r,g,b,a).</summary>
    public static Color Parse(string css)
    {
        css = css.Trim();
        if (css.StartsWith('#'))
            return Color.Parse(css);
        var parts = css[(css.IndexOf('(') + 1)..css.IndexOf(')')].Split(',').Select(p => p.Trim()).ToArray();
        var alpha = parts.Length > 3 ? double.Parse(parts[3], CultureInfo.InvariantCulture) : 1;
        return Color.FromArgb((byte)Math.Round(alpha * 255), byte.Parse(parts[0], CultureInfo.InvariantCulture),
            byte.Parse(parts[1], CultureInfo.InvariantCulture), byte.Parse(parts[2], CultureInfo.InvariantCulture));
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
