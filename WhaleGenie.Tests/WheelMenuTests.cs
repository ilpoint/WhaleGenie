using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
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
    public void A_short_right_press_is_given_back_as_a_click_and_a_swipe_opens_the_wheel()
    {
        var gesture = Gesture();

        // The press belongs to the wheel while it is held: nothing under the pointer may draw a
        // selection of its own from a button that is only being held down.
        var down = gesture.Down(WheelOptions.RightButton, 100, 100);
        Assert.Equal(WheelGestureAction.None, down.Action);
        Assert.True(down.Swallow);
        Assert.True(gesture.IsPending);

        // Nothing moved, so it was an ordinary click after all and is sent again on release.
        var up = gesture.Up(WheelOptions.RightButton, 101, 101);
        Assert.Equal(WheelGestureAction.Replay, up.Action);
        Assert.True(up.Swallow);
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
        Assert.True(selected.Swallow);
        Assert.False(gesture.IsOpen);
    }

    [Fact]
    public void A_release_after_escape_is_swallowed_and_answers_nothing()
    {
        var gesture = Gesture();
        gesture.Down(WheelOptions.RightButton, 100, 100);
        gesture.Move(300, 100);
        Assert.Equal(WheelGestureAction.Cancel, gesture.Key("Esc").Action);

        // The wheel is gone but the button is still down: its release must not reach the
        // application, which never saw the press either.
        var up = gesture.Up(WheelOptions.RightButton, 300, 100);
        Assert.Equal(WheelGestureAction.None, up.Action);
        Assert.True(up.Swallow);

        // And the next press is a wheel press again, not a leftover of the cancelled one.
        Assert.True(gesture.Down(WheelOptions.RightButton, 100, 100).Swallow);
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
        var down = gesture.Down(WheelOptions.MiddleButton, 100, 100);
        Assert.Equal(WheelGestureAction.None, down.Action);
        Assert.False(down.Swallow, "a button that is not the launcher must reach the application");
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
    public void The_machines_options_round_trip_through_their_own_file()
    {
        InOwnFiles(() =>
        {
            Assert.Equal(WheelOptions.RightButton,
                WheelMenuStore.Load().Options.Launcher);

            var state = new WheelMenuState();
            state.Options.Launcher = "Mouse 4";
            state.Options.Diameter = 520;
            WheelMenuStore.Save(state);

            var read = WheelMenuStore.Load();
            Assert.Equal("Mouse 4", read.Options.Launcher);
            Assert.Equal(520, read.Options.Diameter);
        });
    }

    /// <summary>
    /// A position names a macro, and a macro belongs to a project, so the positions travel in the
    /// project's trigger file rather than in the machine's wheel file.
    /// </summary>
    [Fact]
    public void The_positions_travel_with_the_project()
    {
        var triggers = new ProjectTriggers();
        triggers.Wheel.Global.At(Top).Macro = "copy";
        var scene = triggers.Wheel.AddScene(new WindowRule(Process: "notepad",
            How: WindowHow.ProcessEquals));
        scene.At(Right).Macro = "cut";

        var read = ProjectTriggers.FromJson(triggers.ToJson());
        Assert.Equal("copy", read.Wheel.Global.At(Top).Macro);
        Assert.Equal("cut", Assert.Single(read.Wheel.Scenes).At(Right).Macro);
        Assert.Equal(scene.Key, read.Wheel.Scenes[0].Key);

        // Opening a project replaces everything the previous one held, the wheel included.
        var opened = new ProjectTriggers();
        opened.ReplaceAll(read);
        Assert.Equal("copy", opened.Wheel.Global.At(Top).Macro);
        Assert.Single(opened.Wheel.Scenes);
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
            Assert.Equal(WheelOptions.RightButton, viewModel.SelectedWheelLauncher?.Binding);
            Assert.NotNull(window.FindControl<Button>("PreviewWheelButton"));

            window.Close();
        }));
    }

    /// <summary>
    /// The four buttons a wheel can be opened with are the ones a hand can hold while it moves.
    /// A keyboard binding is refused, and a stored one is read back as the default.
    /// </summary>
    [Fact]
    public void The_launcher_is_one_of_four_mouse_buttons_and_nothing_else()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var viewModel = new SettingsViewModel(() => DriverInputState.Ready, project: null);
            Assert.Equal(WheelOptions.LauncherButtons,
                viewModel.WheelLauncherChoices.Select(choice => choice.Binding));

            Assert.Equal(WheelOptions.RightButton,
                new WheelOptions { Launcher = "F2" }.Clamped().Launcher);
            Assert.Equal(string.Empty, new WheelOptions { Launcher = "" }.Clamped().Launcher);
            Assert.False(WheelOptions.IsAllowedLauncher(WheelOptions.MiddleButton + "x"));
        }));
    }

    /// <summary>
    /// The menu and the wheel both start on a press, so one button cannot open both: whichever ran
    /// second would never be seen. The page refuses the change and says which gesture took it.
    /// </summary>
    [Fact]
    public void A_button_the_menu_already_opens_on_cannot_open_the_wheel()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var viewModel = new SettingsViewModel(() => DriverInputState.Ready, project: null);
            Assert.True(viewModel.CaptureMenuLauncher(WheelOptions.MiddleButton));

            Assert.False(viewModel.SetWheelLauncher(WheelOptions.MiddleButton));
            Assert.NotEqual(WheelOptions.MiddleButton, viewModel.WheelLauncher);
            Assert.Equal(Strings.Get("Wheel.LauncherConflict"), viewModel.WheelLauncherStatus);

            // The other three are untouched by the refusal.
            Assert.True(viewModel.SetWheelLauncher(WheelOptions.BackButton));
            Assert.Equal(WheelOptions.BackButton, viewModel.WheelLauncher);
        }));
    }

    [Fact]
    public void The_menu_cannot_take_the_button_the_wheel_opens_on()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var viewModel = new SettingsViewModel(() => DriverInputState.Ready, project: null);
            Assert.True(viewModel.SetWheelLauncher(WheelOptions.ForwardButton));

            Assert.False(viewModel.CaptureMenuLauncher(WheelOptions.ForwardButton));
            Assert.NotEqual(WheelOptions.ForwardButton, viewModel.MenuLauncher);
            Assert.Equal(Strings.Get("Settings.MenuLauncherConflictWheel"),
                viewModel.MenuLauncherStatus);
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
            options.FollowTheme = false;
            options.Gloss = false;
            options.HoverBorder = false;
            options.PopAnimation = false;
            options.HideEmptyOuter = false;
            options.OnlyInPrograms = true;
            WheelMenuStore.Save(state);

            var read = WheelMenuStore.Load();
            Assert.Equal(18, read.Options.FontSize);
            Assert.Equal(5000, read.Options.TimeoutMs);
            Assert.Equal(16, read.Options.ThirdCapacity);
            Assert.Equal("F8", read.Options.RepeatKey);
            Assert.False(read.Options.ShowGuide);
            Assert.False(read.Options.FollowTheme);
            Assert.False(read.Options.HideEmptyOuter);
            Assert.True(read.Options.OnlyInPrograms);
            Assert.False(new WheelOptions().OnlyInPrograms);
            Assert.Equal(0, new WheelOptions { ThirdCapacity = 7 }.Clamped().ThirdCapacity);

            // A note is part of the position, so it belongs to the project's file.
            var triggers = new ProjectTriggers();
            triggers.Wheel.Global.At(Top).Macro = "copy";
            triggers.Wheel.Global.At(Top).Note = "Copy the text";
            var reread = ProjectTriggers.FromJson(triggers.ToJson());
            Assert.Equal("Copy the text", reread.Wheel.Global.At(Top).Note);
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

    /// <summary>
    /// Adding a scene rebuilds the list by clearing it, and a recycled row container is built once
    /// with nothing in it. Reading the row without a guard threw from the template and took the
    /// whole program down, which is what made "add" look like "crash".
    /// </summary>
    [Fact]
    public void Adding_a_scene_while_the_editor_is_open_keeps_the_window_alive()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var window = new SettingsWindow(null, null, SettingsSubPage.WheelActions);
            window.Show();

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            viewModel.AddWheelScene(new WindowRule(Title: "Notepad"));
            viewModel.AddWheelScene(new WindowRule(Title: "Browser"));

            Assert.Equal(3, viewModel.WheelScenes.Count);
            Assert.NotNull(viewModel.SelectedWheelScene);
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

    /// <summary>
    /// The hook writes the pointer position and the window reads it once a frame: what happened
    /// between two frames is one drawing, and nothing written is lost by the reading that took it.
    /// </summary>
    [Fact]
    public void A_pointer_that_moves_between_frames_is_read_once_per_frame()
    {
        var aim = new WheelAim();
        aim.Move(10, 20);
        aim.Move(30, 40);
        aim.Move(50, 60);

        Assert.True(aim.Take(out var x, out var y));
        Assert.Equal(50, x);
        Assert.Equal(60, y);
        Assert.False(aim.Take(out _, out _), "a pointer that has not moved drew again");
        Assert.Equal((3L, 1L), aim.Counts);
    }

    [Fact]
    public void A_move_written_after_a_reading_is_read_by_the_next_one()
    {
        var aim = new WheelAim();
        aim.Move(1, 2);
        Assert.True(aim.Take(out _, out _));

        aim.Move(3, 4);
        Assert.True(aim.Take(out var x, out var y));
        Assert.Equal(3, x);
        Assert.Equal(4, y);
    }

    [Fact]
    public void A_closed_wheel_forgets_the_move_it_never_drew()
    {
        var aim = new WheelAim();
        aim.Move(7, 8);
        aim.Clear();

        Assert.False(aim.Take(out _, out _));
    }


    [Fact]
    public void The_wheel_only_opens_where_a_program_scene_matches()
    {
        var windows = new WindowsForChecks()
            .Add(1, "Notes", "notepad", "Notepad")
            .Add(2, "Browser", "chrome", "Chrome");
        var notepad = windows.Open[0];
        var browser = windows.Open[1];
        var rules = new WheelMenuRules();

        // Without the switch the wheel opens everywhere, which is what it did until now.
        Assert.True(rules.OpensFor(notepad, windows, onlyInPrograms: false));
        Assert.True(rules.OpensFor(browser, windows, onlyInPrograms: false));

        // With it on, and no scene at all, it opens nowhere.
        Assert.False(rules.OpensFor(notepad, windows, onlyInPrograms: true));

        rules.AddScene(new WindowRule(Process: "notepad", How: WindowHow.ProcessEquals));
        Assert.True(rules.OpensFor(notepad, windows, onlyInPrograms: true));
        Assert.False(rules.OpensFor(browser, windows, onlyInPrograms: true));

        // A missing foreground window is not a match: the wheel stays out of the way.
        Assert.False(rules.OpensFor(null, windows, onlyInPrograms: true));
    }

    [Fact]
    public void Editing_an_inherited_position_writes_a_program_override_and_clearing_empties_it()
    {
        InOwnFiles(() =>
        {
            var project = new MainViewModel();
            var settings = new SettingsViewModel(() => DriverInputState.Ready, project: project);
            var scene = project.Triggers.Wheel.AddScene(
                new WindowRule(Process: "notepad", How: WindowHow.ProcessEquals));
            project.Triggers.Wheel.Global.At(Top).Macro = "copy";
            settings.RefreshWheelScenes();
            settings.SelectedWheelScene =
                settings.WheelScenes.First(row => row.Key == scene.Key);

            // The editor starts from the global action the position is drawn with, and saving it
            // writes that action into this program's own scene.
            Assert.True(settings.IsWheelSlotInherited(Top));
            var draft = Assert.IsType<WheelSlot>(settings.WheelSlotForEdit(Top));
            Assert.Equal("copy", draft.Macro);
            draft.Note = "Copy the text";
            settings.SetWheelAction(Top, draft);

            Assert.False(settings.IsWheelSlotInherited(Top));
            Assert.Equal("copy", scene.At(Top).Macro);
            Assert.Equal("Copy the text", scene.At(Top).Note);

            // Clearing through the same path leaves nothing behind.
            settings.SetWheelAction(Top, new WheelSlot());
            Assert.True(scene.At(Top).IsEmpty);
            Assert.Equal(string.Empty, scene.At(Top).Note);
            Assert.Null(scene.At(Top).Icon);

            // The global scene still holds the action this program was inheriting.
            Assert.Equal("copy", project.Triggers.Wheel.Global.At(Top).Macro);
        });
    }

    [Fact]
    public void Clearing_the_global_action_does_not_touch_another_scene()
    {
        var rules = new WheelMenuRules();
        var scene = rules.AddScene(new WindowRule(Process: "notepad",
            How: WindowHow.ProcessEquals));
        scene.At(Top).Macro = "cut";
        rules.Find(WheelScene.GlobalKey)!.At(Top).Macro = "copy";

        Assert.Equal("copy", rules.Global.At(Top).Macro);
        rules.Global.At(Top).Macro = string.Empty;
        Assert.Equal("cut", scene.At(Top).Macro);
    }


    [Fact]
    public void The_action_dialog_opens_on_the_action_it_was_given()
    {
        Ui.Run(() =>
        {
            var slot = new WheelSlot { Macro = "copy", Note = "Copy the text" };
            var macros = new List<MacroItem>
            {
                new() { Name = "copy" },
                new() { Name = "paste" },
            };

            var window = new WheelActionWindow(slot, macros,
                "Inner circle, position 1", inherited: false);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var controls = window.GetVisualDescendants().OfType<Control>().ToList();
            Assert.Equal("copy", controls.OfType<TextBlock>()
                .Single(control => control.Name == "ActionMacroName").Text);
            Assert.Equal("Copy the text", controls.OfType<TextBox>()
                .Single(control => control.Name == "ActionNote").Text);
            // An empty program position inherits the global action again, so "clear this position"
            // is not offered where clearing would leave the wheel showing the same action.
            var clear = controls.OfType<Button>().Single(button =>
                Equals(button.Content, Strings.Get("Wheel.ActionClearSlot")));
            Assert.True(clear.IsVisible);
            window.Close();

            var inherited = new WheelActionWindow(slot, macros,
                "Inner circle, position 1", inherited: true);
            inherited.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var inheritedClear = inherited.GetVisualDescendants().OfType<Button>().Single(button =>
                Equals(button.Content, Strings.Get("Wheel.ActionClearSlot")));
            Assert.False(inheritedClear.IsVisible);
            inherited.Close();
        });
    }


    [Fact]
    public void The_only_in_programs_switch_is_saved_and_warns_when_no_scene_exists()
    {
        InOwnFiles(() =>
        {
            var settings = new SettingsViewModel(() => DriverInputState.Ready);
            Assert.False(settings.OnlyInPrograms);

            settings.OnlyInPrograms = true;
            Assert.True(WheelMenuStore.Load().Options.OnlyInPrograms);
            Assert.Equal(Strings.Get("Wheel.OnlyInProgramsNoScene"),
                settings.WheelLauncherStatus);
        });
    }

    [Fact]
    public void A_system_hotkey_cannot_take_the_key_the_menu_already_opens_on()
    {
        InOwnFiles(() =>
        {
            var settings = new SettingsViewModel(() => DriverInputState.Ready);
            Assert.True(settings.CaptureMenuLauncher("F9"));

            Assert.False(settings.CaptureSystemHotkey("F9"));
            Assert.Equal(Strings.Get("Settings.HotkeyConflictMenu"),
                settings.SystemHotkeyProblem);
            Assert.NotEqual("F9", settings.SystemHotkey);
        });
    }


    [Fact]
    public void The_wheel_page_carries_the_only_in_programs_check_box()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var window = new SettingsWindow(null, null, SettingsSubPage.WheelMenu);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var viewModel = Assert.IsType<SettingsViewModel>(window.DataContext);
            var check = window.FindControl<CheckBox>("OnlyInProgramsCheck");
            Assert.NotNull(check);
            Assert.False(check.IsChecked);

            check.IsChecked = true;
            Assert.True(viewModel.OnlyInPrograms);
            Assert.True(WheelMenuStore.Load().Options.OnlyInPrograms);

            window.Close();
        }));
    }


    [Fact]
    public void A_chosen_icon_is_drawn_even_when_the_label_is_shown()
    {
        Ui.Run(() =>
        {
            var rules = new WheelMenuRules();
            rules.Global.At(Top).Macro = "copy";
            rules.Global.At(Top).Icon = new MenuIconReference(MenuIconKind.Builtin,
                MenuIconCatalog.Options[0].Key);
            var layout = rules.Resolve(null, WheelSource.Global, false,
                [new MacroItem { Name = "copy", IsEnabled = true }]);
            var options = new WheelOptions
            {
                PopAnimation = false,
                HideLabelWithIcon = false,
                FollowTheme = false,
            };

            var window = new WheelMenuWindow(layout, options, new PixelPoint(0, 0), preview: true);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            // The picker used to change nothing unless "labels off" was also on. The icon now has
            // to be there with the label, which is the state a wheel is normally in.
            Assert.NotEmpty(window.GetVisualDescendants().OfType<IconView>());
            window.Close();
        });
    }


    [Fact]
    public void The_editor_paints_the_icon_a_position_carries()
    {
        InOwnFiles(() => Ui.Run(() =>
        {
            var project = new MainViewModel();
            project.AddMacro(new MacroItem { Name = "copy" });
            project.Triggers.Wheel.Global.At(Top).Macro = "copy";
            project.Triggers.Wheel.Global.At(Top).Icon = new MenuIconReference(
                MenuIconKind.Builtin, MenuIconCatalog.Options[0].Key);

            var window = new SettingsWindow(project, null, SettingsSubPage.WheelActions);
            window.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();

            var editor = window.GetVisualDescendants().OfType<WheelMenuEditorView>().Single();
            Assert.NotEmpty(editor.GetVisualDescendants().OfType<IconView>());
            window.Close();
        }));
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
