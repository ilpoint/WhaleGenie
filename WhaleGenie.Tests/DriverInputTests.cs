using System.IO;
using Avalonia.Controls;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The driver-level input panel of the settings window. It is where the two pieces that feature
/// needs are reported, and the driver itself is only ever fetched by hand, so both what the panel
/// says and where its button points are worth pinning down.
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

    [Fact]
    public void The_settings_window_has_a_button_that_installs_the_driver()
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", "SettingsWindow.axaml"));

        // A button that looks like it installs something and does nothing when clicked is exactly
        // the kind of thing the help menu used to be full of.
        var start = xaml.IndexOf("Name=\"DriverInstallButton\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the settings window stopped offering to install the driver");
        Assert.Contains("Click=\"OnInstallDriverClicked\"", xaml[start..], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DriverInputState.Ready, "Settings.DriverReady", true)]
    [InlineData(DriverInputState.DriverMissing, "Settings.DriverMissing", false)]
    [InlineData(DriverInputState.ServerMissing, "Settings.DriverServerMissing", false)]
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
            "Settings.DriverServerMissing",
            "Settings.DriverInstall",
            "Settings.DriverHint",
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
        // The probe reads one registry key and makes one connection to this machine. Whatever this
        // machine has, asking has to come back with one of the three answers.
        Assert.True(Enum.IsDefined(DriverInput.Check()));
    }

    [Fact]
    public void The_window_comes_up_showing_the_panel()
    {
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            Assert.False(string.IsNullOrWhiteSpace(viewModel.DriverStatus));

            var install = window.FindControl<Button>("DriverInstallButton");
            Assert.True(install is not null && install.IsVisible);

            window.Close();
        });
    }

    [Fact]
    public void The_settings_window_is_tall_enough_for_what_it_says()
    {
        // The panel is one more block in a window that was sized by hand, and the wording is longer
        // in one language than the other. What is below the window edge is text nobody can read.
        Ui.Run(() =>
        {
            var window = new SettingsWindow();
            window.Show();

            var panel = window.FindControl<StackPanel>("SettingsPanel")!;
            var wanted = panel.Children.OfType<Control>()
                .Sum(child => child.DesiredSize.Height + child.Margin.Top + child.Margin.Bottom);

            var breakdown = string.Join(" | ", panel.Children.OfType<Control>()
                .Select(child => $"{child.GetType().Name}={child.DesiredSize.Height + child.Margin.Top + child.Margin.Bottom:0}"));
            Assert.True(wanted <= panel.Bounds.Height,
                $"the settings panel wants {wanted:0} pixels but is given {panel.Bounds.Height:0}: {breakdown}");

            window.Close();
        });
    }

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
