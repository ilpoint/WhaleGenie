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
        ("AppSurface", "#292929", "#FFFFFF"),
        ("AppBackdrop", "#1E1E1E", "#F3F3F3"),
        ("AppChrome", "#202020", "#ECECEC"),
        ("AppField", "#2E2E2E", "#F7F7F7"),
        ("AppBorder", "#3A3A3A", "#D5D5D5"),
        ("AppText", "#E8E8E8", "#1F1F1F"),
        ("AppMuted", "#9A9A9A", "#666666"),
        ("AppAccent", "#C8401C", "#C8401C"),
        ("AppControl", "#404040", "#E4E4E4"),
        ("AppGroup", "#1F1F1F", "#F7F7F7"),
        ("AppHover", "#3C3C3C", "#DCDCDC"),
        ("AppDock", "#2A2A2A", "#EDEDED"),
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
