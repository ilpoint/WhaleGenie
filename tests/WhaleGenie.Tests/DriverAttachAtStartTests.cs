using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The switch that puts the virtual keyboard and mouse on the machine as the program starts: the
/// choice is stored beside the program, the engine is told about it when the program starts, and
/// nothing is put on the machine until that switch is on.
/// </summary>
[RestoreSharedState]
public class DriverAttachAtStartTests
{
    [Fact]
    public void A_machine_nobody_has_switched_it_on_for_starts_with_it_off()
    {
        InOwnFile(_ =>
        {
            Assert.False(LocalSettings.LoadDriverAttachAtStart());

            var viewModel = new SettingsViewModel(() => DriverInputState.Ready);

            Assert.False(viewModel.AttachDriverAtStart);
        });
    }

    [Fact]
    public void Switching_it_on_is_what_is_read_back_the_next_time()
    {
        InOwnFile(_ =>
        {
            var viewModel = new SettingsViewModel(() => DriverInputState.Ready)
            {
                AttachDriverAtStart = true,
            };

            Assert.True(LocalSettings.LoadDriverAttachAtStart());
            Assert.True(viewModel.AttachDriverAtStart);

            var reopened = new SettingsViewModel(() => DriverInputState.Ready);

            Assert.True(reopened.AttachDriverAtStart);
        });
    }

    [Fact]
    public void The_program_hands_the_stored_choice_to_the_engine_as_it_starts()
    {
        // The engine does not read the settings file — where it lives is the interface's business —
        // so a choice that was only stored would never put anything on the machine.
        var app = File.ReadAllText(RepoPaths.Source("WhaleGenie", "App.axaml.cs"));

        Assert.Contains(
            "Driver.AttachAtStart = LocalSettings.LoadDriverAttachAtStart()", app, StringComparison.Ordinal);
        Assert.Contains("Driver.WarmUp()", app, StringComparison.Ordinal);
    }

    [Fact]
    public void The_program_takes_the_virtual_devices_off_the_machine_as_it_exits()
    {
        // Whichever way the window is closed, the program comes back out through Main: the pair it
        // put on the machine and the server it runs in itself are let go there rather than left
        // behind.
        var program = File.ReadAllText(RepoPaths.Source("WhaleGenie", "Program.cs"));

        Assert.Contains(".Driver.Stop()", program, StringComparison.Ordinal);
    }

    /// <summary>
    /// Runs the check against a settings file of its own. WhaleGenie's own settings belong to whoever
    /// is using it, and a check has no business writing there.
    /// </summary>
    private static void InOwnFile(Action<string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-driver-" + Guid.NewGuid().ToString("N"));
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
            Directory.Delete(folder, true);
        }
    }
}
