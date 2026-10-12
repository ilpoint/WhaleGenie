using System;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;

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
            LocalSettings.Store(Language.Chinese);
            LocalSettings.StoreFailureScreenshot(false);

            Assert.Equal(Language.Chinese, LocalSettings.Load());
            Assert.False(LocalSettings.LoadFailureScreenshot());

            // And the other way round: the language going back must not put the picture back.
            LocalSettings.Store(Language.English);
            Assert.Equal(Language.English, LocalSettings.Load());
            Assert.False(LocalSettings.LoadFailureScreenshot());
        });
    }

    [Fact]
    public void A_choice_that_was_never_made_keeps_the_failure_picture_on()
    {
        InOwnFile(_ => Assert.True(LocalSettings.LoadFailureScreenshot()));
    }

    [Fact]
    public void A_file_with_nothing_in_it_falls_back_to_the_starting_choices()
    {
        InOwnFile(path =>
        {
            File.WriteAllText(path, "{}");

            Assert.Equal(Language.English, LocalSettings.Load());
            Assert.True(LocalSettings.LoadFailureScreenshot());
            Assert.Equal(CaptureMethod.Auto, LocalSettings.LoadCaptureWindow());
        });
    }

    [Fact]
    public void The_settings_window_turns_the_failure_picture_on_and_off()
    {
        InOwnFile(_ =>
        {
            LocalSettings.StoreFailureScreenshot(false);

            var viewModel = new SettingsViewModel();
            Assert.False(viewModel.FailureScreenshot);

            viewModel.FailureScreenshot = true;
            Assert.True(LocalSettings.LoadFailureScreenshot());
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
                CaptureSetup.StoreWindow(CaptureMethod.PrintWindow);
                CaptureSetup.StoreScreen(CaptureMethod.DesktopDuplication);

                Assert.Equal(CaptureMethod.PrintWindow, LocalSettings.LoadCaptureWindow());
                Assert.Equal(CaptureMethod.DesktopDuplication, LocalSettings.LoadCaptureScreen());
                Assert.Equal(CaptureMethod.PrintWindow, CaptureChoice.Current.Window);
                Assert.Equal(CaptureMethod.DesktopDuplication, CaptureChoice.Current.Screen);

                // A window opened afterwards shows what is in effect, and putting a choice back is a
                // change like any other.
                var viewModel = new SettingsViewModel();
                Assert.Equal(CaptureMethod.PrintWindow, viewModel.SelectedWindowWay.Way);
                Assert.Equal(CaptureMethod.DesktopDuplication, viewModel.SelectedScreenWay.Way);

                viewModel.SelectedScreenWay = viewModel.ScreenWays.First(way => way.Way == CaptureMethod.Gdi);
                Assert.Equal(CaptureMethod.Gdi, LocalSettings.LoadCaptureScreen());
                Assert.Equal(CaptureMethod.Gdi, CaptureChoice.Current.Screen);
            }
            finally
            {
                CaptureChoice.Current.Window = window;
                CaptureChoice.Current.Screen = screen;
            }
        });
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
