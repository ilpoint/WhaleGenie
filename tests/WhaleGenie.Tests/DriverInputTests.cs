using System.IO;
using Avalonia.Controls;
using Avalonia.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Platform.Windows;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The driver-level input panel of the settings window. It is where the one piece of that feature a
/// user still installs is reported, together with the switch that decides whether the virtual
/// devices go on the machine at startup, so both what the panel says and where its button points are
/// worth pinning down.
/// </summary>
public class DriverInputTests
{
    [Fact]
    public void The_install_button_opens_the_drivers_own_download_page()
    {
        // Another project's page, and the newest release on purpose: a version written down here
        // would be wrong the day after that project released again.
        Assert.StartsWith("https://", ProjectLinks.DriverDownload, StringComparison.Ordinal);
        Assert.Contains("usbip-win2", ProjectLinks.DriverDownload, StringComparison.Ordinal);
        Assert.EndsWith("/releases/latest", ProjectLinks.DriverDownload, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DriverInputState.Ready, "Settings.DriverReady", true)]
    [InlineData(DriverInputState.DriverMissing, "Settings.DriverMissing", false)]
    [InlineData(DriverInputState.LibraryMissing, "Settings.DriverLibraryMissing", false)]
    public void The_panel_says_which_piece_is_still_missing(
        DriverInputState state, string key, bool ready)
    {
        var viewModel = new SettingsViewModel(() => state);

        Assert.Equal(state, viewModel.DriverState);
        Assert.Equal(ready, viewModel.DriverReady);

        // The sentence has to be the one that belongs to the state: reporting the wrong piece sends
        // the user to install something that is already there.
        Assert.Equal(Strings.Get(key), viewModel.DriverStatus);
        Assert.NotEqual(key, viewModel.DriverStatus);
    }

    [Fact]
    public void The_panel_is_written_in_both_languages()
    {
        string[] keys =
        [
            "Settings.Driver",
            "Settings.DriverReady",
            "Settings.DriverMissing",
            "Settings.DriverLibraryMissing",
            "Settings.DriverInstall",
            "Settings.DriverHint",
            "Settings.DriverAttachAtStart",
            "Settings.DriverAttachAtStartHint",
        ];

        Assert.All(keys, key =>
        {
            Assert.True(Strings.English.ContainsKey(key), $"English {key} is missing");
            Assert.True(Strings.Chinese.ContainsKey(key), $"Chinese {key} is missing");
        });
    }

    [Fact]
    public void Asking_about_this_machine_answers_rather_than_failing()
    {
        // The look reads one registry key and looks for one file beside the program. Whatever this
        // machine has, asking has to come back with one of the three answers.
        Assert.True(Enum.IsDefined(DriverInput.Check()));
    }

}
