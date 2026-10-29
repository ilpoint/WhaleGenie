using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using WhaleGenie;

namespace WhaleGenie.Tests;

/// <summary>
/// The palette lives in one dictionary now, and the windows ask for it by name. A key that is
/// missing there does not fail the build: DynamicResource simply paints nothing, so a window would
/// come up with no background at all. These checks read the keys out of the application the way a
/// window does.
/// </summary>
[Collection("Theme")]
public class ThemeTokenTests
{
    /// <summary>Every semantic colour the styles are written against, dark and light.</summary>
    private static readonly string[] Tokens =
    [
        "SurfaceWindow", "SurfaceChrome", "SurfaceSunken", "SurfacePanel", "SurfaceField",
        "SurfaceCard", "SurfaceFlyout", "SurfaceHover", "SurfacePressed", "SurfaceControl",
        "SurfaceControlHover", "SurfaceControlPressed",
        "TextPrimary", "TextSecondary", "TextTertiary", "TextDisabled", "TextOnAccent",
        "BorderSubtle", "BorderControl", "BorderControlHover", "BorderStrong",
        "FocusRing", "FocusRingInner",
        "AccentFill", "AccentFillHover", "AccentFillPressed", "AccentText", "AccentSurface",
        "Success", "SuccessSurface", "Warning", "WarningSurface",
        "Danger", "DangerSurface", "Info", "InfoSurface",
        "OverlayHover", "OverlayPressed",
    ];

    /// <summary>The five picker colours, which are deliberately the same in both variants.</summary>
    private static readonly string[] PickerTokens =
    [
        "PickerScrim", "PickerTargetScrim", "PickerReadout", "PickerEdgeDark", "PickerEdgeLight",
    ];

    /// <summary>The old names the twenty-three windows still use.</summary>
    private static readonly string[] LegacyKeys =
    [
        "AppSurface", "AppBackdrop", "AppChrome", "AppField", "AppBorder", "AppText",
        "AppTextStrong", "AppMuted", "AppAccent", "AppControl", "AppGroup", "AppHover",
        "AppDock", "AppSelected", "AppLabel", "AppInfo", "AppInfoSoft", "AppWarning",
        "AppDanger", "AppSuccess", "AppAccentSoft", "AppAccentHover",
    ];

    [Fact]
    public void Every_token_resolves_in_both_variants()
    {
        Ui.Run(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                foreach (var token in Tokens)
                {
                    Assert.NotNull(Brush(token, variant));
                    Assert.NotNull(ColorOf(token + "Color", variant));
                }

                foreach (var token in PickerTokens)
                {
                    Assert.NotNull(Brush(token, variant));
                }
            }
        });
    }

    [Fact]
    public void The_picker_colours_do_not_follow_the_variant()
    {
        Ui.Run(() =>
        {
            foreach (var token in PickerTokens)
            {
                Assert.Equal(
                    ((ISolidColorBrush)Brush(token, ThemeVariant.Dark)!).Color,
                    ((ISolidColorBrush)Brush(token, ThemeVariant.Light)!).Color);
            }
        });
    }

    [Fact]
    public void Every_legacy_key_resolves_in_both_variants()
    {
        Ui.Run(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                foreach (var key in LegacyKeys)
                {
                    Assert.NotNull(Brush(key, variant));
                }
            }
        });
    }

    private static IBrush? Brush(string key, ThemeVariant variant)
        => Application.Current!.TryGetResource(key, variant, out var value) ? value as IBrush : null;

    private static Color? ColorOf(string key, ThemeVariant variant)
        => Application.Current!.TryGetResource(key, variant, out var value) && value is Color color
            ? color
            : null;
}
