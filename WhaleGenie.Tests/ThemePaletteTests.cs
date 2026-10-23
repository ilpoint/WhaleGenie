using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using WhaleGenie;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The custom palette has to keep the built-in values intact, and a custom value that cannot be
/// read must fall back to the variant underneath rather than make a window invisible.
/// </summary>
[Collection("Theme")]
public class ThemePaletteTests
{
    [Fact]
    public void The_built_in_palettes_still_have_the_same_values()
    {
        Assert.Equal(22, ThemePalette.Brushes.Count);

        var dark = ThemePalette.Defaults(light: false);
        var light = ThemePalette.Defaults(light: true);

        Assert.Equal("#292929", dark["AppSurface"]);
        Assert.Equal("#FAF8F4", light["AppSurface"]);
        Assert.Equal("#C8401C", dark["AppAccent"]);
        Assert.Equal("#2B2723", light["AppText"]);

        Assert.All(ThemePalette.Brushes, brush =>
        {
            Assert.NotEqual(brush.LabelKey, Strings.Get(brush.LabelKey));
            Assert.NotEqual(brush.GroupKey, Strings.Get(brush.GroupKey));
        });
    }

    [Fact]
    public void A_custom_color_wins_and_a_bad_one_falls_back_to_the_base_variant()
    {
        var brushes = ThemePalette.Brushes.ToDictionary(brush => brush.Key);
        var selection = new ThemeSelection(ThemeMode.Custom, false,
            new Dictionary<string, string>
            {
                ["AppAccent"] = "#112233",
                ["AppSurface"] = "not a color",
            });

        Assert.Equal(Color.Parse("#112233"), ThemePalette.Resolve(brushes["AppAccent"], selection));
        Assert.Equal(Color.Parse("#292929"), ThemePalette.Resolve(brushes["AppSurface"], selection));
        Assert.True(ThemePalette.TryParse("#AABBCC", out var parsed));
        Assert.Equal(Color.Parse("#AABBCC"), parsed);
        Assert.False(ThemePalette.TryParse("nope", out _));
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

    [Fact]
    public void Applying_a_custom_palette_changes_the_live_resource()
    {
        Ui.Run(() =>
        {
            var original = ThemeController.Load();
            try
            {
                var colors = new Dictionary<string, string>(ThemePalette.Defaults(light: false))
                {
                    ["AppAccent"] = "#123456",
                };
                ThemePalette.Apply(new ThemeSelection(ThemeMode.Custom, false, colors));

                var brush = Assert.IsType<SolidColorBrush>(
                    Application.Current!.Resources["AppAccent"]);
                Assert.Equal(Color.Parse("#123456"), brush.Color);
            }
            finally
            {
                ThemeController.Apply(original);
            }
        });
    }

    [Fact]
    public void The_editor_lists_every_color_in_its_group()
    {
        Ui.Run(() =>
        {
            var colors = new Dictionary<string, string>(ThemePalette.Defaults(light: false));
            var editor = new ThemePaletteViewModel(
                new ThemeSelection(ThemeMode.Custom, false, colors));

            Assert.Equal(22, editor.Entries.Count);
            Assert.Equal(3, editor.Groups.Count);
            Assert.Equal("AppSurface", editor.SelectedEntry.Key);
            Assert.All(editor.Groups, group => Assert.NotEmpty(group.Entries));
        });
    }
}
