using System;
using System.IO;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Tests;

public class RuntimeHookSafetyTests
{
    [Fact]
    public async Task Global_shortcuts_are_matched_without_the_ui_thread()
    {
        var plan = new GlobalHotkeyPlan("Home", true, ["F6"],
            [new InterfaceHotkeyGroup("group", "Ctrl+Alt+H")]);

        var result = await Task.Run(() => (
            plan.Match("Home", []),
            plan.Match("H", ["左Ctrl", "左Alt"]),
            plan.Match("F6", []),
            plan.Match("NumPadSubtract", [])));

        Assert.Equal(GlobalHotkeyAction.ToggleSystem, result.Item1.Action);
        Assert.Equal(GlobalHotkeyAction.ToggleInterfaceGroup, result.Item2.Action);
        Assert.Equal("group", result.Item2.GroupKey);
        Assert.Equal(GlobalHotkeyAction.None, result.Item3.Action);

        var off = new GlobalHotkeyPlan("Home", false, [],
            [new InterfaceHotkeyGroup("group", "Ctrl+Alt+H")]);
        Assert.Equal(GlobalHotkeyAction.None, off.Match("H", ["左Ctrl", "左Alt"]).Action);
        Assert.Equal(GlobalHotkeyAction.ToggleSystem, off.Match("Home", []).Action);

        var subtract = new GlobalHotkeyPlan("Home", true, [],
            [new InterfaceHotkeyGroup("subtract", "NumPadSubtract")]);
        Assert.Equal(GlobalHotkeyAction.ToggleInterfaceGroup,
            subtract.Match("NumPadSubtract", []).Action);
    }

