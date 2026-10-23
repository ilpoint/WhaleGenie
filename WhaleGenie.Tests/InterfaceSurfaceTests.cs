using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

public class InterfaceSurfaceTests
{
    [Fact]
    public void One_surface_lists_every_macro_in_its_group()
    {
        Ui.Run(() =>
        {
            var macros = new[]
            {
                new MacroItem { Name = "one" },
                new MacroItem { Name = "two" },
                new MacroItem { Name = "three" },
            };
            var surface = new FloatingSurfaceWindow(macros, _ => { });
            surface.Show();

            Assert.Equal(["one", "two", "three"], surface.MacroNames);
            Assert.Equal(3, surface.GetVisualDescendants().OfType<Button>().Count());
            surface.Close();
        });
    }

    [Fact]
    public void Moving_a_surface_is_saved_once_after_the_drag_stops()
    {
        Ui.RunAsync(async () =>
        {
            var surface = new FloatingSurfaceWindow([new MacroItem { Name = "one" }], _ => { });
            surface.Show();
            var windows = new WindowsForChecks().Add(1, "target", "app", "Class");
            surface.PlaceAt(windows.Open[0], windows,
                new InterfaceLayout(new WindowRule(Title: "target"), 10, 10));

            var completed = 0;
            surface.MoveCompleted += _ => completed++;
            surface.BeginDragForCheck();
            surface.Position = new PixelPoint(40, 50);
            surface.Position = new PixelPoint(45, 55);
            await Task.Delay(300);

            Assert.Equal(1, completed);
            Assert.False(surface.IsMoving);
            surface.Close();
            return true;
        });
    }

    [Fact]
    public void Programmatic_placement_never_starts_a_move_or_writes_layout()
    {
        Ui.RunAsync(async () =>
        {
            var surface = new FloatingSurfaceWindow([new MacroItem { Name = "one" }], _ => { });
            surface.Show();
            var completed = 0;
            surface.MoveCompleted += _ => completed++;

            surface.Position = new PixelPoint(80, 90);
            await Task.Delay(250);

            Assert.Equal(0, completed);
            Assert.False(surface.IsMoving);
            surface.Close();
            return true;
        });
    }

