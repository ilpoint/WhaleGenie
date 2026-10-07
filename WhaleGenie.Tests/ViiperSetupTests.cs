using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

/// <summary>
/// The VIIPER server as the user meets it: a panel in the settings window, a choice stored beside
/// the program, and the engine being told about that choice. The server itself is started by the
/// engine, and starting a real one is a thing for a person to do rather than for a check.
/// </summary>
public class ViiperSetupTests
{
    [Fact]
    public void The_download_is_a_page_of_that_project()
    {
        Assert.StartsWith("https://", ProjectLinks.ViiperDownload, StringComparison.Ordinal);
        Assert.Contains("VIIPER", ProjectLinks.ViiperDownload, StringComparison.Ordinal);
        Assert.EndsWith("/releases/latest", ProjectLinks.ViiperDownload, StringComparison.Ordinal);
    }

    [Fact]
    public void The_settings_window_offers_the_server_and_answers_both_buttons()
    {
        var xaml = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Views", "SettingsWindow.axaml"));

        foreach (var (name, handler) in new[]
                 {
                     ("ServerDownloadButton", "OnDownloadViiperClicked"),
                     ("ServerChooseButton", "OnChooseViiperClicked"),
                 })
        {
            var start = xaml.IndexOf($"Name=\"{name}\"", StringComparison.Ordinal);
            Assert.True(start >= 0, $"the settings window no longer offers {name}");
            Assert.Contains($"Click=\"{handler}\"", xaml[start..], StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(true, "Settings.ServerReady")]
    [InlineData(false, "Settings.ServerNotSet")]
    public void The_panel_says_what_the_server_is_doing(bool answering, string key)
    {
        InOwnFile(_ =>
        {
            var viewModel = new SettingsViewModel(() => DriverInputState.Ready, () => answering);

            Assert.Equal(Strings.Get(key), viewModel.ServerStatus);
            Assert.Equal(answering, viewModel.ServerAnswering);
        });
    }

    [Fact]
    public void A_chosen_file_reads_as_a_server_that_will_be_started()
    {
        InOwnFile(_ =>
        {
            LocalSettings.StoreViiperPath(@"C:\somewhere\viiper.exe");

            var viewModel = new SettingsViewModel(() => DriverInputState.Ready, () => false);

            Assert.Equal(Strings.Get("Settings.ServerWillStart"), viewModel.ServerStatus);
            Assert.True(viewModel.HasViiperPath);
            Assert.Equal(@"C:\somewhere\viiper.exe", viewModel.ViiperPath);
        });
    }

    [Fact]
    public void The_chosen_file_is_stored_and_handed_to_the_engine()
    {
        // The engine does not read the settings file — where it lives is the interface's business —
        // so a choice that was only stored would leave driver-level input unable to start anything.
        InOwnFile(path =>
        {
            var engine = ViiperServer.Executable;
            try
            {
                ViiperServer.Executable = null;
                ViiperSetup.Store(path);

                Assert.Equal(path, LocalSettings.LoadViiperPath());
                Assert.Equal(path, ViiperServer.Executable);

                // And the other way round: what is stored is what a fresh start hands over.
                ViiperServer.Executable = null;
                ViiperSetup.Load();

                Assert.Equal(path, ViiperServer.Executable);
            }
            finally
            {
                ViiperServer.Executable = engine;
            }
        });
    }

    [Fact]
    public void The_program_stops_the_server_it_started()
    {
        // Whichever way the window is closed, the program comes back out through Main; a server it
        // started is closed there rather than left running behind the user's back.
        var program = File.ReadAllText(Path.Combine(Repository(), "WhaleGenie", "Program.cs"));

        Assert.Contains("ViiperServer.Stop()", program, StringComparison.Ordinal);
    }

    [Fact]
    public void The_panel_is_written_in_both_languages()
    {
        string[] keys =
        [
            "Settings.Server",
            "Settings.ServerReady",
            "Settings.ServerWillStart",
            "Settings.ServerNotSet",
            "Settings.ServerDownload",
            "Settings.ServerChoose",
            "Settings.ServerFilter",
            "Settings.ServerHint",
        ];

        Assert.All(keys, key =>
        {
            Assert.True(Strings.English.ContainsKey(key), $"English {key} is missing");
            Assert.True(Strings.Chinese.ContainsKey(key), $"Chinese {key} is missing");
        });
    }

    /// <summary>
    /// Runs the check against a settings file of its own. WhaleGenie's own settings belong to
    /// whoever is using it, and a check has no business writing there.
    /// </summary>
    private static void InOwnFile(Action<string> check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-viiper-" + Guid.NewGuid().ToString("N"));
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
