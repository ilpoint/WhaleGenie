using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace WhaleGenie;

/// <summary>The three choices the theme picker offers.</summary>
public enum ThemeMode
{
    Dark,
    Light,
    Custom,
}

/// <summary>
/// One colour the interface paints with: its key, the two built-in values, and where it belongs in
/// the custom palette.
/// </summary>
public sealed record ThemeBrush(string Key, string Dark, string Light, string GroupKey, string LabelKey);

/// <summary>
/// The whole theme choice: a built-in variant, or a custom palette over one of the two control
/// variants.
/// </summary>
public sealed record ThemeSelection(
    ThemeMode Mode,
    bool CustomBaseLight,
    IReadOnlyDictionary<string, string> CustomColors)
{
    public bool BaseLight => Mode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Custom => CustomBaseLight,
        _ => false,
    };
}

/// <summary>
/// The handful of colours the windows share. They are changed together rather than one control at
/// a time, so a switch from dark to light cannot leave a panel or a label behind.
/// </summary>
public static class ThemePalette
{
    public static IReadOnlyList<ThemeBrush> Brushes { get; } =
    [
        new("AppSurface", "#292929", "#FAF8F4", "Theme.Group.Surface", "Theme.Color.AppSurface"),
        new("AppBackdrop", "#1E1E1E", "#F4F1EB", "Theme.Group.Surface", "Theme.Color.AppBackdrop"),
        new("AppChrome", "#202020", "#E9E4DA", "Theme.Group.Surface", "Theme.Color.AppChrome"),
        new("AppField", "#2E2E2E", "#F2EEE7", "Theme.Group.Surface", "Theme.Color.AppField"),
        new("AppBorder", "#3A3A3A", "#D5CEC1", "Theme.Group.Surface", "Theme.Color.AppBorder"),
        new("AppControl", "#404040", "#E2DCD1", "Theme.Group.Surface", "Theme.Color.AppControl"),
        new("AppGroup", "#2B2723", "#F2EEE7", "Theme.Group.Surface", "Theme.Color.AppGroup"),
        new("AppDock", "#2A2A2A", "#EBE5DB", "Theme.Group.Surface", "Theme.Color.AppDock"),
        new("AppText", "#E8E8E8", "#2B2723", "Theme.Group.Text", "Theme.Color.AppText"),
        new("AppTextStrong", "#F5F5F5", "#1B1713", "Theme.Group.Text", "Theme.Color.AppTextStrong"),
        new("AppMuted", "#9A9A9A", "#6B6258", "Theme.Group.Text", "Theme.Color.AppMuted"),
        new("AppLabel", "#C8A165", "#8A6428", "Theme.Group.Text", "Theme.Color.AppLabel"),
        new("AppAccent", "#C8401C", "#C8401C", "Theme.Group.State", "Theme.Color.AppAccent"),
        new("AppAccentHover", "#D9502A", "#D9502A", "Theme.Group.State", "Theme.Color.AppAccentHover"),
        new("AppAccentSoft", "#4A342E", "#F6D9CB", "Theme.Group.State", "Theme.Color.AppAccentSoft"),
        new("AppSelected", "#4A2A20", "#FBEDE4", "Theme.Group.State", "Theme.Color.AppSelected"),
        new("AppHover", "#3C3C3C", "#D8D1C5", "Theme.Group.State", "Theme.Color.AppHover"),
        new("AppInfo", "#8FB3D9", "#2F6F9F", "Theme.Group.State", "Theme.Color.AppInfo"),
        new("AppInfoSoft", "#2B3A4A", "#E3EEF8", "Theme.Group.State", "Theme.Color.AppInfoSoft"),
        new("AppWarning", "#E0A060", "#9A6418", "Theme.Group.State", "Theme.Color.AppWarning"),
        new("AppDanger", "#E06C75", "#B23A3A", "Theme.Group.State", "Theme.Color.AppDanger"),
        new("AppSuccess", "#7FC97F", "#2E7D4F", "Theme.Group.State", "Theme.Color.AppSuccess"),
    ];

    /// <summary>A palette of the built-in dark or light values, keyed by resource name.</summary>
    public static IReadOnlyDictionary<string, string> Defaults(bool light)
    {
        var colors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var brush in Brushes)
        {
            colors[brush.Key] = light ? brush.Light : brush.Dark;
        }

        return colors;
    }

    /// <summary>Applies one theme choice to every window that is already open.</summary>
    public static void Apply(ThemeSelection selection)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        // The control theme is chosen first: switching it can rebuild template resources, while
        // these app resources are what the custom palette writes over.
        app.RequestedThemeVariant = selection.BaseLight ? ThemeVariant.Light : ThemeVariant.Dark;

        foreach (var brush in Brushes)
        {
            app.Resources[brush.Key] = new SolidColorBrush(Resolve(brush, selection));
        }
    }

    /// <summary>The old entry point: a built-in dark or light palette.</summary>
    public static void Apply(bool light)
        => Apply(new ThemeSelection(
            light ? ThemeMode.Light : ThemeMode.Dark,
            light,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));

    /// <summary>The colour a brush takes under a selection, with a bad custom value falling back.</summary>
    public static Color Resolve(ThemeBrush brush, ThemeSelection selection)
    {
        if (selection.Mode is ThemeMode.Custom
            && selection.CustomColors.TryGetValue(brush.Key, out var text)
            && TryParse(text, out var colour))
        {
            return colour;
        }

        return Color.Parse(selection.BaseLight ? brush.Light : brush.Dark);
    }

    /// <summary>Reads an opaque #RRGGBB (or #AARRGGBB) value; alpha is dropped.</summary>
    public static bool TryParse(string? text, out Color colour)
    {
        colour = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        try
        {
            var parsed = Color.Parse(text.Trim());
            colour = Color.FromRgb(parsed.R, parsed.G, parsed.B);
            return true;
        }
        catch (Exception)
        {
            // A color typed by hand is user input; a value that cannot be read falls back.
            return false;
        }
    }

    /// <summary>The text a custom colour is stored as.</summary>
    public static string ToHex(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";
}