    [Fact]
    public void More_than_six_macros_use_the_narrow_vertical_scroll_area()
    {
        Ui.Run(() =>
        {
            var macros = Enumerable.Range(0, 8)
                .Select(index => new MacroItem { Name = "macro-" + index })
                .ToList();
            var surface = new FloatingSurfaceWindow(macros, _ => { });
            surface.Show();

            Assert.Equal(86, surface.Width);
            var scroll = Assert.Single(surface.GetVisualDescendants().OfType<ScrollViewer>());
            Assert.Equal(154, scroll.MaxHeight);
            var buttons = surface.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Content is TextBlock)
                .ToList();
            Assert.Equal(8, buttons.Count);
            Assert.All(buttons, button => Assert.Equal(80, button.Width));
            surface.Close();
        });
    }

    [Fact]
    public void Eight_macros_in_one_rule_all_resolve_including_macro_two()
    {
        var rule = new WindowRule(Title: "target");
        var macros = Enumerable.Range(1, 8)
            .Select(index => new MacroItem { Name = $"Macro{index}" })
            .ToList();
        var triggers = new ProjectTriggers();
        foreach (var macro in macros)
        {
            triggers.Entries.Add(new TriggerEntry
            {
                Kind = TriggerKind.Interface,
                MacroName = macro.Name == "Macro2" ? "  Macro2  " : macro.Name,
                WindowTarget = rule,
            });
        }

        var group = Assert.Single(triggers.InterfaceGroups());
        var members = InterfaceMemberResolver.Resolve(group, macros);
        Assert.Equal(8, members.Count);
        Assert.Equal("Macro2", members[1].Name);
        Assert.Equal(InterfaceMemberState.Visible, members[1].State);
    }

    [Fact]
    public void A_disabled_interface_member_is_explained_and_hidden()
    {
        var macro = new MacroItem { Name = "Macro2", IsEnabled = false };
        var group = new InterfaceGroupView("group", new WindowRule(Title: "target"),
        [
            new TriggerEntry
            {
                Kind = TriggerKind.Interface,
                MacroName = "Macro2",
                WindowTarget = new WindowRule(Title: "target"),
            },
        ]);

        var member = Assert.Single(InterfaceMemberResolver.Resolve(group, [macro]));

        Assert.Equal(InterfaceMemberState.Disabled, member.State);
        Assert.False(member.IsVisibleOnSurface);
        Assert.Contains(Strings.Get("Interface.Member.Disabled"), member.Display);
    }

    [Fact]
    public void An_interface_group_shows_its_members_and_saves_its_own_hotkey()
    {
        Ui.Run(() =>
        {
            var rule = new WindowRule(Title: "target");
            var one = new MacroItem { Name = "one" };
            one.Triggers.Add(new TriggerEntry
            {
                Kind = TriggerKind.Interface,
                MacroName = one.Name,
                WindowTarget = rule,
            });
            var two = new MacroItem { Name = "two" };
            two.Triggers.Add(new TriggerEntry
            {
                Kind = TriggerKind.Interface,
                MacroName = two.Name,
                WindowTarget = rule,
            });
            var triggers = ProjectTriggers.FromMacros([one, two]);
            triggers.StoreInterfaceHotkey(rule, "Ctrl+Alt+U");

            var editor = new MacroEditorViewModel();
            editor.LoadFrom(one);
            editor.LoadProjectTriggerSettings(triggers, [one, two]);
            editor.SelectedTrigger = editor.TriggerEntries
                .Single(entry => entry.Kind == TriggerKind.Interface);

            Assert.Equal("Ctrl+Alt+U", editor.InterfaceToggleHotkey);
            Assert.Contains("one", editor.InterfaceGroupMembers);
            Assert.Contains("two", editor.InterfaceGroupMembers);

            editor.SetInterfaceToggleHotkey("Ctrl+Alt+J");
            MacroItem? saved = null;
            editor.CloseRequested += macro => saved = macro;
            editor.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Contains(saved!.InterfaceLayouts,
                layout => layout.GroupKey == ProjectTriggers.GroupKey(rule)
                    && layout.ToggleHotkey == "Ctrl+Alt+J");
        });
    }

    [Fact]
    public void An_interface_group_hotkey_is_checked_against_menu_launchers()
    {
        Ui.Run(() =>
        {
            var rule = new WindowRule(Title: "target");
            var macro = new MacroItem { Name = "one" };
            macro.Triggers.Add(new TriggerEntry
            {
                Kind = TriggerKind.Interface,
                MacroName = macro.Name,
                WindowTarget = rule,
            });
            var triggers = ProjectTriggers.FromMacros([macro]);
            triggers.StoreInterfaceHotkey(rule, "Ctrl+Alt+U");
            triggers.MenuLauncher = "Ctrl+Alt+U";

            var editor = new MacroEditorViewModel();
            editor.LoadFrom(macro);
            editor.LoadProjectTriggerSettings(triggers, [macro]);
            editor.SelectedTrigger = editor.TriggerEntries
                .Single(entry => entry.Kind == TriggerKind.Interface);
            Assert.True(editor.HasTriggerProblem);
            Assert.Contains("Ctrl+Alt+U", editor.TriggerProblemSummary);
        });
    }

    [Fact]
    public void Numeric_keypad_subtract_is_captured_with_the_runtime_name()
    {
        Assert.Equal("NumPadSubtract",
            WhaleGenie.Views.HotkeyNameMapper.Name(Avalonia.Input.Key.Subtract));
        Assert.True(HotkeyBinding.IsSupported("NumPadSubtract"));
        Assert.False(HotkeyBinding.IsSupported("ImeProcessed"));

        Ui.Run(() =>
        {
            var rule = new WindowRule(Title: "target");
            var macro = new MacroItem { Name = "one" };
            macro.Triggers.Add(new TriggerEntry
            {
                Kind = TriggerKind.Interface,
                MacroName = macro.Name,
                WindowTarget = rule,
            });
            var triggers = ProjectTriggers.FromMacros([macro]);
            var editor = new MacroEditorViewModel();
            editor.LoadFrom(macro);
            editor.LoadProjectTriggerSettings(triggers, [macro]);
            editor.SelectedTrigger = editor.TriggerEntries
                .Single(entry => entry.Kind == TriggerKind.Interface);

            Assert.True(editor.SetInterfaceToggleHotkey("NumPadSubtract"));
            Assert.Empty(editor.InterfaceHotkeyProblem);
            Assert.False(editor.SetInterfaceToggleHotkey("ImeProcessed"));
            Assert.NotEmpty(editor.InterfaceHotkeyProblem);
            Assert.Equal("NumPadSubtract", editor.InterfaceToggleHotkey);
        });

        var project = new MainViewModel { IsRunning = true };
        var interfaceRule = new WindowRule(Title: "target");
        var projectMacro = new MacroItem { Name = "one" };
        project.AddMacro(projectMacro);
        project.Triggers.Entries.Add(new TriggerEntry
        {
            Kind = TriggerKind.Interface,
            MacroName = projectMacro.Name,
            WindowTarget = interfaceRule,
        });
        project.Triggers.StoreInterfaceHotkey(interfaceRule, "NumPadSubtract");

        var match = GlobalHotkeyPlan.From(project).Match("NumPadSubtract", []);
        Assert.Equal(GlobalHotkeyAction.ToggleInterfaceGroup, match.Action);
    }
}
