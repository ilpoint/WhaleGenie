using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Tests;

/// <summary>
/// The two promises of this first wheel step: a middle press still opens the macro menu exactly as
/// before, and the wheel only appears after a held right button has actually been moved.
/// </summary>
public class WheelMenuTests
{
    private static readonly WheelSlotRef Top = new(WheelRing.Inner, 0);
    private static readonly WheelSlotRef Right = new(WheelRing.Inner, 2);

    [Fact]
    public void A_middle_press_still_requests_the_macro_menu_with_a_wheel_configured()
    {
        InOwnFiles(() =>
        {
            LocalSettings.StoreMenuLauncher(WheelOptions.MiddleButton);
            var state = new WheelMenuState();
            state.Options.Launcher = WheelOptions.RightButton;
            WheelMenuStore.Save(state);

            var snapshots = new TriggerRuntimeSnapshotStore();
            snapshots.Publish(new ProjectTriggers(), []);
            using var service = new MacroTriggerService(
                () => snapshots.Current, NullDeviceLayer.Instance, new NoWindowEvents());
            var requested = 0;
            service.MenuRequested += () => requested++;

            Assert.True(service.RequestMenuForCheck(WheelOptions.MiddleButton));
            Assert.Equal(1, requested);
            Assert.False(service.RequestMenuForCheck(WheelOptions.RightButton));
            Assert.Equal(1, requested);
        });
    }

    [Fact]
    public void A_short_right_press_is_left_alone_and_a_swipe_opens_and_selects_the_wheel()
    {
        var gesture = Gesture();

        // A press must not claim or suppress anything: it is still an ordinary right click.
        Assert.Equal(WheelGestureAction.None,
            gesture.Down(WheelOptions.RightButton, 100, 100).Action);
        Assert.True(gesture.IsPending);
        Assert.Equal(WheelGestureAction.None,
            gesture.Up(WheelOptions.RightButton, 101, 101).Action);
        Assert.False(gesture.IsPending);

        gesture.Down(WheelOptions.RightButton, 100, 100);
        Assert.Equal(WheelGestureAction.None, gesture.Move(105, 100).Action);
        var opened = gesture.Move(200, 100);
        Assert.Equal(WheelGestureAction.OpenWheel, opened.Action);
        Assert.Equal(100, opened.X);
        Assert.True(gesture.IsOpen);

        gesture.SetCenter(100, 100, WheelGeometry.For(420, false));
        var selected = gesture.Up(WheelOptions.RightButton, 260, 100);
        Assert.Equal(WheelGestureAction.Select, selected.Action);
        Assert.Equal(new WheelSlotRef(WheelRing.Outer, 2), selected.Slot);
        Assert.False(gesture.IsOpen);
    }

    [Fact]
    public async Task The_whole_gesture_runs_on_the_hook_thread_without_touching_the_ui()
    {
        // SharpHook calls back on a background thread. Anything the enabled delegate reads must be
        // plain state: reading Avalonia's DataContext there throws InvalidOperationException, was
        // swallowed by the handler and left the wheel permanently idle on every right press.
        var running = new RunningFlag();
        var gesture = new WheelGesture(
            () => WheelOptions.RightButton,
            () => WheelOptions.UsualRepeatKey,
            () => running.Enabled);

        await Task.Run(() =>
        {
            gesture.Down(WheelOptions.RightButton, 100, 100);
            Assert.Equal(WheelGestureAction.OpenWheel, gesture.Move(300, 100).Action);
            gesture.SetCenter(100, 100, WheelGeometry.For(420, false));
            Assert.Equal(WheelGestureAction.Select,
                gesture.Up(WheelOptions.RightButton, 260, 100).Action);
        });

        running.Disable();
        gesture.Down(WheelOptions.RightButton, 100, 100);
        Assert.False(gesture.IsPending);
    }

    private sealed class RunningFlag
    {
        private int _value = 1;

        public bool Enabled => Volatile.Read(ref _value) == 1;

        public void Disable() => Volatile.Write(ref _value, 0);
    }

