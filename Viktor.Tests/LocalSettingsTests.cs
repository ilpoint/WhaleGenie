using System;
using System.IO;
using Viktor.Localization;
using Viktor.ViewModels;

namespace Viktor.Tests;

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
    /// Runs the check against a file of its own, then puts the real one back. Viktor's own settings
    /// belong to whoever is using it, and a check has no business writing there.
    /// </summary>
    private static void InOwnFile(Action<string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "viktor-settings-" + Guid.NewGuid().ToString("N"));
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
