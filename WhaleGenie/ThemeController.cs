using WhaleGenie.Localization;

namespace WhaleGenie;

/// <summary>
/// Loads, applies and saves the one theme choice. Startup, the settings window and the palette
/// editor all come through here, so a custom palette cannot be applied without being stored or
/// stored without being applied.
/// </summary>
public static class ThemeController
{
    public static ThemeSelection Load() => LocalSettings.LoadTheme();

    public static void Apply(ThemeSelection selection) => ThemePalette.Apply(selection);

    public static void Save(ThemeSelection selection) => LocalSettings.StoreTheme(selection);

    /// <summary>
    /// A custom palette to start from: the built-in palette of the base variant, unless a custom
    /// one is already stored.
    /// </summary>
    public static ThemeSelection StartCustom(ThemeSelection current)
    {
        var light = current.BaseLight;
        var colors = current.Mode is ThemeMode.Custom && current.CustomColors.Count > 0
            ? current.CustomColors
            : ThemePalette.Defaults(light);

        return new ThemeSelection(ThemeMode.Custom, light, colors);
    }
}