    [Fact]
    public void The_middle_button_never_starts_a_wheel_gesture()
    {
        var gesture = Gesture();
        Assert.Equal(WheelGestureAction.None,
            gesture.Down(WheelOptions.MiddleButton, 100, 100).Action);
        Assert.Equal(WheelGestureAction.None, gesture.Move(300, 100).Action);
        Assert.Equal(WheelGestureAction.None,
            gesture.Up(WheelOptions.MiddleButton, 300, 100).Action);
    }

    [Fact]
    public void Releasing_in_the_centre_or_pressing_escape_cancels_an_open_wheel()
    {
        var gesture = Gesture();
        gesture.Down(WheelOptions.RightButton, 100, 100);
        gesture.Move(300, 100);
        gesture.SetCenter(100, 100, WheelGeometry.For(420, false));
        Assert.Equal(WheelGestureAction.Cancel,
            gesture.Up(WheelOptions.RightButton, 100, 100).Action);

        gesture.Down(WheelOptions.RightButton, 100, 100);
        gesture.Move(300, 100);
        gesture.Hover(Right);
        Assert.Equal(WheelGestureAction.Repeat, gesture.Key("F1").Action);
        Assert.Equal(Right, gesture.Key("F1").Slot);
        Assert.Equal(WheelGestureAction.Cancel, gesture.Key("Esc").Action);
        Assert.False(gesture.IsOpen);
    }

    [Fact]
    public void Directions_and_rings_always_name_the_same_position()
    {
        var metrics = WheelGeometry.For(420, false);
        Assert.Equal(Top, WheelGeometry.At(0, -100, metrics));
        Assert.Equal(Right, WheelGeometry.At(100, 0, metrics));
        Assert.Equal(new WheelSlotRef(WheelRing.Inner, 4), WheelGeometry.At(0, 100, metrics));
        Assert.Null(WheelGeometry.At(0, 0, metrics));
        Assert.Equal(new WheelSlotRef(WheelRing.Outer, 0),
            WheelGeometry.At(0, -160, metrics));
        Assert.Equal(new WheelSlotRef(WheelRing.Extended, 2),
            WheelGeometry.At(400, 0, metrics));
        Assert.Equal(16, WheelGeometry.For(420, true).OuterCount);
    }

    [Fact]
    public void A_program_scene_overrides_only_the_positions_it_fills_in()
    {
        var rules = new WheelMenuRules();
        rules.Global.At(Top).Macro = "copy";
        rules.Global.At(new WheelSlotRef(WheelRing.Inner, 1)).Macro = "paste";
        var scene = rules.AddScene(new WindowRule(Process: "notepad",
            How: WindowHow.ProcessEquals));
        scene.At(Top).Macro = "cut";
        var macros = new[]
        {
            new MacroItem { Name = "copy", IsEnabled = true },
            new MacroItem { Name = "paste", IsEnabled = true },
            new MacroItem { Name = "cut", IsEnabled = true },
        };

        var merged = rules.Resolve(scene, WheelSource.Merged, false, macros);
        Assert.Equal("cut", merged.At(Top)?.Macro?.Name);
        Assert.True(merged.At(Top)?.FromProgram);
        Assert.Equal("paste", merged.At(new WheelSlotRef(WheelRing.Inner, 1))?.Macro?.Name);
        Assert.Null(rules.Resolve(scene, WheelSource.Program, false, macros)
            .At(new WheelSlotRef(WheelRing.Inner, 1))?.Macro);
    }

    [Fact]
    public void The_wheel_state_round_trips_through_its_own_file()
    {
        InOwnFiles(() =>
        {
            Assert.Equal(WheelOptions.RightButton,
                WheelMenuStore.Load().Options.Launcher);

            var state = new WheelMenuState();
            state.Options.Launcher = "Mouse 4";
            state.Options.Diameter = 520;
            state.Rules.Global.At(Top).Macro = "copy";
            var scene = state.Rules.AddScene(new WindowRule(Process: "notepad",
                How: WindowHow.ProcessEquals));
            scene.At(Right).Macro = "cut";
            WheelMenuStore.Save(state);

            var read = WheelMenuStore.Load();
            Assert.Equal("Mouse 4", read.Options.Launcher);
            Assert.Equal(520, read.Options.Diameter);
            Assert.Equal("copy", read.Rules.Global.At(Top).Macro);
            Assert.Equal("cut", Assert.Single(read.Rules.Scenes).At(Right).Macro);
        });
    }

