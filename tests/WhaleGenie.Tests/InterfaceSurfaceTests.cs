using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
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
            Assert.Equal(3, surface.GetVisualDescendants().OfType<Button>()
                .Count(button => button.Content is TextBlock));
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
            var reported = new TaskCompletionSource();
            surface.MoveCompleted += _ =>
            {
                completed++;
                reported.TrySetResult();
            };
            surface.BeginDragForCheck();
            surface.Position = new PixelPoint(40, 50);
            surface.Position = new PixelPoint(45, 55);

            // Waited for rather than slept through: the save lands on a quiet-period timer, and a
            // fixed sleep is the one thing a loaded machine stretches past that timer.
            await reported.Task.WaitAsync(TimeSpan.FromSeconds(5));

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

    /// <summary>
    /// The handle offers two shapes beside the drag grip: the buttons can be folded away, and the
    /// column can be turned into a row. The panel is not a topmost window — it belongs to the
    /// target window's layer, so the program in front covers it instead of being pierced by it.
    /// </summary>
    [Fact]
    public void A_panel_can_be_collapsed_and_turned_into_a_row()
    {
        Ui.Run(() =>
        {
            var macros = new[]
            {
                new MacroItem { Name = "one" },
                new MacroItem { Name = "two" },
            };
            var surface = new FloatingSurfaceWindow(macros, _ => { });
            surface.Show();

            Assert.False(surface.Topmost);
            Assert.False(surface.IsCollapsedForCheck);
            Assert.False(surface.IsHorizontalForCheck);
            Assert.True(surface.BodyVisibleForCheck);
            Assert.Equal(Orientation.Vertical, surface.ButtonOrientationForCheck);

            surface.ToggleLayoutForCheck();
            Assert.True(surface.IsHorizontalForCheck);
            Assert.Equal(Orientation.Horizontal, surface.ButtonOrientationForCheck);
            Assert.True(surface.Width > 86);

            surface.ToggleCollapsedForCheck();
            Assert.True(surface.IsCollapsedForCheck);
            Assert.False(surface.BodyVisibleForCheck);
            Assert.Equal(86, surface.Width);

            // The folded window is taller than the handle because Windows will not make it
            // shorter, so the handle sits on the top edge: the region keeps that strip, and a
            // centred handle would have its buttons cut in half there.
            Assert.Equal(20, surface.HeaderHeightForCheck);
            Assert.Equal(VerticalAlignment.Top, surface.HeaderAlignmentForCheck);

            surface.ToggleCollapsedForCheck();
            Assert.True(surface.BodyVisibleForCheck);
            Assert.True(surface.Width > 86);
            surface.Close();
        });
    }

    /// <summary>The collapsed window is taller than its native region; the header must stay in the visible top.</summary>
    [Fact]
    public void A_collapsed_panel_keeps_its_header_in_the_clipped_top()
    {
        Ui.Run(() =>
        {
            var surface = new FloatingSurfaceWindow([new MacroItem { Name = "one" }], _ => { });
            surface.Show();
            surface.ToggleCollapsedForCheck();
            Dispatcher.UIThread.RunJobs();

            var header = surface.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "SurfaceHeader");
            Assert.Equal(VerticalAlignment.Top, header.VerticalAlignment);
            Assert.Equal(20, header.Height);
            Assert.InRange(header.Bounds.Y, 0, 0.5);

            var collapse = surface.GetVisualDescendants().OfType<Button>()
                .Single(button => button.Name == "SurfaceCollapseButton");
            Assert.InRange(collapse.Bounds.Bottom, 0, header.Bounds.Height);

            surface.Close();
        });
    }

    /// <summary>A long macro name must be ellipsized inside the pill, not clipped by its right edge.</summary>
    [Fact]
    public void A_long_macro_label_trims_inside_its_button()
    {
        Ui.Run(() =>
        {
            const string name = "全局面板设置很多字";
            var surface = new FloatingSurfaceWindow([new MacroItem { Name = name }], _ => { });
            surface.Show();
            Dispatcher.UIThread.RunJobs();

            var label = surface.GetVisualDescendants().OfType<TextBlock>()
                .Single(text => text.Text == name);
            var button = surface.GetVisualDescendants().OfType<Button>()
                .Single(button => ReferenceEquals(button.Content, label));

            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            Assert.True(label.MaxWidth <= button.Bounds.Width - 12,
                $"label max {label.MaxWidth} escapes button width {button.Bounds.Width}");
            Assert.True(label.Bounds.Width <= label.MaxWidth + 0.5,
                $"label width {label.Bounds.Width} escapes max {label.MaxWidth}");

            surface.Close();
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
                MacroName = macro.Name == "Macro2" ? "  Macro2  " : macro.Name,
                Spec = new InterfaceSpec(rule),
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
                MacroName = "Macro2",
                Spec = new InterfaceSpec(new WindowRule(Title: "target")),
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
            var two = new MacroItem { Name = "two" };
            var triggers = new ProjectTriggers();
            triggers.Entries.Add(new TriggerEntry
            {
                MacroName = one.Name,
                Spec = new InterfaceSpec(rule),
            });
            triggers.Entries.Add(new TriggerEntry
            {
                MacroName = two.Name,
                Spec = new InterfaceSpec(rule),
            });
            triggers.StoreInterfaceHotkey(rule, "Ctrl+Alt+U");

            var editor = new MacroEditorViewModel();
            editor.LoadFrom(one, triggers.ForMacro(one.Name));
            editor.LoadProjectTriggerSettings(triggers, [one, two]);
            editor.SelectedTrigger = editor.TriggerEntries
                .Single(entry => entry.Kind == TriggerKind.Interface);

            Assert.Equal("Ctrl+Alt+U", editor.InterfaceToggleHotkey);
            Assert.Contains("one", editor.InterfaceGroupMembers);
            Assert.Contains("two", editor.InterfaceGroupMembers);

            editor.SetInterfaceToggleHotkey("Ctrl+Alt+J");
            MacroDraft? saved = null;
            editor.CloseRequested += draft => saved = draft;
            editor.SaveCommand.Execute(null);

            Assert.NotNull(saved);
            Assert.Contains(saved!.Layouts,
                layout => layout.GroupKey == ProjectTriggers.GroupKey(rule)
                    && layout.ToggleHotkey == "Ctrl+Alt+J");
        });
    }

    [Fact]
    public void An_interface_group_hotkey_is_checked_against_menu_launchers()
    {
        var folder = Path.Combine(Path.GetTempPath(), "whalegenie-settings-" + Guid.NewGuid().ToString("N"));
        var stored = LocalSettings.FilePath;
        try
        {
            Directory.CreateDirectory(folder);
            LocalSettings.FilePath = Path.Combine(folder, "settings.json");
            LocalSettings.StoreMenuLauncher("Ctrl+Alt+U");

            Ui.Run(() =>
            {
                var rule = new WindowRule(Title: "target");
                var macro = new MacroItem { Name = "one" };
                var triggers = new ProjectTriggers();
                triggers.Entries.Add(new TriggerEntry
                {
                    MacroName = macro.Name,
                    Spec = new InterfaceSpec(rule),
                });
                triggers.StoreInterfaceHotkey(rule, "Ctrl+Alt+U");

                var editor = new MacroEditorViewModel
                {
                    Settings = new WhaleGenie.Editing.LocalEditorSettings(),
                };
                editor.LoadFrom(macro, triggers.ForMacro(macro.Name));
                editor.LoadProjectTriggerSettings(triggers, [macro]);
                editor.SelectedTrigger = editor.TriggerEntries
                    .Single(entry => entry.Kind == TriggerKind.Interface);
                Assert.True(editor.HasTriggerProblem);
                Assert.Contains("Ctrl+Alt+U", editor.TriggerProblemSummary);
            });
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
            var triggers = new ProjectTriggers();
            triggers.Entries.Add(new TriggerEntry
            {
                MacroName = macro.Name,
                Spec = new InterfaceSpec(rule),
            });
            var editor = new MacroEditorViewModel();
            editor.LoadFrom(macro, triggers.ForMacro(macro.Name));
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
            MacroName = projectMacro.Name,
            Spec = new InterfaceSpec(interfaceRule),
        });
        project.Triggers.StoreInterfaceHotkey(interfaceRule, "NumPadSubtract");

        var match = GlobalHotkeyPlan.From(project.Workspace, project.SystemSwitch,
            LocalSettings.LoadSystemHotkey(), "").Match("NumPadSubtract", []);
        Assert.Equal(GlobalHotkeyAction.ToggleInterfaceGroup, match.Action);
    }
}
