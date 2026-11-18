using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using WhaleGenie;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// A custom theme is one accent colour, and everything else is derived from it. The checks pin the
/// three things that make that safe: what the derivation produces, that a colour which cannot be
/// made readable is refused rather than painted, and that a refusal falls back to the variant's own
/// accent instead of leaving a window with no colour at all.
/// </summary>
[Collection("Theme")]
[RestoreSharedState]
public class ThemePaletteTests
{
    private static readonly Color WindowDark = Color.Parse("#1C1C1C");

    private static readonly Color WindowLight = Color.Parse("#F3F2F0");

    [Fact]
    public void The_built_in_accent_derives_a_set_each_part_of_which_is_readable()
    {
        Assert.True(ThemePalette.TryDerive(Color.Parse(ThemePalette.DefaultAccent), light: false, out var dark));
        Assert.True(ThemePalette.Contrast(dark.Fill, Colors.White) >= 4.5);
        Assert.True(ThemePalette.Contrast(dark.Text, WindowDark) >= 4.5);

        // On a dark window hover goes towards the light and pressed away from it.
        Assert.True(Lightness(dark.Hover) > Lightness(dark.Fill));
        Assert.True(Lightness(dark.Pressed) < Lightness(dark.Fill));

        // The faint surface is the accent laid over the window, and it stays opaque so whatever it
        // is drawn on cannot show through and change it.
        Assert.Equal(255, dark.Surface.A);
        Assert.True(Lightness(dark.Surface) > Lightness(WindowDark));
    }

    [Fact]
    public void The_light_base_derives_the_other_way()
    {
        Assert.True(ThemePalette.TryDerive(Color.Parse(ThemePalette.DefaultAccent), light: true, out var light));
        Assert.True(ThemePalette.Contrast(light.Fill, Colors.White) >= 4.5);
        Assert.True(ThemePalette.Contrast(light.Text, WindowLight) >= 4.5);

        Assert.True(Lightness(light.Hover) < Lightness(light.Fill));
        Assert.True(Lightness(light.Text) < Lightness(light.Fill));
        Assert.True(Lightness(light.Surface) < Lightness(WindowLight));
    }

    [Fact]
    public void A_picked_color_is_moved_until_white_text_fits_on_it()
    {
        var picked = Color.Parse("#FF7043");
        Assert.True(ThemePalette.TryDerive(picked, light: false, out var tokens));

        Assert.True(ThemePalette.Contrast(tokens.Fill, Colors.White) >= 4.5);
        Assert.NotEqual(picked, tokens.Fill);
    }

    [Fact]
    public void A_color_that_cannot_be_made_readable_is_refused()
    {
        // White cannot carry white text at any lightness within reach of the colour it started as.
        Assert.False(ThemePalette.TryDerive(Colors.White, light: false, out _));
        Assert.False(ThemePalette.TryDerive(Colors.White, light: true, out _));
    }

    /// <summary>
    /// Dark colours on the dark window get their accent text lifted as far as it takes, rather than
    /// being refused. These two numbers are the whole evidence for that choice: they are what the
    /// limit on the accent text is worth, and if the limit is ever tightened back to the fill's one
    /// the derivation starts refusing both of these and this check goes red.
    /// </summary>
    [Theory]
    [InlineData("#000000", "#858585", 4.62)]
    [InlineData("#112233", "#4C88C4", 4.57)]
    public void A_dark_accent_keeps_its_text_lifted_rather_than_refused(string accent, string text, double contrast)
    {
        Assert.True(ThemePalette.TryDerive(Color.Parse(accent), light: false, out var tokens));

        Assert.Equal(Color.Parse(text), tokens.Text);
        Assert.Equal(contrast, Math.Round(ThemePalette.Contrast(tokens.Text, WindowDark), 2));
    }

