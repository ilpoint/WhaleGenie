using WhaleGenie.Localization;

namespace WhaleGenie;

/// <summary>
/// Loads, applies and saves the one theme choice. Startup, the settings window and the accent
/// picker all come through here, so a custom accent cannot be applied without being stored or
/// stored without being applied.
/// </summary>
public static class ThemeController
{
    public static ThemeSelection Load() => LocalSettings.LoadTheme();

    public static void Apply(ThemeSelection selection) => ThemePalette.Apply(selection);

    public static void Save(ThemeSelection selection) => LocalSettings.StoreTheme(selection);

    /// <summary>
    /// The choice to start customising from: the variant in use, with the accent it was already
    /// painting with. A built-in variant has no accent of its own to carry over, so the picker
    /// starts on the one both variants are drawn with.
    /// </summary>
    public static ThemeSelection StartCustom(ThemeSelection current)
        => new(ThemeMode.Custom, current.BaseLight, current.AccentHex ?? ThemePalette.DefaultAccent);
}
