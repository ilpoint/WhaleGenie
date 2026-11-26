using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Recording;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The rules that turn a raw recording into macro steps. They are checked here because they can
/// be checked without a keyboard, a mouse or a desktop.
/// </summary>
public class RecordingTests
{
    private static IReadOnlyList<RecordedAction> Translate(
        RecordingOptions? options, params RecordedInput[] inputs)
        => RecordingTranslator.Translate(inputs, options);

    private static RecordingOptions Quiet => new() { Delays = false };

    [Fact]
    public void A_key_pressed_and_let_go_quickly_becomes_one_press()
    {
        var actions = Translate(Quiet, RecordedInput.KeyDown("A", 100), RecordedInput.KeyUp("A", 160));

        var press = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.KeyPress, press.Kind);
        Assert.Equal("A", press.Key);
        Assert.Equal(60, press.DurationMs);
    }

    [Fact]
    public void A_key_held_for_a_while_stays_down_until_it_is_let_go()
    {
        var actions = Translate(Quiet, RecordedInput.KeyDown("A", 0), RecordedInput.KeyUp("A", 2000));

        Assert.Equal(
            [RecordedActionKind.KeyDown, RecordedActionKind.KeyUp],
            actions.Select(action => action.Kind));
    }

    [Fact]
    public void Held_modifiers_and_a_key_become_one_hotkey()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.KeyDown("Ctrl", 1000),
            RecordedInput.KeyDown("C", 1030),
            RecordedInput.KeyUp("C", 1080),
            RecordedInput.KeyUp("Ctrl", 1120));

        var hotkey = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.Hotkey, hotkey.Kind);
        Assert.Equal("Ctrl+C", hotkey.Key);
        Assert.Equal(50, hotkey.DurationMs);
    }

    [Fact]
    public void A_hotkey_is_recognised_however_the_modifiers_come_back_up()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.KeyDown("Shift", 0),
            RecordedInput.KeyDown("Ctrl", 10),
            RecordedInput.KeyDown("A", 20),
            RecordedInput.KeyUp("A", 30),
            RecordedInput.KeyUp("Ctrl", 40),
            RecordedInput.KeyUp("Shift", 50));

        var hotkey = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.Hotkey, hotkey.Kind);
        Assert.Equal("Shift+Ctrl+A", hotkey.Key);
    }

    [Fact]
    public void A_key_held_across_a_chord_is_still_a_hotkey()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.KeyDown("Ctrl", 0),
            RecordedInput.KeyDown("A", 100),
            RecordedInput.KeyUp("A", 2000),
            RecordedInput.KeyUp("Ctrl", 2100));

        var hotkey = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.Hotkey, hotkey.Kind);
        Assert.Equal(1900, hotkey.DurationMs);
    }

    [Fact]
    public void A_press_and_release_in_one_place_becomes_a_click()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(100, 100, 0),
            RecordedInput.MouseDown("left", 100, 100, 10),
            RecordedInput.MouseUp("left", 100, 100, 40));

        var click = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.MouseClick, click.Kind);
        Assert.Equal("left", click.Button);
        Assert.Equal(100, click.X);
        Assert.Equal(100, click.Y);
    }

    [Fact]
    public void A_press_that_moves_is_recorded_as_a_drag()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(100, 100, 0),
            RecordedInput.MouseDown("left", 100, 100, 10),
            RecordedInput.MouseMove(120, 100, 40),
            RecordedInput.MouseMove(160, 100, 90),
            RecordedInput.MouseUp("left", 160, 100, 120));

        Assert.Equal(
            [RecordedActionKind.MouseDown, RecordedActionKind.MouseMove, RecordedActionKind.MouseMove,
             RecordedActionKind.MouseUp],
            actions.Select(action => action.Kind));

        Assert.Equal(100, actions[0].X);
        Assert.Equal(120, actions[1].X);
        Assert.Equal(160, actions[2].X);
        Assert.Equal(160, actions[3].X);
    }

    [Fact]
    public void Two_quick_clicks_in_one_place_become_a_double_click()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(50, 50, 0),
            RecordedInput.MouseDown("left", 50, 50, 10),
            RecordedInput.MouseUp("left", 50, 50, 40),
            RecordedInput.MouseDown("left", 50, 50, 120),
            RecordedInput.MouseUp("left", 50, 50, 150));

        var doubleClick = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.MouseDoubleClick, doubleClick.Kind);
    }

    [Fact]
    public void A_click_followed_by_a_different_click_stays_two_clicks()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(50, 50, 0),
            RecordedInput.MouseDown("left", 50, 50, 10),
            RecordedInput.MouseUp("left", 50, 50, 40),
            RecordedInput.MouseDown("left", 300, 300, 120),
            RecordedInput.MouseUp("left", 300, 300, 150));

        Assert.Equal(
            [RecordedActionKind.MouseClick, RecordedActionKind.MouseClick],
            actions.Select(action => action.Kind));
    }

    [Fact]
    public void Turning_the_wheel_the_same_way_becomes_one_scroll()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.Scroll("up", 1, 0, 0, 0),
            RecordedInput.Scroll("up", 1, 0, 0, 100),
            RecordedInput.Scroll("up", 2, 0, 0, 200));

        var scroll = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.Scroll, scroll.Kind);
        Assert.Equal("up", scroll.Direction);
        Assert.Equal(4, scroll.Amount);
    }

    [Fact]
    public void Turning_the_wheel_the_other_way_starts_a_new_scroll()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.Scroll("up", 1, 0, 0, 0),
            RecordedInput.Scroll("down", 1, 0, 0, 100));

        Assert.Equal(2, actions.Count);
        Assert.All(actions, action => Assert.Equal(RecordedActionKind.Scroll, action.Kind));
    }

    [Fact]
    public void Pointer_movement_is_thinned_to_a_readable_path()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(0, 0, 0),
            RecordedInput.MouseMove(5, 0, 10),
            RecordedInput.MouseMove(10, 0, 20),
            RecordedInput.MouseMove(20, 0, 70),
            RecordedInput.MouseMove(30, 0, 80));

        Assert.Equal(2, actions.Count);
        Assert.All(actions, action => Assert.Equal(RecordedActionKind.MouseMove, action.Kind));
        Assert.Equal(20, actions[0].X);
        Assert.Equal(30, actions[1].X);
    }

    [Fact]
    public void Relative_capture_writes_the_offset_instead_of_the_place()
    {
        var actions = Translate(
            new RecordingOptions { Delays = false, RelativeMovement = true },
            RecordedInput.MouseMove(100, 100, 0),
            RecordedInput.MouseMove(130, 90, 70));

        var move = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.MouseMoveRelative, move.Kind);
        Assert.Equal(30, move.X);
        Assert.Equal(-10, move.Y);
    }

    [Fact]
    public void A_pause_long_enough_to_have_been_meant_becomes_a_delay()
    {
        var actions = Translate(
            new RecordingOptions(),
            RecordedInput.KeyDown("A", 0),
            RecordedInput.KeyUp("A", 10),
            RecordedInput.KeyDown("B", 500),
            RecordedInput.KeyUp("B", 510));

        Assert.Equal(
            [RecordedActionKind.KeyPress, RecordedActionKind.Delay, RecordedActionKind.KeyPress],
            actions.Select(action => action.Kind));

        Assert.Equal(490, actions[1].DurationMs);
    }

    [Fact]
    public void A_pause_too_short_to_matter_is_left_out()
    {
        var actions = Translate(
            new RecordingOptions(),
            RecordedInput.KeyDown("A", 0),
            RecordedInput.KeyUp("A", 10),
            RecordedInput.KeyDown("B", 30),
            RecordedInput.KeyUp("B", 40));

        Assert.Equal(
            [RecordedActionKind.KeyPress, RecordedActionKind.KeyPress],
            actions.Select(action => action.Kind));
    }

    [Fact]
    public void The_wait_before_the_first_action_is_not_written_down()
    {
        var actions = Translate(
            new RecordingOptions(),
            RecordedInput.KeyDown("A", 5000),
            RecordedInput.KeyUp("A", 5010));

        var press = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.KeyPress, press.Kind);
    }

    [Fact]
    public void A_held_key_is_not_charged_a_delay_for_the_time_it_was_held()
    {
        var actions = Translate(
            new RecordingOptions(),
            RecordedInput.KeyDown("A", 0),
            RecordedInput.KeyUp("A", 400),
            RecordedInput.KeyDown("B", 420),
            RecordedInput.KeyUp("B", 430));

        // The 400 ms hold is part of the press, so only the 20 ms after it could be a pause,
        // and 20 ms is below the threshold.
        Assert.Equal(
            [RecordedActionKind.KeyPress, RecordedActionKind.KeyPress],
            actions.Select(action => action.Kind));
    }

    [Fact]
    public void A_release_without_a_press_is_ignored()
    {
        var actions = Translate(Quiet, RecordedInput.KeyUp("A", 10));

        Assert.Empty(actions);
    }

    [Fact]
    public void A_release_of_a_button_that_was_already_down_is_ignored()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseUp("left", 50, 50, 10),
            RecordedInput.MouseUp("left", 50, 50, 20));

        Assert.Empty(actions);
    }

    [Fact]
    public void The_keyboard_switch_turns_key_steps_off()
    {
        var actions = Translate(
            new RecordingOptions { KeyboardKeys = false },
            RecordedInput.KeyDown("A", 0),
            RecordedInput.KeyUp("A", 10));

        Assert.Empty(actions);
    }

    [Fact]
    public void The_mouse_switch_turns_mouse_steps_off()
    {
        var actions = Translate(
            new RecordingOptions { MouseButtons = false, MouseMovement = false },
            RecordedInput.MouseMove(10, 10, 0),
            RecordedInput.MouseMove(80, 10, 80),
            RecordedInput.MouseDown("left", 80, 10, 90),
            RecordedInput.MouseUp("left", 80, 10, 120),
            RecordedInput.Scroll("up", 1, 80, 10, 130));

        Assert.Empty(actions);
    }

    /// <summary>
    /// A controller button pressed and let go again quickly is one tap, the way a key is. The button
    /// travels named the way a macro names it, so the step that comes of it presses the same button.
    /// </summary>
    [Fact]
    public void A_controller_button_pressed_quickly_becomes_one_tap()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.GamepadDown("a", 100),
            RecordedInput.GamepadUp("a", 160));

        var tap = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.GamepadPress, tap.Kind);
        Assert.Equal("a", tap.Key);
        Assert.Equal(60, tap.DurationMs);
    }

    /// <summary>
    /// A button held longer than a tap stays down until it is let go, like a key: that is what a macro
    /// needs for a button a game reads while other steps run.
    /// </summary>
    [Fact]
    public void A_controller_button_held_stays_down_until_it_is_let_go()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.GamepadDown("lb", 0),
            RecordedInput.GamepadUp("lb", 2000));

        Assert.Equal(
            [RecordedActionKind.GamepadDown, RecordedActionKind.GamepadUp],
            actions.Select(action => action.Kind));
    }

    /// <summary>A release for a button that was already down when recording started writes nothing.</summary>
    [Fact]
    public void A_controller_release_with_no_press_behind_it_writes_nothing()
    {
        var actions = Translate(Quiet, RecordedInput.GamepadUp("a", 10));

        Assert.Empty(actions);
    }

    /// <summary>The controller switch turns controller steps off, the way the mouse one does.</summary>
    [Fact]
    public void The_controller_switch_turns_controller_steps_off()
    {
        var actions = Translate(
            new RecordingOptions { GamepadButtons = false },
            RecordedInput.GamepadDown("a", 0),
            RecordedInput.GamepadUp("a", 60));

        Assert.Empty(actions);
    }



    [Fact]
    public void The_delay_switch_writes_no_pauses()
    {
        var actions = Translate(
            new RecordingOptions { Delays = false },
            RecordedInput.KeyDown("A", 0),
            RecordedInput.KeyUp("A", 10),
            RecordedInput.KeyDown("B", 900),
            RecordedInput.KeyUp("B", 910));

        Assert.DoesNotContain(actions, action => action.Kind == RecordedActionKind.Delay);
    }

    /// <summary>
    /// A pointer that keeps moving inside one interval is written once, at the position it had come
    /// to: the two positions in between are the same gesture, not three steps.
    /// </summary>
    [Fact]
    public void Pointer_moves_inside_the_interval_keep_only_the_latest_position()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(0, 0, 0),
            RecordedInput.MouseMove(10, 0, 100),
            RecordedInput.MouseMove(20, 0, 110),
            RecordedInput.MouseMove(30, 0, 120));

        Assert.Equal([10, 30], actions.Select(action => action.X));
    }

    /// <summary>A gap of exactly the interval is long enough: the move is written where it happened.</summary>
    [Fact]
    public void A_pointer_move_at_the_interval_is_written()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(0, 0, 0),
            RecordedInput.MouseMove(10, 0, 100),
            RecordedInput.MouseMove(20, 0, 160),
            RecordedInput.MouseMove(30, 0, 170));

        Assert.Equal([10, 20, 30], actions.Select(action => action.X));
    }

    /// <summary>
    /// A drag keeps the positions the pointer passed through, and drops the ones that came less than
    /// the drag interval apart.
    /// </summary>
    [Fact]
    public void A_drag_keeps_only_the_positions_it_waited_for()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(0, 0, 0),
            RecordedInput.MouseDown("left", 0, 0, 0),
            RecordedInput.MouseMove(10, 0, 25),
            RecordedInput.MouseMove(20, 0, 50),
            RecordedInput.MouseMove(30, 0, 60),
            RecordedInput.MouseUp("left", 30, 0, 60));

        Assert.Equal(
            [RecordedActionKind.MouseDown, RecordedActionKind.MouseMove, RecordedActionKind.MouseMove,
             RecordedActionKind.MouseUp],
            actions.Select(action => action.Kind));
        Assert.Equal([0, 10, 20, 30], actions.Select(action => action.X));
    }

    /// <summary>A press held for exactly the click length is still a click; longer is a drag.</summary>
    [Fact]
    public void A_press_held_for_the_click_length_is_still_a_click()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(0, 0, 0),
            RecordedInput.MouseDown("left", 0, 0, 100),
            RecordedInput.MouseUp("left", 0, 0, 450));

        var click = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.MouseClick, click.Kind);
    }

    /// <summary>Two clicks exactly the double-click gap apart are one double click.</summary>
    [Fact]
    public void Two_clicks_at_the_double_click_gap_are_one_double_click()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(50, 50, 0),
            RecordedInput.MouseDown("left", 50, 50, 10),
            RecordedInput.MouseUp("left", 50, 50, 40),
            RecordedInput.MouseDown("left", 50, 50, 440),
            RecordedInput.MouseUp("left", 50, 50, 470));

        var doubleClick = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.MouseDoubleClick, doubleClick.Kind);
    }

    /// <summary>
    /// A press that lands on the position still waiting belongs to that position: the click carries
    /// the spot, so no move step of its own is written for it.
    /// </summary>
    [Fact]
    public void A_press_on_the_waiting_position_writes_no_move_of_its_own()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(0, 0, 0),
            RecordedInput.MouseMove(2, 2, 10),
            RecordedInput.MouseDown("left", 2, 2, 20),
            RecordedInput.MouseUp("left", 2, 2, 40));

        var click = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.MouseClick, click.Kind);
        Assert.Equal(2, click.X);
        Assert.Equal(2, click.Y);
    }

    /// <summary>A move that changes only one axis is still a move, and is written.</summary>
    [Fact]
    public void A_move_along_one_axis_is_written()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(0, 0, 0),
            RecordedInput.MouseMove(0, 10, 100));

        var move = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.MouseMove, move.Kind);
        Assert.Equal(0, move.X);
        Assert.Equal(10, move.Y);
    }

    /// <summary>
    /// Letting go of the last button ends the drag: a move after it is a move, not another position
    /// of the drag that was just finished.
    /// </summary>
    [Fact]
    public void A_move_after_the_button_is_let_go_is_written_as_a_move()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(0, 0, 0),
            RecordedInput.MouseDown("left", 0, 0, 10),
            RecordedInput.MouseUp("left", 0, 0, 40),
            RecordedInput.MouseMove(50, 50, 1000));

        Assert.Equal(
            [RecordedActionKind.MouseClick, RecordedActionKind.MouseMove],
            actions.Select(action => action.Kind));
        Assert.Equal(50, actions[1].X);
    }

    /// <summary>A wheel turn still writes the position that was waiting before it.</summary>
    [Fact]
    public void A_wheel_turn_writes_the_position_that_was_waiting()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(0, 0, 0),
            RecordedInput.MouseMove(10, 10, 10),
            RecordedInput.Scroll("up", 1, 10, 10, 20));

        Assert.Equal(
            [RecordedActionKind.MouseMove, RecordedActionKind.Scroll],
            actions.Select(action => action.Kind));
        Assert.Equal(10, actions[0].X);
    }

    /// <summary>A wheel event of no notches writes nothing at all.</summary>
    [Fact]
    public void A_wheel_turn_of_no_notches_writes_nothing()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(0, 0, 0),
            RecordedInput.Scroll("up", 0, 0, 0, 10));

        Assert.Empty(actions);
    }

    /// <summary>Turns of the wheel exactly the coalescing gap apart are still one gesture.</summary>
    [Fact]
    public void Two_wheel_turns_at_the_coalescing_gap_are_one_scroll()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.Scroll("up", 1, 0, 0, 100),
            RecordedInput.Scroll("up", 1, 0, 0, 500));

        var scroll = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.Scroll, scroll.Kind);
        Assert.Equal(2, scroll.Amount);
    }

    /// <summary>A key held for exactly the tap length is still one press.</summary>
    [Fact]
    public void A_key_held_for_the_tap_length_is_one_press()
    {
        var actions = Translate(Quiet, RecordedInput.KeyDown("A", 0), RecordedInput.KeyUp("A", 700));

        var press = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.KeyPress, press.Kind);
        Assert.Equal(700, press.DurationMs);
    }

    /// <summary>A controller button held for exactly the tap length is still one tap.</summary>
    [Fact]
    public void A_controller_button_held_for_the_tap_length_is_one_tap()
    {
        var actions = Translate(Quiet, RecordedInput.GamepadDown("a", 0), RecordedInput.GamepadUp("a", 700));

        var tap = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.GamepadPress, tap.Kind);
    }

    /// <summary>Two actions at the same moment leave no pause between them.</summary>
    [Fact]
    public void Two_actions_at_the_same_moment_leave_no_pause()
    {
        var actions = Translate(
            new RecordingOptions(),
            RecordedInput.KeyDown("A", 0),
            RecordedInput.KeyUp("A", 10),
            RecordedInput.KeyDown("B", 10),
            RecordedInput.KeyUp("B", 20));

        Assert.DoesNotContain(actions, action => action.Kind == RecordedActionKind.Delay);
    }

    /// <summary>A pause of exactly the threshold has been meant, and is written down.</summary>
    [Fact]
    public void A_pause_of_exactly_the_threshold_is_written()
    {
        var actions = Translate(
            new RecordingOptions(),
            RecordedInput.KeyDown("A", 0),
            RecordedInput.KeyUp("A", 10),
            RecordedInput.KeyDown("B", 60),
            RecordedInput.KeyUp("B", 70));

        var delay = Assert.Single(actions, action => action.Kind == RecordedActionKind.Delay);
        Assert.Equal(50, delay.DurationMs);
    }

    /// <summary>
    /// A controller tap keeps the timeline busy only while it is being pressed: the pause after it
    /// is measured from where it started, not from the end of the press the game never sees as a
    /// hold.
    /// </summary>
    [Fact]
    public void A_controller_tap_does_not_hold_the_timeline()
    {
        var actions = Translate(
            new RecordingOptions(),
            RecordedInput.GamepadDown("a", 0),
            RecordedInput.GamepadUp("a", 60),
            RecordedInput.KeyDown("B", 70),
            RecordedInput.KeyUp("B", 80));

        var delay = Assert.Single(actions, action => action.Kind == RecordedActionKind.Delay);
        Assert.Equal(70, delay.DurationMs);
    }

    /// <summary>A modifier held when the recording ends is one key down, not half a chord.</summary>
    [Fact]
    public void A_modifier_still_held_at_the_end_is_one_key_down()
    {
        var actions = Translate(Quiet, RecordedInput.KeyDown("Ctrl", 0));

        var down = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.KeyDown, down.Kind);
        Assert.Equal("Ctrl", down.Key);
    }

    /// <summary>A chord after another action is still one step, and what follows it is kept.</summary>
    [Fact]
    public void A_chord_after_a_move_is_one_step_and_the_next_key_is_kept()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseMove(0, 0, 0),
            RecordedInput.MouseMove(10, 0, 100),
            RecordedInput.KeyDown("Ctrl", 110),
            RecordedInput.KeyDown("C", 120),
            RecordedInput.KeyUp("C", 130),
            RecordedInput.KeyUp("Ctrl", 140),
            RecordedInput.KeyDown("B", 150),
            RecordedInput.KeyUp("B", 160));

        Assert.Equal(
            [RecordedActionKind.MouseMove, RecordedActionKind.Hotkey, RecordedActionKind.KeyPress],
            actions.Select(action => action.Kind));
        Assert.Equal("Ctrl+C", actions[1].Key);
    }

    /// <summary>Letting go as far as the tolerance away is still a click.</summary>
    [Fact]
    public void A_release_within_the_tolerance_is_still_a_click()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.MouseDown("left", 0, 0, 0),
            RecordedInput.MouseUp("left", 6, 6, 10));

        var click = Assert.Single(actions);
        Assert.Equal(RecordedActionKind.MouseClick, click.Kind);
        Assert.Equal(6, click.X);
        Assert.Equal(6, click.Y);
    }

    /// <summary>Turns that add up to the largest scroll are still one step.</summary>
    [Fact]
    public void Wheel_turns_that_add_up_to_the_largest_scroll_are_one_step()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.Scroll("up", 59, 0, 0, 0),
            RecordedInput.Scroll("up", 1, 0, 0, 100));

        var scroll = Assert.Single(actions);
        Assert.Equal(60, scroll.Amount);
    }

    /// <summary>Turns that would go past the largest scroll start a new step.</summary>
    [Fact]
    public void Wheel_turns_past_the_largest_scroll_start_a_new_step()
    {
        var actions = Translate(
            Quiet,
            RecordedInput.Scroll("up", 59, 0, 0, 0),
            RecordedInput.Scroll("up", 2, 0, 0, 100));

        Assert.Equal(2, actions.Count);
        Assert.Equal([59, 2], actions.Select(action => action.Amount));
    }

    [Fact]
    public void An_empty_recording_produces_no_actions()
        => Assert.Empty(RecordingTranslator.Translate([], new RecordingOptions()));
}
