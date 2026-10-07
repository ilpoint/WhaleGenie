using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The one thing about running as an administrator that the program can say and offer: a hotkey is
/// read through a system hook, and Windows does not let that hook see the keys going to a window of
/// a higher privilege. Wanting them means starting a second copy with the administrator token, and
/// that is decided at launch — a running process cannot be given one.
/// </summary>
public class AdministratorTests
{
    [Fact]
    public void Asking_about_this_process_answers_rather_than_failing()
    {
        // Whatever this machine is, the answer is one of the two, and asking never throws.
        Assert.IsType<bool>(ProcessRights.IsElevated);
    }

    [Theory]
    [InlineData(true, "Settings.AdminYes")]
    [InlineData(false, "Settings.AdminNo")]
    public void The_panel_says_which_rights_the_program_has(bool elevated, string key)
    {
        var viewModel = new SettingsViewModel(() => DriverInputState.Ready, () => true, elevated);

        Assert.Equal(elevated, viewModel.IsElevated);
        Assert.Equal(Strings.Get(key), viewModel.AdminStatus);
        Assert.NotEqual(key, viewModel.AdminStatus);
    }

    [Fact]
    public void The_settings_window_offers_a_restart_that_answers_the_click()
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", "SettingsWindow.axaml"));

        var start = xaml.IndexOf("Name=\"AdminRestartButton\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the settings window stopped offering to restart as administrator");
        Assert.Contains("Click=\"OnRestartElevatedClicked\"", xaml[start..], StringComparison.Ordinal);

        // Nothing to restart when the program already has the rights, so the button is not there.
        Assert.Contains("IsVisible=\"{Binding !IsElevated}\"", xaml[start..], StringComparison.Ordinal);
    }

    [Fact]
    public void The_manifest_leaves_the_choice_to_whoever_starts_the_program()
    {
        // requireAdministrator would put a UAC prompt in front of everybody, including the macros
        // that never touch an elevated window.
        var manifest = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "app.manifest"));

        Assert.Contains("level=\"asInvoker\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("requireAdministrator", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_is_written_in_both_languages()
    {
        string[] keys =
        [
            "Settings.Admin",
            "Settings.AdminYes",
            "Settings.AdminNo",
            "Settings.AdminHint",
            "Settings.AdminRestart",
        ];

        Assert.All(keys, key =>
        {
            Assert.True(Strings.English.ContainsKey(key), $"English {key} is missing");
            Assert.True(Strings.Chinese.ContainsKey(key), $"Chinese {key} is missing");
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
