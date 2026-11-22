using Avalonia.Media;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;

namespace WhaleGenie.Web;

/// <summary>The theme palette and resource application the bridge's port needs.</summary>
public sealed class ThemeCommands(AppSettings settings) : IThemeCommands
{
    public ThemeState Current()
    {
        var selection = ThemeController.Load(settings);
        var requested = ThemePalette.TryParse(selection.AccentHex, out var accent)
            ? accent
            : Color.Parse(ThemePalette.DefaultAccent);
        var usable = selection.Mode is not ThemeMode.Custom
                     || ThemePalette.TryDerive(requested, selection.BaseLight, out _);
        if (!ThemePalette.TryDerive(requested, selection.BaseLight, out var tokens)
            && !ThemePalette.TryDerive(Color.Parse(ThemePalette.DefaultAccent), selection.BaseLight,
                out tokens))
        {
            tokens = default;
        }

        return new ThemeState(
            selection.Mode.ToString().ToLowerInvariant(),
            selection.BaseLight,
            selection.AccentHex,
            usable,
            ThemePalette.ToHex(tokens.Fill),
            ThemePalette.ToHex(tokens.Surface),
            ThemePalette.ToHex(tokens.Text),
            ThemePalette.DefaultAccent);
    }

    public ThemeState? Save(string mode, bool? baseLight, string? accentHex)
    {
        var selected = mode.ToLowerInvariant() switch
        {
            "dark" => ThemeMode.Dark,
            "light" => ThemeMode.Light,
            "custom" => ThemeMode.Custom,
            _ => (ThemeMode?)null,
        };
        if (selected is not { } themeMode)
        {
            return null;
        }

        // WithMode pairs the built-in modes with their variant, the same pairing the settings
        // window makes: a choice that says light on a dark base would paint light colours on dark
        // controls.
        var selection = new ThemeSelection(themeMode,
            baseLight ?? themeMode == ThemeMode.Light, accentHex).WithMode(themeMode);
        ThemeController.Apply(selection);
        ThemeController.Save(settings, selection);
        return Current();
    }
}
