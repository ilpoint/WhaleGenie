using System;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The file the settings window writes. Every choice lives in the one file, so storing one has to
/// leave the others where they were — that is the mistake this file is here to catch.
/// </summary>
public class LocalSettingsTests
{
    [Fact]
    public void Storing_one_choice_leaves_the_other_alone()
    {
        InOwnFile(_ =>
        {
            LocalSettings.StorePreloadOcr(true);
            LocalSettings.Store(Language.Chinese);

            Assert.Equal(Language.Chinese, LocalSettings.Load());
            Assert.True(LocalSettings.LoadPreloadOcr());

            // And the other way round: the language going back must not move the other choice.
            LocalSettings.Store(Language.English);
            Assert.Equal(Language.English, LocalSettings.Load());
            Assert.True(LocalSettings.LoadPreloadOcr());
        });
    }

    [Fact]
    public void A_file_with_nothing_in_it_falls_back_to_the_starting_choices()
    {
        InOwnFile(path =>
        {
            File.WriteAllText(path, "{}");

            Assert.Equal(Language.English, LocalSettings.Load());
            Assert.Equal(CaptureWay.Auto, LocalSettings.LoadCaptureWindow());
        });
    }

    /// <summary>
    /// How this machine reads the screen is chosen in the settings window and written down like every
    /// other choice, and the engine hears about it in the same breath: a macro that is running reads
    /// the change at its next look.
    /// </summary>
    [Fact]
    public void The_settings_window_chooses_how_the_screen_is_read()
    {
        InOwnFile(_ =>
        {
            var window = CaptureChoice.Current.Window;
            var screen = CaptureChoice.Current.Screen;
            try
            {
                CaptureSetup.StoreWindow(CaptureWay.PrintWindow);
                CaptureSetup.StoreScreen(CaptureWay.DesktopDuplication);

                Assert.Equal(CaptureWay.PrintWindow, LocalSettings.LoadCaptureWindow());
                Assert.Equal(CaptureWay.DesktopDuplication, LocalSettings.LoadCaptureScreen());
                Assert.Equal(CaptureWay.PrintWindow, CaptureChoice.Current.Window);
                Assert.Equal(CaptureWay.DesktopDuplication, CaptureChoice.Current.Screen);

                // A window opened afterwards shows what is in effect, and putting a choice back is a
                // change like any other.
                var viewModel = new SettingsViewModel();
                Assert.Equal(CaptureWay.PrintWindow, viewModel.SelectedWindowWay.Way);
                Assert.Equal(CaptureWay.DesktopDuplication, viewModel.SelectedScreenWay.Way);

                viewModel.SelectedScreenWay = viewModel.ScreenWays.First(way => way.Way == CaptureWay.Gdi);
                Assert.Equal(CaptureWay.Gdi, LocalSettings.LoadCaptureScreen());
                Assert.Equal(CaptureWay.Gdi, CaptureChoice.Current.Screen);
            }
            finally
            {
                CaptureChoice.Current.Window = window;
                CaptureChoice.Current.Screen = screen;
            }
        });
    }

    /// <summary>
    /// Whether the text recognition models are loaded as the program starts is a choice like the
    /// others, and it is off until it is turned on: the models hold memory for the rest of the run.
    /// </summary>
    [Fact]
    public void The_settings_window_chooses_whether_the_models_are_loaded_at_startup()
    {
        InOwnFile(_ =>
        {
            Assert.False(LocalSettings.LoadPreloadOcr());

            var viewModel = new SettingsViewModel();
            Assert.False(viewModel.PreloadOcr);

            viewModel.PreloadOcr = true;
            Assert.True(LocalSettings.LoadPreloadOcr());

            // Off again, which is what a machine that never reads text wants, and a window opened
            // afterwards shows whichever of the two is in effect.
            viewModel.PreloadOcr = false;
            Assert.False(LocalSettings.LoadPreloadOcr());
            Assert.False(new SettingsViewModel().PreloadOcr);
        });
    }

    /// <summary>
    /// The switch itself, as the window draws it: a choice nothing on the window is wired to would
    /// store whatever the window happened to hold rather than what the person clicked.
    /// </summary>
    [Fact]
    public void The_settings_window_carries_the_switch_that_loads_the_models()
    {
        InOwnFile(_ => Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();

            var box = window.FindControl<CheckBox>("PreloadOcrCheck");
            Assert.True(box is not null, "the settings window stopped offering to load the models");
            Assert.False(box.IsChecked);

            box.IsChecked = true;
            Assert.True(LocalSettings.LoadPreloadOcr());

            window.Close();
        }));
    }

    /// <summary>
    /// Runs the check against a file of its own, then puts the real one back. WhaleGenie's own settings
    /// belong to whoever is using it, and a check has no business writing there.
    /// </summary>
    private static void InOwnFile(Action<string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-settings-" + Guid.NewGuid().ToString("N"));
        var stored = LocalSettings.FilePath;
        try
        {
            Directory.CreateDirectory(folder);
            LocalSettings.FilePath = Path.Combine(folder, "settings.json");
            check(LocalSettings.FilePath);
        }
        finally
        {
            LocalSettings.FilePath = stored;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
