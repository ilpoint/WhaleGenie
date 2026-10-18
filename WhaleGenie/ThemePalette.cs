using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace WhaleGenie;

/// <summary>
/// The handful of colours the windows share. They are changed together rather than one control at
/// a time, so a switch from dark to light cannot leave a panel or a label behind.
/// </summary>
public static class ThemePalette
{
    private static readonly (string Key, string Dark, string Light)[] Brushes =
    [
        ("AppSurface", "#292929", "#FAF8F4"),
        ("AppBackdrop", "#1E1E1E", "#F4F1EB"),
        ("AppChrome", "#202020", "#E9E4DA"),
        ("AppField", "#2E2E2E", "#F2EEE7"),
        ("AppBorder", "#3A3A3A", "#D5CEC1"),
        ("AppText", "#E8E8E8", "#2B2723"),
        ("AppMuted", "#9A9A9A", "#6B6258"),
        ("AppAccent", "#C8401C", "#C8401C"),
        ("AppControl", "#404040", "#E2DCD1"),
        ("AppGroup", "#2B2723", "#F2EEE7"),
        ("AppHover", "#3C3C3C", "#D8D1C5"),
        ("AppDock", "#2A2A2A", "#EBE5DB"),
        ("AppSelected", "#4A2A20", "#FBEDE4"),
        ("AppLabel", "#C8A165", "#8A6428"),
        ("AppInfo", "#8FB3D9", "#2F6F9F"),
        ("AppInfoSoft", "#2B3A4A", "#E3EEF8"),
        ("AppWarning", "#E0A060", "#9A6418"),
        ("AppDanger", "#E06C75", "#B23A3A"),
        ("AppSuccess", "#7FC97F", "#2E7D4F"),
        ("AppAccentSoft", "#4A342E", "#F6D9CB"),
        ("AppAccentHover", "#D9502A", "#D9502A"),
    ];

    public static void Apply(bool light)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        foreach (var (key, dark, bright) in Brushes)
        {
            app.Resources[key] = new SolidColorBrush(Color.Parse(light ? bright : dark));
        }

        app.RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
