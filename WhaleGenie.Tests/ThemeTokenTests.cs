using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
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

    /// <summary>
    /// Where each old name lands. The old keys carry no colour of their own any more, so this is
    /// also the check that the table in LegacyKeys.axaml points where it says it does.
    /// </summary>
    private static readonly (string Old, string Token)[] Mapped =
    [
        ("AppBackdrop", "SurfaceWindow"),
        ("AppChrome", "SurfaceChrome"),
        ("AppSurface", "SurfacePanel"),
        ("AppDock", "SurfacePanel"),
        ("AppField", "SurfaceField"),
        ("AppBorder", "BorderControl"),
        ("AppControl", "SurfaceControl"),
        ("AppGroup", "SurfaceSunken"),
        ("AppHover", "SurfaceHover"),
        ("AppSelected", "AccentSurface"),
        ("AppAccentSoft", "AccentSurface"),
        ("AppAccent", "AccentFill"),
        ("AppAccentHover", "AccentFillHover"),
        ("AppText", "TextPrimary"),
        ("AppTextStrong", "TextPrimary"),
        ("AppMuted", "TextTertiary"),
        ("AppLabel", "Warning"),
        ("AppWarning", "Warning"),
        ("AppInfo", "Info"),
        ("AppInfoSoft", "InfoSurface"),
        ("AppDanger", "Danger"),
        ("AppSuccess", "Success"),
    ];

    [Fact]
    public void Every_legacy_key_paints_the_token_it_stands_for()
    {
        Ui.Run(() =>
        {
            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                foreach (var (oldKey, token) in Mapped)
                {
                    Assert.Equal(Colour(token, variant), Colour(oldKey, variant));
                }
            }
        });
    }

    private static Color Colour(string key, ThemeVariant variant)
    {
        var brush = Brush(key, variant);
        Assert.NotNull(brush);
        return Assert.IsAssignableFrom<ISolidColorBrush>(brush!).Color;
    }

    /// <summary>
    /// The tokens that are not colours — fonts, sizes, spacing, radii — are read out of the file the
    /// same way, because a missing one of those does not fail anything either: a style that names it
    /// just gets no value, and the next person fills the gap with a literal.
    /// </summary>
    [Fact]
    public void Every_declared_token_resolves_in_the_dark_variant()
    {
        Ui.Run(() =>
        {
            var tokens = Path.Combine(RepositoryRoot(), "WhaleGenie", "Themes", "Tokens.axaml");
            var keys = DeclaredKeys(tokens);

            Assert.Contains("UiCodeFont", keys, StringComparer.Ordinal);
            Assert.Contains("PadSection", keys, StringComparer.Ordinal);
            Assert.Contains("GapSection", keys, StringComparer.Ordinal);

            foreach (var key in keys)
            {
                Assert.True(
                    Application.Current!.TryGetResource(key, ThemeVariant.Dark, out _),
                    $"{key} is declared in Tokens.axaml but does not resolve");
            }
        });
    }

    /// <summary>
    /// Layer one is a table of aliases: each Fluent key names the token it should paint with. An alias
    /// that names a key nobody can see throws when it is read, and an alias written to the wrong key
    /// silently paints the wrong colour — both are invisible until somebody looks. So the table is
    /// read back out of the file and every entry is compared with the token it claims, in both
    /// variants.
    /// </summary>
    [Fact]
    public void Every_fluent_key_paints_the_token_it_names()
    {
        Ui.Run(() =>
        {
            var path = Path.Combine(RepositoryRoot(), "WhaleGenie", "Themes", "FluentKeys.axaml");
            if (!File.Exists(path))
            {
                Assert.Fail($"the layer-one dictionary is not where the repository root says it is: {path}");
            }

            var xml = File.ReadAllText(path);
            var aliases = Regex.Matches(xml, "x:Key=\"([^\"]+)\" ResourceKey=\"([^\"]+)\"")
                .Select(match => (Key: match.Groups[1].Value, Token: match.Groups[2].Value))
                .Distinct()
                .ToList();
            Assert.NotEmpty(aliases);

            foreach (var variant in new[] { ThemeVariant.Dark, ThemeVariant.Light })
            {
                foreach (var (key, token) in aliases)
                {
                    Assert.True(
                        Application.Current!.TryGetResource(token, variant, out var expected),
                        $"{token} is named by {key} but does not resolve");
                    Assert.True(
                        Application.Current.TryGetResource(key, variant, out var actual),
                        $"{key} does not resolve in {variant}");

                    var same = actual is ISolidColorBrush painted && expected is ISolidColorBrush token1
                        ? painted.Color == token1.Color
                        : Equals(actual, expected);
                    Assert.True(same, $"{key} paints {actual} but {token} is {expected} in {variant}");
                }
            }
        });
    }

    /// <summary>
    /// Every key the dictionary declares a value for. The theme dictionaries' own <c>x:Key="Dark"</c>
    /// is a container rather than a value, so the pattern names the value elements instead of taking
    /// every key in the file.
    /// </summary>
    private static HashSet<string> DeclaredKeys(string path)
    {
        if (!File.Exists(path))
        {
            Assert.Fail($"the dictionary is not where the repository root says it is: {path}");
        }

        var xml = File.ReadAllText(path);
        var pattern = "<(?:Color|SolidColorBrush|FontFamily|sys:Double|Thickness|CornerRadius)"
            + "\\s+x:Key=\"([^\"]+)\"";
        return Regex.Matches(xml, pattern)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void The_lists_above_match_the_dictionaries_they_describe()
    {
        // The three arrays are a second copy of what the two dictionaries declare, and a second copy
        // is what drifts. These read the dictionaries themselves: a key added there and forgotten
        // here would otherwise go untested, and a key renamed in one place only would keep passing
        // the checks above, which walk this array rather than the file.
        var tokens = Path.Combine(RepositoryRoot(), "WhaleGenie", "Themes", "Tokens.axaml");
        var legacy = Path.Combine(RepositoryRoot(), "WhaleGenie", "Themes", "LegacyKeys.axaml");

        Assert.Equal(
            Tokens.Concat(PickerTokens).ToHashSet(StringComparer.Ordinal),
            Declared(tokens, "SolidColorBrush"));

        Assert.Equal(
            LegacyKeys.ToHashSet(StringComparer.Ordinal),
            Declared(legacy, "SolidColorBrush"));
    }

    /// <summary>
    /// The brush keys a dictionary declares. The pattern is pinned to the whole opening tag: loosened
    /// to the key alone it would also collect the forty-three *Color entries, which are not brushes.
    /// </summary>
    private static HashSet<string> Declared(string path, string element)
    {
        if (!File.Exists(path))
        {
            Assert.Fail($"the dictionary is not where the repository root says it is: {path}");
        }

        var xml = File.ReadAllText(path);
        Assert.Contains($"<{element}", xml, StringComparison.Ordinal);

        var pattern = "<" + element + "\\s+x:Key=\"([^\"]+)\"";
        return Regex.Matches(xml, pattern)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Where this file's own path says the repository is. The checks run out of bin, where the
    /// dictionaries are copied but the repository layout is not, so the path is taken from the
    /// source file rather than from the working directory.
    /// </summary>
    private static string RepositoryRoot([CallerFilePath] string here = "")
    {
        var folder = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "WhaleGenie", "Themes")))
        {
            folder = folder.Parent;
        }

        if (folder is null)
        {
            Assert.Fail($"the repository root was not found above {here}; check where the tests are built");
        }

        return folder!.FullName;
    }

    private static IBrush? Brush(string key, ThemeVariant variant)
        => Application.Current!.TryGetResource(key, variant, out var value) ? value as IBrush : null;

    private static Color? ColorOf(string key, ThemeVariant variant)
        => Application.Current!.TryGetResource(key, variant, out var value) && value is Color color
            ? color
            : null;
}
