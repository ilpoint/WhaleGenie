using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The file the settings window writes. Every choice lives in the one file, so storing one has to
/// leave the others where they were — that is the mistake this file is here to catch.
/// </summary>
[RestoreSharedState]
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

    /// <summary>
    /// 三项启动设置：默认与今天的行为一致（打开上次项目、系统不随启动启用、宏按原顺序），
    /// 改一项不碰其他项。默认值就是升级后第一次打开时的样子，不能悄悄改变既有行为。
    /// </summary>
    [Fact]
    public void The_start_settings_default_to_what_the_program_did_before()
    {
        InOwnFile(_ =>
        {
            Assert.True(LocalSettings.LoadLastProjectAtStart());
            Assert.False(LocalSettings.LoadSystemAtStart());
            Assert.False(LocalSettings.LoadEnabledFirst());

            LocalSettings.StoreSystemAtStart(true);
            LocalSettings.StoreEnabledFirst(true);
            LocalSettings.StoreLastProjectAtStart(false);

            Assert.False(LocalSettings.LoadLastProjectAtStart());
            Assert.True(LocalSettings.LoadSystemAtStart());
            Assert.True(LocalSettings.LoadEnabledFirst());

            // 另一个选择写一次，三项都不动。
            LocalSettings.StorePreloadOcr(true);
            Assert.False(LocalSettings.LoadLastProjectAtStart());
            Assert.True(LocalSettings.LoadSystemAtStart());
            Assert.True(LocalSettings.LoadEnabledFirst());
        });
    }

    /// <summary>
    /// 启动时启用系统：新建主视图模型时按设置决定系统状态。默认关闭，就是按热键启用之前的样子。
    /// </summary>
    [Fact]
    public void The_system_can_be_enabled_by_the_start_setting()
    {
        InOwnFile(_ =>
        {
            Assert.False(new MainViewModel().IsRunning);

            LocalSettings.StoreSystemAtStart(true);
            Assert.True(new MainViewModel().IsRunning);
        });
    }

    /// <summary>
    /// 已启用的宏排在前面：关闭时保持项目原有顺序，打开后只把两组分开，组内顺序不变。
    /// </summary>
    [Fact]
    public void Enabled_macros_can_be_listed_first_without_reordering_each_group()
    {
        InOwnFile(_ =>
        {
            var viewModel = new MainViewModel();
            viewModel.SelectedDock = DockPage.Keys;

            viewModel.AddMacro(new MacroItem { Name = "停用一", IsEnabled = false });
            viewModel.AddMacro(new MacroItem { Name = "启用一" });
            viewModel.AddMacro(new MacroItem { Name = "停用二", IsEnabled = false });
            viewModel.AddMacro(new MacroItem { Name = "启用二" });

            Assert.Equal(["停用一", "启用一", "停用二", "启用二"],
                viewModel.VisibleMacros.Select(macro => macro.Name));

            LocalSettings.StoreEnabledFirst(true);
            viewModel.RefreshVisibleMacros();

            Assert.Equal(["启用一", "启用二", "停用一", "停用二"],
                viewModel.VisibleMacros.Select(macro => macro.Name));
        });
    }

    /// <summary>
    /// The package that was open last, so the next start does not begin with an empty list. A path
    /// that no longer exists is kept: the work may be on a drive that is not plugged in.
    /// </summary>
    [Fact]
    public void The_last_project_is_remembered()
    {
        InOwnFile(_ =>
        {
            Assert.Equal(string.Empty, LocalSettings.LastProject());

            LocalSettings.StoreLastProject(@"D://宏//我的.wgmacro");
            Assert.Equal(@"D://宏//我的.wgmacro", LocalSettings.LastProject());

            // Storing another choice leaves it alone, like every other choice in this file.
            LocalSettings.StorePreloadOcr(true);
            Assert.Equal(@"D://宏//我的.wgmacro", LocalSettings.LastProject());
        });
    }

    [Fact]
    public void A_file_with_nothing_in_it_falls_back_to_the_starting_choices()
    {
        InOwnFile(path =>
        {
            File.WriteAllText(path, "{}");

            Assert.Equal(LocalSettings.ForCulture(CultureInfo.CurrentUICulture), LocalSettings.Load());
            Assert.Equal(CaptureWay.Auto, LocalSettings.LoadCaptureWindow());
        });
    }

    [Theory]
    [InlineData("zh-CN", Language.Chinese)]
    [InlineData("zh-TW", Language.Chinese)]
    [InlineData("en-US", Language.English)]
    [InlineData("ja-JP", Language.English)]
    public void A_machine_that_has_not_chosen_a_language_follows_its_system_language(
        string culture, Language expected)
        => Assert.Equal(expected, LocalSettings.ForCulture(CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void A_stored_language_wins_over_the_system_language()
    {
        InOwnFile(_ =>
        {
            var system = LocalSettings.ForCulture(CultureInfo.CurrentUICulture);
            var chosen = system == Language.Chinese ? Language.English : Language.Chinese;

            LocalSettings.Store(chosen);

            Assert.Equal(chosen, LocalSettings.Load());
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

    [Fact]
    public void Trigger_shortcuts_and_the_selected_dock_page_are_remembered()
    {
        InOwnFile(_ =>
        {
            Assert.Equal("Home", LocalSettings.LoadSystemHotkey());
            Assert.Equal(DockPage.Keys, LocalSettings.LoadDockPage());
            Assert.Equal(string.Empty, LocalSettings.LoadMenuLauncher());

            LocalSettings.StoreSystemHotkey("Ctrl+Alt+Home");
            LocalSettings.StoreDockPage(DockPage.Menu);
            LocalSettings.StoreMenuLauncher("F9");

            Assert.Equal("Ctrl+Alt+Home", LocalSettings.LoadSystemHotkey());
            Assert.Equal(DockPage.Menu, LocalSettings.LoadDockPage());
            Assert.Equal("F9", LocalSettings.LoadMenuLauncher());
        });
    }

    [Fact]
    public void An_old_theme_choice_migrates_and_the_accent_round_trips()
    {
        InOwnFile(path =>
        {
            LocalSettings.StoreLightTheme(true);
            Assert.Equal(ThemeMode.Light, LocalSettings.LoadTheme().Mode);

            // The file a custom theme left behind before this version: twenty-two colours, one per
            // interface colour. Only the accent still means anything, so only it comes across.
            File.WriteAllText(path, """
                {"Theme":"custom","CustomThemeLight":true,"CustomThemeColors":{"AppAccent":"#112233","AppText":"#00FF00"}}
                """);

            var migrated = LocalSettings.LoadTheme();
            Assert.Equal(ThemeMode.Custom, migrated.Mode);
            Assert.True(migrated.BaseLight);
            Assert.Equal("#112233", migrated.AccentHex);

            // Saving writes the new shape, and the dictionary that was read is not carried along.
            LocalSettings.StoreTheme(migrated);
            var written = File.ReadAllText(path);
            Assert.DoesNotContain("CustomThemeColors", written, StringComparison.Ordinal);
            Assert.Contains("\"Accent\":\"#112233\"", written, StringComparison.Ordinal);
            Assert.Equal("#112233", LocalSettings.LoadTheme().AccentHex);

            // A built-in variant has no accent of its own; the stored one is kept for the next time
            // custom is picked rather than thrown away.
            LocalSettings.StoreTheme(new ThemeSelection(ThemeMode.Dark, false, "#445566"));
            Assert.Equal(ThemeMode.Dark, LocalSettings.LoadTheme().Mode);
            Assert.Equal("#445566", LocalSettings.LoadTheme().AccentHex);
        });
    }

    [Fact]
    public void The_main_window_size_is_remembered_only_when_it_is_usable()
    {
        InOwnFile(path =>
        {
            Assert.Null(LocalSettings.LoadMainWindowSize());

            LocalSettings.StoreMainWindowSize(620, 740);
            Assert.Equal((620d, 740d), LocalSettings.LoadMainWindowSize());

            LocalSettings.StoreMainWindowSize(0, -1);
            Assert.Equal((620d, 740d), LocalSettings.LoadMainWindowSize());

            File.WriteAllText(path, """{"MainWindowWidth":-4,"MainWindowHeight":0}""");
            Assert.Null(LocalSettings.LoadMainWindowSize());
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
