using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Localization;
using WhaleGenie.Runtime;
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
        var viewModel = new SettingsViewModel(() => DriverInputState.Ready, elevated);

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

    /// <summary>
    /// 什么时候该把这一次启动换成提权的那一份：没提权、设置里要了、而且这一次不是被请求起来的
    /// 那一份。最后一条是防止 UAC 被拒之后一直重开 —— 被请求起来的那一份再请求，就成了一个环。
    /// </summary>
    [Fact]
    public void Only_a_start_that_was_asked_for_hands_over_to_an_elevated_copy()
    {
        Assert.True(ProcessRights.WantsElevation(elevated: false, asked: true, []));
        Assert.False(ProcessRights.WantsElevation(elevated: true, asked: true, []));
        Assert.False(ProcessRights.WantsElevation(elevated: false, asked: false, []));
        Assert.False(ProcessRights.WantsElevation(elevated: false, asked: true,
            [ProcessRights.ElevatedFlag]));
    }

    /// <summary>设置页上那一格与重启按钮并排：一个管下一次启动，一个管这一次。</summary>
    [Fact]
    public void The_panel_offers_the_choice_as_well_as_the_restart()
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", "SettingsWindow.axaml"));

        var start = xaml.IndexOf("Name=\"RunAsAdminCheck\"", StringComparison.Ordinal);
        Assert.True(start >= 0, "the settings window stopped offering to start elevated");
        Assert.Contains("IsChecked=\"{Binding RunAsAdmin}\"", xaml[start..], StringComparison.Ordinal);
        Assert.Contains("[Settings.RunAsAdmin]", xaml[start..], StringComparison.Ordinal);
        Assert.Contains("[Settings.RunAsAdminHint]", xaml[start..], StringComparison.Ordinal);
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
