using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>The palette window has to load the official picker and list every semantic color.</summary>
[Collection("Theme")]
public class ThemePaletteWindowTests
{
    [Fact]
    public void The_window_shows_a_picker_and_all_twenty_two_colors()
    {
        Ui.Run(() =>
        {
            var folder = Path.Combine(Path.GetTempPath(), "whalegenie-theme-" + Guid.NewGuid().ToString("N"));
            var stored = LocalSettings.FilePath;
            var original = ThemeController.Load();
            try
            {
                Directory.CreateDirectory(folder);
                LocalSettings.FilePath = Path.Combine(folder, "settings.json");

                var colors = new Dictionary<string, string>(ThemePalette.Defaults(light: false));
                var viewModel = new ThemePaletteViewModel(
                    new ThemeSelection(ThemeMode.Custom, false, colors));
                var window = new ThemePaletteWindow(viewModel);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.Equal(22, viewModel.Entries.Count);
                Assert.Single(window.GetVisualDescendants().OfType<ColorView>());

                viewModel.SelectedEntry.Color = Color.Parse("#123456");
                Dispatcher.UIThread.RunJobs();
                var surface = Assert.IsType<SolidColorBrush>(
                    Application.Current!.Resources["AppSurface"]);
                Assert.Equal(Color.Parse("#123456"), surface.Color);

                window.Close();
            }
            finally
            {
                LocalSettings.FilePath = stored;
                ThemeController.Apply(original);
                if (Directory.Exists(folder))
                {
                    Directory.Delete(folder, true);
                }
            }
        });
    }
}
