using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>The accent window has to load the official picker and show what the colour becomes.</summary>
[Collection("Theme")]
[RestoreSharedState]
public class ThemePaletteWindowTests
{
    [Fact]
    public void The_window_shows_one_picker_and_three_previews()
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

                var viewModel = new ThemePaletteViewModel(
                    new ThemeSelection(ThemeMode.Custom, false, "#112233"));
                var window = new ThemePaletteWindow(viewModel);
                window.Show();
                Dispatcher.UIThread.RunJobs();

                Assert.Single(window.GetVisualDescendants().OfType<ColorView>());

                // The three previews paint the derived colours, not the picked one: the window has
                // to show what the rest of the program will look like.
                Assert.True(ThemePalette.TryDerive(Color.Parse("#112233"), light: false, out var tokens));
                Assert.Equal(tokens.Fill, Fill(viewModel.FillPreview));
                Assert.Equal(tokens.Surface, Fill(viewModel.SurfacePreview));
                Assert.Equal(tokens.Text, Fill(viewModel.TextPreview));

                var next = Color.Parse("#2E5AAC");
                viewModel.Accent = next;
                Dispatcher.UIThread.RunJobs();

                Assert.True(ThemePalette.TryDerive(next, light: false, out var expected));
                Assert.True(Application.Current!.TryGetResource("AccentFill", ThemeVariant.Dark, out var live));
                Assert.Equal(expected.Fill, Fill(Assert.IsAssignableFrom<IBrush>(live)));

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

    /// <summary>
    /// The picker lays itself out at its own natural size — the tab content is 350 wide and the
    /// whole control about 400 tall — and the window used to be 420 tall with the preview cards
    /// underneath. The row left for the picker was 82 pixels, so the palette tab was cut off by the
    /// cards: the window was pressing on a control that does not shrink. The contract now is a
    /// resizable window, a picker never put under 380, and a scrollable page for a short screen.
    /// </summary>
    [Fact]
    public void The_picker_is_given_its_own_height()
    {
        Ui.Run(() =>
        {
            var window = new ThemePaletteWindow(new ThemePaletteViewModel());
            window.Show();
            Dispatcher.UIThread.RunJobs();

            try
            {
                Assert.True(window.CanResize, "the window cannot be made taller");
                var picker = Assert.Single(window.GetVisualDescendants().OfType<ColorView>());
                Assert.True(picker.Bounds.Height >= 380,
                    $"the picker is {picker.Bounds.Height:0.#} tall, so its palette is being cut");

                // The tab content has to end inside the picker: when the picker itself is shorter
                // than its content, the palette is what gets covered — silently, because nothing
                // clips at this level.
                var tabs = picker.GetVisualDescendants().OfType<TabControl>()
                    .Single(control => control.Name == "PART_TabControl");
                Assert.True(tabs.Bounds.Bottom <= picker.Bounds.Height + 0.5,
                    $"the tabs end at {tabs.Bounds.Bottom:0.#} inside a {picker.Bounds.Height:0.#} tall picker");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Color Fill(IBrush brush) => Assert.IsAssignableFrom<ISolidColorBrush>(brush).Color;
}
