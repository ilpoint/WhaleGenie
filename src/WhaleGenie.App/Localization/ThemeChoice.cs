namespace WhaleGenie;

/// <summary>The three choices the theme picker offers.</summary>
public enum ThemeMode
{
    Dark,
    Light,
    Custom,
}

/// <summary>
/// The whole theme choice: a built-in variant, or one accent colour over one of the two control
/// variants. Everything a custom theme paints is derived from that single colour, so the two
/// variants cannot be left half-customised the way twenty-two per-colour rows could.
/// </summary>
/// <remarks>
/// The choice is a value in the settings file, so it is written down without a colour library
/// behind it: the window turns it into brushes, and this file only says what was picked.
/// </remarks>
public sealed record ThemeSelection(ThemeMode Mode, bool BaseLight, string? AccentHex)
{
    /// <summary>
    /// The same choice under another mode. Picking a built-in mode also picks its variant: a choice
    /// that says "light" while keeping the dark base would paint light colours on dark controls.
    /// </summary>
    public ThemeSelection WithMode(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => this with { Mode = mode, BaseLight = true },
        ThemeMode.Dark => this with { Mode = mode, BaseLight = false },
        _ => this with { Mode = mode },
    };
}