    [Fact]
    public void An_unreadable_or_unparsable_accent_falls_back_to_the_built_in_one()
    {
        Ui.Run(() =>
        {
            var original = ThemeController.Load();
            try
            {
                foreach (var text in new[] { "not a color", null, "#FFFFFF" })
                {
                    ThemePalette.Apply(new ThemeSelection(ThemeMode.Custom, false, text));
                    Assert.Equal(
                        Color.Parse(ThemePalette.DefaultAccent),
                        ColorAt("AccentFill", ThemeVariant.Dark));
                }
            }
            finally
            {
                ThemeController.Apply(original);
            }
        });
    }

    [Fact]
    public void Applying_a_custom_accent_writes_both_shapes()
    {
        Ui.Run(() =>
        {
            var original = ThemeController.Load();
            try
            {
                Assert.True(ThemePalette.TryDerive(Color.Parse("#112233"), light: false, out var tokens));
                ThemePalette.Apply(new ThemeSelection(ThemeMode.Custom, false, "#112233"));

                Assert.Equal(tokens.Fill, ColorAt("AccentFill", ThemeVariant.Dark));
                Assert.Equal(tokens.Fill, ColorAt("AccentFillColor", ThemeVariant.Dark));
                Assert.Equal(tokens.Text, ColorAt("AccentText", ThemeVariant.Dark));
                Assert.Equal(tokens.Surface, ColorAt("AccentSurface", ThemeVariant.Dark));
            }
            finally
            {
                ThemeController.Apply(original);
            }
        });
    }

    [Fact]
    public void The_editor_shows_one_accent_and_the_base_it_sits_on()
    {
        Ui.Run(() =>
        {
            var editor = new ThemePaletteViewModel(new ThemeSelection(ThemeMode.Custom, false, "#112233"));

            Assert.Equal("#112233", editor.AccentHex);
            Assert.False(editor.BaseLight);
            Assert.True(editor.AccentUsable);
        });
    }

    [Fact]
    public void The_editor_says_so_when_the_picked_color_is_refused()
    {
        Ui.Run(() =>
        {
            var original = ThemeController.Load();
            try
            {
                var editor = new ThemePaletteViewModel(new ThemeSelection(ThemeMode.Custom, false, "#112233"));

                editor.Accent = Colors.White;

                Assert.False(editor.AccentUsable);

                // The previews keep showing what the program is actually painting with.
                Assert.Equal(
                    Color.Parse(ThemePalette.DefaultAccent),
                    ColorAt("AccentFill", ThemeVariant.Dark));
            }
            finally
            {
                ThemeController.Apply(original);
            }
        });
    }

    [Fact]
    public void The_settings_offer_custom_theme_and_its_palette_button()
    {
        Ui.Run(() =>
        {
            var folder = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "whalegenie-theme-settings-" + Guid.NewGuid().ToString("N"));
            var stored = LocalSettings.FilePath;
            var original = ThemeController.Load();
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                LocalSettings.FilePath = System.IO.Path.Combine(folder, "settings.json");

                var window = new SettingsWindow();
                window.Show();
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
                Assert.Equal(3, viewModel.ThemeOptions.Count);
                viewModel.SetPage(SettingsRootPage.Appearance, SettingsSubPage.Theme);
                viewModel.SelectedTheme = viewModel.ThemeOptions.Single(option => option.Mode == ThemeMode.Custom);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();

                var button = window.FindControl<Avalonia.Controls.Button>("CustomizeThemeButton");
                Assert.NotNull(button);
                Assert.True(button!.IsVisible);
                Assert.Equal(ThemeMode.Custom, LocalSettings.LoadTheme().Mode);

                window.Close();
            }
            finally
            {
                LocalSettings.FilePath = stored;
                ThemeController.Apply(original);
                if (System.IO.Directory.Exists(folder))
                {
                    System.IO.Directory.Delete(folder, true);
                }
            }
        });
    }

    private static Color ColorAt(string key, ThemeVariant variant)
    {
        Assert.True(Application.Current!.TryGetResource(key, variant, out var value));
        return value switch
        {
            Color colour => colour,
            ISolidColorBrush brush => brush.Color,
            _ => throw new InvalidOperationException(key + " is not a color"),
        };
    }

    private static double Lightness(Color colour) => colour.ToHsl().L;
}
