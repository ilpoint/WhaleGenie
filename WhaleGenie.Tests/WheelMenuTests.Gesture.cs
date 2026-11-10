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

// The wheel gesture: the buttons that start it, how it ends, and the thread it runs on.
public partial class WheelMenuTests
{
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
}