    [Fact]
    public void A_project_interface_hotkey_reaches_its_toggle_action()
    {
        var viewModel = new MainViewModel { IsRunning = true };
        var rule = new WindowRule(Title: "target");
        viewModel.Triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Interface,
            MacroName = "macro",
            WindowTarget = rule,
        });
        viewModel.Triggers.StoreInterfaceHotkey(rule, "Ctrl+Alt+H");

        var plan = GlobalHotkeyPlan.From(viewModel);
        var match = plan.Match("H", ["左Ctrl", "左Alt"]);

        Assert.Equal(GlobalHotkeyAction.ToggleInterfaceGroup, match.Action);
        Assert.Equal(ProjectTriggers.GroupKey(rule), match.GroupKey);
    }

    /// <summary>
    /// 宏自己带的启用/停用键走同一份计划；宏停着的时候也要认，否则这个键只能停不能开。
    /// </summary>
    [Fact]
    public void A_macro_switch_key_toggles_its_macro_even_while_it_is_disarmed()
    {
        var viewModel = new MainViewModel { IsRunning = true };
        viewModel.AddMacro(new MacroItem { Name = "探针", IsEnabled = false, ToggleKey = "F9" });

        var match = GlobalHotkeyPlan.From(viewModel).Match("F9", []);

        Assert.Equal(GlobalHotkeyAction.ToggleMacro, match.Action);
        Assert.Equal("探针", match.MacroName);
    }

    /// <summary>一个键已经被按键触发占着时，它不会被开关键抢走：跑宏优先。</summary>
    [Fact]
    public void A_trigger_key_still_wins_over_a_macro_switch_key()
    {
        var viewModel = new MainViewModel { IsRunning = true };
        viewModel.AddMacro(new MacroItem { Name = "探针", ToggleKey = "F9" });
        viewModel.Triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Key,
            MacroName = "探针",
            BindKey = "F9",
        });

        Assert.Equal(GlobalHotkeyAction.None,
            GlobalHotkeyPlan.From(viewModel).Match("F9", []).Action);
    }

    /// <summary>组合键写法与触发键一致，退出自动化总开关之后也不再认。</summary>
    [Fact]
    public void A_macro_switch_key_is_matched_like_a_trigger_binding()
    {
        var plan = new GlobalHotkeyPlan("Home", true, ["F6"], [])
        {
            MacroToggles = [new MacroToggleHotkey("探针", "Ctrl+Alt+P")],
        };

        var match = plan.Match("P", ["左Ctrl", "左Alt"]);

        Assert.Equal(GlobalHotkeyAction.ToggleMacro, match.Action);
        Assert.Equal("探针", match.MacroName);

        var off = plan with { AutomationEnabled = false };
        Assert.Equal(GlobalHotkeyAction.None, off.Match("P", ["左Ctrl", "左Alt"]).Action);
    }

    [Fact]
    public void A_project_hotkey_only_claims_the_system_switch_while_enabled()
    {
        var viewModel = new MainViewModel();
        viewModel.Triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Key,
            MacroName = "macro",
            BindKey = "Home",
        });

        Assert.Equal(GlobalHotkeyAction.ToggleSystem,
            GlobalHotkeyPlan.From(viewModel).Match("Home", []).Action);

        viewModel.IsRunning = true;
        Assert.Equal(GlobalHotkeyAction.None,
            GlobalHotkeyPlan.From(viewModel).Match("Home", []).Action);
    }

    [Fact]
    public void A_runtime_snapshot_does_not_change_when_the_project_does()
    {
        var triggers = new ProjectTriggers();
        triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Hotstring,
            MacroName = "macro",
            Abbreviation = "one",
        });
        var macro = new MacroItem { Name = "macro" };
        var store = new TriggerRuntimeSnapshotStore();

        store.Publish(triggers, [macro]);
        triggers.Entries[0].Abbreviation = "two";

        Assert.Equal("one", Assert.Single(store.Current.Entries).Abbreviation);
    }

    [Fact]
    public void A_saved_menu_hotkey_requests_the_menu()
    {
        InOwnSettings("F9", () =>
        {
            var macros = new[] { new MacroItem { Name = "menu" } };
            var triggers = new ProjectTriggers();
            triggers.Entries.Add(new TriggerEntry
            {
                Kind = TriggerKind.Menu,
                MacroName = "menu",
                MenuPageId = triggers.CommonMenuPages()[0].Id,
                MenuRow = 0,
                MenuColumn = 0,
            });
            var store = new TriggerRuntimeSnapshotStore();
            store.Publish(triggers, macros);
            using var service = new MacroTriggerService(() => store.Current,
                NullDeviceLayer.Instance, new VariableSession(), new NoWindowEvents());
            var requested = false;
            service.MenuRequested += () => requested = true;

            Assert.True(service.RequestMenuForCheck("F9"));
            Assert.True(requested);
        });
    }

    [Fact]
    public void The_middle_mouse_button_requests_the_menu()
    {
        InOwnSettings("Mouse Middle", () =>
        {
            var triggers = new ProjectTriggers();
            var store = new TriggerRuntimeSnapshotStore();
            store.Publish(triggers, []);
            using var service = new MacroTriggerService(() => store.Current,
                NullDeviceLayer.Instance, new VariableSession(), new NoWindowEvents());
            var requested = false;
            service.MenuRequested += () => requested = true;

            Assert.True(service.RequestMenuForCheck("Mouse Middle"));
            Assert.True(requested);
        });
    }

    private static void InOwnSettings(string launcher, Action check)
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-settings-" + Guid.NewGuid().ToString("N"));
        var stored = LocalSettings.FilePath;
        try
        {
            Directory.CreateDirectory(folder);
            LocalSettings.FilePath = Path.Combine(folder, "settings.json");
            LocalSettings.StoreMenuLauncher(launcher);
            check();
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

    [Theory]
    [InlineData(HookMouseButton.Left, true)]
    [InlineData(HookMouseButton.Right, true)]
    [InlineData(HookMouseButton.Middle, false)]
    [InlineData(HookMouseButton.Back, false)]
    [InlineData(HookMouseButton.Forward, false)]
    public void Only_left_and_right_mouse_buttons_reset_hotstring_recognition(
        HookMouseButton button, bool expected)
        => Assert.Equal(expected, MacroTriggerService.MouseButtonResetsHotstring(button));

    [Fact]
    public void An_exception_inside_a_hook_subscriber_is_contained_and_logged()
    {
        var folder = Path.Combine(Path.GetTempPath(), "wg-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var previous = AppDiagnostics.FilePathOverride;
        try
        {
            AppDiagnostics.FilePathOverride = Path.Combine(folder, "runtime.log");
            var error = Record.Exception(() => MacroTriggerService.RunGuarded(
                "hook.test", () => throw new InvalidOperationException("boom")));

            Assert.Null(error);
            Assert.Contains("hook.test", File.ReadAllText(AppDiagnostics.FilePath));
            Assert.Contains("boom", File.ReadAllText(AppDiagnostics.FilePath));
        }
        finally
        {
            AppDiagnostics.FilePathOverride = previous;
            Directory.Delete(folder, true);
        }
    }

}