    [Fact]
    public void The_wheel_page_opens_with_its_preview_button()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var window = new SettingsWindow(null, null, SettingsSubPage.WheelMenu);
            window.Show();

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            Assert.True(viewModel.IsWheelMenuPage);
            Assert.Equal(SettingsRoot.Triggers, viewModel.SelectedRoot);
            Assert.True(viewModel.WheelLauncherDefault);
            Assert.NotNull(window.FindControl<Button>("PreviewWheelButton"));

            window.Close();
        }));
    }

    [Fact]
    public void The_centre_says_close_and_the_invisible_circle_names_its_action()
    {
        Ui.Run(() =>
        {
            var rules = new WheelMenuRules();
            rules.Global.At(Top).Macro = "copy";
            var extended = new WheelSlotRef(WheelRing.Extended, 2);
            rules.Global.At(extended).Macro = "paste";
            var layout = rules.Resolve(null, WheelSource.Global, false,
                [
                    new MacroItem { Name = "copy", IsEnabled = true },
                    new MacroItem { Name = "paste", IsEnabled = true, Description = "粘贴纯文本" },
                ]);
            var window = new WheelMenuWindow(layout, new WheelOptions(),
                new PixelPoint(400, 400), preview: true);

            Assert.Equal(Strings.Get("Wheel.Close"), window.CenterHint);
            window.Hover(Top);
            Assert.NotEqual(Strings.Get("Wheel.Close"), window.CenterHint);

            window.Hover(extended);
            Assert.Contains(Strings.Get("Wheel.ExtendedPrefix"), window.CenterHint);
            Assert.Contains("粘贴纯文本", window.CenterHint);

            window.Hover(null);
            Assert.Equal(Strings.Get("Wheel.Close"), window.CenterHint);
            window.Aim(600, 400);
            window.Close();
        });
    }

    [Fact]
    public void The_guide_line_stops_at_the_window_edge_when_the_pointer_is_far_outside()
    {
        var centre = new Point(210, 210);
        Assert.Equal(new Point(300, 210),
            WheelCanvas.ClampToSquare(centre, new Point(300, 210), 420));
        Assert.Equal(420, WheelCanvas.ClampToSquare(centre, new Point(900, 210), 420).X, 5);
        var diagonal = WheelCanvas.ClampToSquare(centre, new Point(-500, -500), 420);
        Assert.InRange(diagonal.X, 0, 420);
        Assert.InRange(diagonal.Y, 0, 420);
    }

    [Fact]
    public void A_preview_wheel_renders_without_any_global_hook()
    {
        Ui.Run(() =>
        {
            var rules = new WheelMenuRules();
            rules.Global.At(Top).Macro = "copy";
            var layout = rules.Resolve(null, WheelSource.Global, false,
                [new MacroItem { Name = "copy", IsEnabled = true }]);
            var window = new WheelMenuWindow(layout, new WheelOptions(),
                new PixelPoint(400, 400), preview: true);

            Assert.Equal(840, window.Width);
            Assert.True(window.Center.X >= 0);
            Assert.NotNull(window.SlotAt(Top)?.Macro);
            window.Hover(Right);
            window.Close();
        });
    }

    [Fact]
    public void A_third_circle_adds_positions_and_the_geometry_follows_it()
    {
        var classic = WheelGeometry.For(420, false);
        Assert.Equal(0, classic.ThirdCount);
        Assert.Equal(new WheelSlotRef(WheelRing.Outer, 2),
            WheelGeometry.At(160, 0, classic));

        var third = WheelGeometry.For(420, false, 8);
        Assert.Equal(8, third.ThirdCount);
        Assert.True(third.HasThird);
        Assert.Equal(new WheelSlotRef(WheelRing.Inner, 0),
            WheelGeometry.At(0, -60, third));
        Assert.Equal(new WheelSlotRef(WheelRing.Outer, 2),
            WheelGeometry.At(110, 0, third));
        Assert.Equal(new WheelSlotRef(WheelRing.Third, 2),
            WheelGeometry.At(170, 0, third));
        Assert.Equal(new WheelSlotRef(WheelRing.Extended, 2),
            WheelGeometry.At(400, 0, third));

        var hidden = WheelGeometry.For(420, false, 8, rings: 1);
        Assert.False(hidden.HasOuter);
        Assert.False(hidden.HasThird);
        Assert.Equal(new WheelSlotRef(WheelRing.Inner, 2),
            WheelGeometry.At(160, 0, hidden));
        Assert.Equal(new WheelSlotRef(WheelRing.Extended, 2),
            WheelGeometry.At(250, 0, hidden));
    }

    [Fact]
    public void An_empty_outer_circle_is_not_drawn_when_it_carries_no_action()
    {
        var rules = new WheelMenuRules();
        rules.Global.At(Top).Macro = "copy";
        var layout = rules.Resolve(null, WheelSource.Global, false,
            [new MacroItem { Name = "copy", IsEnabled = true }],
            thirdCapacity: 8, hideEmptyOuter: true);

        Assert.Equal(8, layout.ThirdCapacity);
        Assert.True(layout.HideEmptyOuter);
        Assert.DoesNotContain(layout.Slots, slot =>
            slot.Reference.Ring == WheelRing.Third && slot.Macro is not null);
    }

    [Fact]
    public void Visible_sub_actions_are_resolved_and_answered_by_direction()
    {
        var rules = new WheelMenuRules();
        var parent = rules.Global.At(Top);
        parent.Macro = "copy";
        parent.Children.Add(new WheelSlot { Macro = "paste", Note = "Plain text" });
        parent.Children.Add(new WheelSlot { Macro = "cut" });
        var layout = rules.Resolve(null, WheelSource.Global, false,
            [
                new MacroItem { Name = "copy", IsEnabled = true },
                new MacroItem { Name = "paste", IsEnabled = true },
                new MacroItem { Name = "cut", IsEnabled = true },
            ]);

        Assert.True(layout.At(Top)?.HasSub);
        Assert.Equal(2, layout.At(Top)?.Sub.Count);
        var child = new WheelSlotRef(WheelRing.Inner, 0, 1);
        Assert.Equal("cut", layout.At(child)?.Macro?.Name);

        var metrics = WheelGeometry.For(420, false);
        Assert.Null(WheelGeometry.SubAt(0, -100, metrics, 0, 2));
        Assert.Equal(1, WheelGeometry.SubAt(0, -(metrics.Edge + 40), metrics, 0, 2));
    }

    [Fact]
    public void A_release_while_a_child_is_aimed_at_runs_the_child()
    {
        var gesture = Gesture();
        gesture.Down(WheelOptions.RightButton, 100, 100);
        gesture.Move(300, 100);
        gesture.SetCenter(100, 100, WheelGeometry.For(420, false));
        gesture.Hover(new WheelSlotRef(WheelRing.Inner, 2, 1));

        var selected = gesture.Up(WheelOptions.RightButton, 160, 100);
        Assert.Equal(WheelGestureAction.Select, selected.Action);
        Assert.Equal(new WheelSlotRef(WheelRing.Inner, 2, 1), selected.Slot);
    }

    [Fact]
    public void The_three_sources_answer_the_same_position_differently()
    {
        var rules = new WheelMenuRules();
        rules.Global.At(Top).Macro = "copy";
        var scene = rules.AddScene(new WindowRule(Process: "notepad",
            How: WindowHow.ProcessEquals));
        scene.At(Top).Macro = "cut";
        var macros = new[]
        {
            new MacroItem { Name = "copy", IsEnabled = true },
            new MacroItem { Name = "cut", IsEnabled = true },
        };

        Assert.Equal("copy", rules.Resolve(scene, WheelSource.Global, false, macros)
            .At(Top)?.Macro?.Name);
        Assert.Equal("cut", rules.Resolve(scene, WheelSource.Merged, false, macros)
            .At(Top)?.Macro?.Name);
        Assert.Equal("cut", rules.Resolve(scene, WheelSource.Program, false, macros)
            .At(Top)?.Macro?.Name);
    }

    [Fact]
    public void Every_new_wheel_choice_round_trips_through_its_own_file()
    {
        InOwnFiles(() =>
        {
            var state = new WheelMenuState();
            var options = state.Options;
            options.FontSize = 18;
            options.TimeoutMs = 5000;
            options.ThirdCapacity = 16;
            options.RepeatKey = "F8";
            options.ShowGuide = false;
            options.ShowSubActions = false;
            options.FollowTheme = false;
            options.Gloss = false;
            options.HoverBorder = false;
            options.PopAnimation = false;
            options.HideEmptyOuter = false;
            state.Rules.Global.At(Top).Macro = "copy";
            state.Rules.Global.At(Top).Note = "Copy the text";
            state.Rules.Global.At(Top).Children.Add(new WheelSlot { Macro = "paste" });
            WheelMenuStore.Save(state);

            var read = WheelMenuStore.Load();
            Assert.Equal(18, read.Options.FontSize);
            Assert.Equal(5000, read.Options.TimeoutMs);
            Assert.Equal(16, read.Options.ThirdCapacity);
            Assert.Equal("F8", read.Options.RepeatKey);
            Assert.False(read.Options.ShowGuide);
            Assert.False(read.Options.FollowTheme);
            Assert.False(read.Options.HideEmptyOuter);
            Assert.Equal("Copy the text", read.Rules.Global.At(Top).Note);
            Assert.Equal("paste", Assert.Single(read.Rules.Global.At(Top).Children).Macro);
            Assert.Equal(0, new WheelOptions { ThirdCapacity = 7 }.Clamped().ThirdCapacity);
        });
    }

    [Fact]
    public void The_wheel_actions_page_hosts_the_editor()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var window = new SettingsWindow(null, null, SettingsSubPage.WheelActions);
            window.Show();

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            Assert.True(viewModel.IsWheelActionsPage);
            Assert.Equal(SettingsRoot.Triggers, viewModel.SelectedRoot);
            Assert.NotEmpty(viewModel.WheelScenes);
            Assert.IsType<WheelMenuEditorView>(
                Assert.IsType<ContentControl>(window.FindControl<ContentControl>("WheelEditorHost"))
                    .Content);

            window.Close();
        }));
    }

    [Fact]
    public void The_menu_launcher_check_boxes_set_the_middle_button_or_a_custom_key()
    {
        InOwnFiles(() =>
        {
            var viewModel = new SettingsViewModel(() => DriverInputState.Ready);
            viewModel.MenuLauncherMiddle = true;
            Assert.Equal(WheelOptions.MiddleButton, LocalSettings.LoadMenuLauncher());
            Assert.True(viewModel.MenuLauncherMiddle);

            Assert.True(viewModel.CaptureMenuLauncher("F9"));
            Assert.Equal("F9", LocalSettings.LoadMenuLauncher());
            Assert.True(viewModel.MenuLauncherCustom);
            Assert.False(viewModel.MenuLauncherMiddle);
        });
    }

    private static WheelGesture Gesture() => new(
        () => WheelOptions.RightButton,
        () => WheelOptions.UsualRepeatKey,
        () => true);

    private static void InOwnFiles(Action check)
    {
        var folder = Path.Combine(Path.GetTempPath(),
            "whalegenie-wheel-" + Guid.NewGuid().ToString("N"));
        var settings = LocalSettings.FilePath;
        var wheel = WheelMenuStore.FilePathOverride;
        try
        {
            Directory.CreateDirectory(folder);
            LocalSettings.FilePath = Path.Combine(folder, "settings.json");
            WheelMenuStore.FilePathOverride = Path.Combine(folder, "wheel-menu.json");
            check();
        }
        finally
        {
            LocalSettings.FilePath = settings;
            WheelMenuStore.FilePathOverride = wheel;
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
