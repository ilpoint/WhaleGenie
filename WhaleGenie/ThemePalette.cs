using System;
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
/// The whole theme choice: a built-in variant, or one accent colour over one of the two control
/// variants. Everything a custom theme paints is derived from that single colour, so the two
/// variants cannot be left half-customised the way twenty-two per-colour rows could.
/// </summary>
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

/// <summary>The five accent colours a picked colour turns into.</summary>
public readonly record struct AccentTokens(Color Fill, Color Hover, Color Pressed, Color Text, Color Surface);

/// <summary>
/// The colours a custom theme overrides. Everything else comes from Themes/Tokens.axaml, which is
/// also what these five fall back to when no colour was picked.
/// </summary>
public static class ThemePalette
{
    /// <summary>The accent the two built-in variants paint with, and the one the picker starts on.</summary>
    public const string DefaultAccent = "#C8401C";

    /// <summary>Text has to stay readable: this is the floor for both pairs the derivation checks.</summary>
    private const double MinContrast = 4.5;

    /// <summary>
    /// How far the fill may be moved before the derivation gives up: a colour that needs more than
    /// this to carry white text is not that colour any more, so it is refused instead of quietly
    /// painted in some other colour.
    /// </summary>
    private const double MaxFillShift = 0.30;

    /// <summary>
    /// The accent text is a legibility variant of the picked colour standing on the window, not the
    /// colour itself, so it may travel the whole range. Holding it to the fill's limit would refuse
    /// every dark colour on the dark window: black reaches only #4D4D4D in thirty percent, which is
    /// 1.9:1 against the window, and a person who picked black would be told their colour is
    /// unusable rather than shown it as light grey text.
    /// </summary>
    private const double MaxTextShift = 1.0;

    /// <summary>
    /// The five tokens a custom accent writes, by the name the styles paint with. Each is written in
    /// both shapes the token dictionary defines: the brush for whatever paints with it, and the
    /// colour under it for whatever derives from it.
    /// </summary>
    private static readonly string[] AccentTokenNames =
    [
        "AccentFill", "AccentFillHover", "AccentFillPressed", "AccentText", "AccentSurface",
    ];

    /// <summary>Applies one theme choice to every window that is already open.</summary>
    public static void Apply(ThemeSelection selection)
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        // The variant is switched before the override keys are touched, and the order matters:
        // those keys belong to a variant. Writing them first and switching after would leave the
        // colour that was derived for the old variant standing over the new one, so a window would
        // keep the accent of the variant it just left.
        app.RequestedThemeVariant = selection.BaseLight ? ThemeVariant.Light : ThemeVariant.Dark;

        if (selection.Mode is ThemeMode.Custom
            && TryParse(selection.AccentHex, out var accent)
            && TryDerive(accent, selection.BaseLight, out var tokens))
        {
            Write(app, tokens);
            return;
        }

