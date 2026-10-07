using System.Collections.Generic;
using System.Linq;
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

    [Fact]
    public void An_empty_recording_produces_no_actions()
        => Assert.Empty(RecordingTranslator.Translate([], new RecordingOptions()));
}
