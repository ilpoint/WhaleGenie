using Avalonia.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class SettingsMenuTests
{
    [Fact]
    public void The_menu_launcher_accepts_one_keyboard_or_side_button_binding()
    {
        var project = new MainViewModel();
        var viewModel = new SettingsViewModel(() => DriverInputState.Ready,
            project: project);

        Assert.True(viewModel.CaptureMenuLauncher("F11"));
        Assert.Equal("F11", project.Triggers.MenuLauncher);
        Assert.True(viewModel.CaptureMenuLauncher("Mouse 4"));
        Assert.Equal("Mouse 4", project.Triggers.MenuLauncher);
        Assert.False(viewModel.CaptureMenuLauncher("Wheel Down"));
        Assert.Equal("Mouse 4", project.Triggers.MenuLauncher);
        Assert.False(viewModel.CaptureMenuLauncher("Mouse Middle"));
        Assert.Equal("Mouse 4", project.Triggers.MenuLauncher);
    }

    [Fact]
    public void Window_rules_keep_multiple_pages_under_one_rule()
    {
        var project = new MainViewModel();
        var viewModel = new SettingsViewModel(() => DriverInputState.Ready,
            project: project);

        viewModel.AddMenuRule(new WindowRule(Title: "Notepad"));
        var row = viewModel.MenuRules.Single(candidate => !candidate.IsDefault);
        viewModel.AddMenuPage(row);

        Assert.Equal(2, viewModel.MenuRules.Count);
        Assert.True(viewModel.MenuRules[0].IsDefault);
        Assert.Equal(2, viewModel.MenuRules[1].PageCount);
        Assert.Empty(viewModel.MenuRuleProblem);
    }

    [Fact]
    public void The_launcher_status_explains_system_empty_and_ready_states()
    {
        var project = new MainViewModel();
        var viewModel = new SettingsViewModel(() => DriverInputState.Ready,
            project: project);
        viewModel.CaptureMenuLauncher("F11");

        Assert.Equal(Strings.Get("Settings.MenuSystemOff"), viewModel.MenuLauncherStatus);

        project.IsRunning = true;
        Assert.Equal(Strings.Get("Settings.MenuEmpty"), viewModel.MenuLauncherStatus);

        var macro = new MacroItem { Name = "run" };
        project.Macros.Add(macro);
        project.Triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Menu,
            MacroName = macro.Name,
            MenuPageId = project.Triggers.CommonMenuPages()[0].Id,
            MenuRow = 0,
            MenuColumn = 0,
        });
        Assert.Equal(Strings.Get("Settings.MenuReady"), viewModel.MenuLauncherStatus);
    }

    [Fact]
    public void Dragging_a_rule_down_moves_its_page_block_after_the_target()
    {
        var project = new MainViewModel();
        var viewModel = new SettingsViewModel(() => DriverInputState.Ready,
            project: project);
        viewModel.AddMenuRule(new WindowRule(Title: "Notepad"));
        viewModel.AddMenuRule(new WindowRule(Title: "Browser"));
        viewModel.AddMenuRule(new WindowRule(Title: "Office"));

        var notepad = viewModel.MenuRules.Single(row => row.Summary.Contains("Notepad"));
        var browser = viewModel.MenuRules.Single(row => row.Summary.Contains("Browser"));
        viewModel.MoveMenuRuleTo(notepad, browser);

        Assert.Equal(["Browser", "Notepad", "Office"],
            viewModel.MenuRules.Where(row => !row.IsDefault)
                .Select(row => row.Rule.MatchValue));
    }

    [Fact]
    public void Settings_opens_on_the_requested_trigger_page()
    {
        Ui.Run(() =>
        {
            var project = new MainViewModel();
            var window = new SettingsWindow(project, null,
                SettingsRootPage.Triggers, SettingsSubPage.MenuLauncher);
            window.Show();

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            Assert.True(viewModel.IsTriggersRoot);
            Assert.True(viewModel.IsMenuLauncherPage);
            Assert.NotNull(window.FindControl<Button>("CaptureMenuLauncherButton"));
            Assert.NotNull(window.FindControl<ListBox>("MenuRuleList"));
            window.Close();
        });
    }
}