        // A built-in variant, or a colour the derivation refused: the overrides go away and the
        // variant's own accent from the token dictionary is what everything paints with.
        Remove(app);
    }

    /// <summary>
    /// Turns one colour into the set the interface needs. False means this colour cannot be made
    /// readable — white text on the button and the accent text on the window both have to stay at
    /// <see cref="MinContrast"/> — and the caller must not paint with it.
    /// </summary>
    public static bool TryDerive(Color accent, bool light, out AccentTokens tokens)
    {
        tokens = default;

        // A button carries white text, so the fill has to be dark enough for it. Lightness only
        // moves down: the hue the person picked is the thing worth keeping.
        if (!TryReach(accent.ToHsl(), Colors.White, lighter: false, MaxFillShift, out var fill))
        {
            return false;
        }

        // Hover moves towards the light on a dark window and away from it on a light one, so it
        // reads as "closer" either way. Pressed is a step further down in both.
        var hover = WithLightness(fill.ToHsl(), fill.ToHsl().L + (light ? -0.04 : 0.04));
        var pressed = WithLightness(fill.ToHsl(), fill.ToHsl().L - 0.10);

        // Accent text stands on the window itself rather than inside a button, so it is measured
        // against the window and moves away from it: up on a dark window, down on a light one.
        if (!TryReach(fill.ToHsl(), light ? WindowLight : WindowDark, lighter: !light, MaxTextShift, out var text))
        {
            return false;
        }

        tokens = new AccentTokens(fill, hover, pressed, text, Blend(fill, 0.12, light ? WindowLight : WindowDark));
        return true;
    }

    /// <summary>The contrast ratio of two opaque colours, in the 1:1 to 21:1 range.</summary>
    public static double Contrast(Color a, Color b)
    {
        var first = Luminance(a);
        var second = Luminance(b);
        var brighter = Math.Max(first, second);
        var darker = Math.Min(first, second);
        return (brighter + 0.05) / (darker + 0.05);
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
            // A colour typed by hand is user input; a value that cannot be read falls back.
            return false;
        }
    }

    /// <summary>The text a colour is stored as.</summary>
    public static string ToHex(Color colour) => $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}";

    private static Color WindowDark => Color.Parse("#1C1C1C");

    private static Color WindowLight => Color.Parse("#F3F2F0");

    /// <summary>
    /// Walks one colour towards white or black until it is readable against something else, and
    /// gives up once it has moved further than the colour is worth. A step is one percent of
    /// lightness, which is fine enough that the first readable value is used rather than a darker
    /// one than needed.
    /// </summary>
    private static bool TryReach(HslColor start, Color against, bool lighter, double limit, out Color reached)
    {
        reached = default;
        for (var step = 0; step <= limit * 100; step++)
        {
            var candidate = WithLightness(start, start.L + (lighter ? step : -step) / 100.0);
            if (Contrast(candidate, against) >= MinContrast)
            {
                reached = candidate;
                return true;
            }
        }

        return false;
    }

    private static Color WithLightness(HslColor colour, double lightness)
        => new HslColor(colour.A, colour.H, colour.S, Math.Clamp(lightness, 0, 1)).ToRgb();

    /// <summary>One colour laid over another at an alpha, flattened into an opaque result.</summary>
    private static Color Blend(Color over, double alpha, Color under) => Color.FromRgb(
        Mix(over.R, under.R, alpha),
        Mix(over.G, under.G, alpha),
        Mix(over.B, under.B, alpha));

    private static byte Mix(byte over, byte under, double alpha)
        => (byte)Math.Round((over * alpha) + (under * (1 - alpha)), MidpointRounding.AwayFromZero);

    private static double Luminance(Color colour)
        => (0.2126 * Channel(colour.R)) + (0.7152 * Channel(colour.G)) + (0.0722 * Channel(colour.B));

    private static double Channel(byte value)
    {
        var srgb = value / 255.0;
        return srgb <= 0.04045 ? srgb / 12.92 : Math.Pow((srgb + 0.055) / 1.055, 2.4);
    }

    private static void Write(Application app, AccentTokens tokens)
    {
        Set(app, "AccentFill", tokens.Fill);
        Set(app, "AccentFillHover", tokens.Hover);
        Set(app, "AccentFillPressed", tokens.Pressed);
        Set(app, "AccentText", tokens.Text);
        Set(app, "AccentSurface", tokens.Surface);

        // The tab strip's selected bar reads its own WinUI name, which the dictionary ties to
        // `AccentFill` by a static alias — a static alias is baked when the dictionary is first
        // parsed, so it would keep the accent the user chose before this one. Writing it here as
        // well is what makes a custom accent reach the tab under the window it was chosen in.
        app.Resources["PivotHeaderItemSelectedPipeFill"] = new SolidColorBrush(tokens.Fill);
    }

    private static void Set(Application app, string token, Color colour)
    {
        app.Resources[token] = new SolidColorBrush(colour);
        app.Resources[token + "Color"] = colour;
    }

    private static void Remove(Application app)
    {
        foreach (var token in AccentTokenNames)
        {
            app.Resources.Remove(token);
            app.Resources.Remove(token + "Color");
        }

        app.Resources.Remove("PivotHeaderItemSelectedPipeFill");
    }
}
