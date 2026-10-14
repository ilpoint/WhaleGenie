using SharpHook;
using SharpHook.Data;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Where the hook library's own vocabulary is turned into the events this program publishes.
/// Installing a hook of the real kind needs a desktop, so what is checked here is the turning:
/// which key, which button, which way the wheel went, whether a program sent the input, and that a
/// handler's answer about swallowing it reaches the library.
/// </summary>
public class HookTranslationTests
{
    [Fact]
    public void A_key_carries_the_key_and_whether_a_program_sent_it()
    {
        Assert.Equal(KeyNames.Resolve("F5"), HookTranslation.KeyDown(Keyboard(KeyCode.VcF5)).Key);
        Assert.False(HookTranslation.KeyDown(Keyboard(KeyCode.VcF5)).Injected);
        Assert.True(HookTranslation.KeyDown(Keyboard(KeyCode.VcF5, simulated: true)).Injected);

        // Letting go is the same event under another name: which way it went is the listeners'
        // business, so a release has to name the same key a press did.
        Assert.Equal(KeyNames.Resolve("F5"), HookTranslation.KeyUp(Keyboard(KeyCode.VcF5)).Key);
    }

    [Fact]
    public void A_button_arrives_with_where_the_pointer_was()
    {
        var raised = HookTranslation.MouseDown(Mouse(MouseButton.Button2, 1200, 34));

        Assert.NotNull(raised);
        Assert.Equal(HookMouseButton.Right, raised.Button);
        Assert.Equal(1200, raised.X);
        Assert.Equal(34, raised.Y);
        Assert.False(raised.Injected);

        Assert.Equal(
            HookMouseButton.Right,
            HookTranslation.MouseUp(Mouse(MouseButton.Button2, 1200, 34))!.Button);
    }

    [Fact]
    public void A_button_with_no_name_here_is_not_reported_at_all()
    {
        // A trigger is bound to one of the buttons a macro can name, so a button nobody named has
        // to be left out rather than taken for the left one.
        Assert.Null(HookTranslation.MouseDown(Mouse(MouseButton.NoButton, 1, 2)));
    }

    [Fact]
    public void A_move_carries_the_place_and_a_relative_move_carries_the_amount()
    {
        var place = HookTranslation.Moved(Mouse(MouseButton.Button1, 640, 480));
        Assert.Equal(640, place.X);
        Assert.Equal(480, place.Y);

        // The library carries a relative move's amount where a place would go, so that is where it
        // is read from: a recorded pointer that read a place out of it would jump near a corner
        // instead of moving by a few pixels.
        var amount = HookTranslation.MovedRelative(Mouse(MouseButton.Button1, 3, -7));
        Assert.Equal(3, amount.DeltaX);
        Assert.Equal(-7, amount.DeltaY);
    }

    [Fact]
    public void The_wheel_arrives_with_its_way_round_and_how_far_it_turned()
    {
        var raised = HookTranslation.Wheel(
            new MouseWheelHookEventArgs(new UioHookEvent
            {
                Type = EventType.MouseWheel,
                Wheel = new MouseWheelEventData
                {
                    Direction = MouseWheelScrollDirection.Horizontal,
                    Rotation = -120,
                    Delta = 1,
                    X = 44,
                    Y = 55,
                },
            }));

        Assert.Equal(HookWheelAxis.Horizontal, raised.Axis);
        Assert.Equal(-120, raised.Rotation);
        Assert.Equal(1, raised.Delta);
        Assert.Equal(44, raised.X);
        Assert.Equal(55, raised.Y);
        Assert.False(raised.Injected);
    }

    [Fact]
    public void An_answer_about_swallowing_the_input_reaches_the_library()
    {
        // A picker takes the click it was given away from the window underneath, and it says so on
        // the event it was handed: this is the line that carries that answer back to the hook.
        var args = Mouse(MouseButton.Button1, 5, 6);

        HookTranslation.Swallow(asked: false, args);
        Assert.False(args.SuppressEvent);

        HookTranslation.Swallow(asked: true, args);
        Assert.True(args.SuppressEvent);
    }

    /// <summary>A press of one key, as the library reports it.</summary>
    private static KeyboardHookEventArgs Keyboard(KeyCode code, bool simulated = false)
        => new(new UioHookEvent
        {
            Type = EventType.KeyPressed,
            Mask = simulated ? EventMask.SimulatedEvent : EventMask.None,
            Keyboard = new KeyboardEventData { KeyCode = code },
        });

    /// <summary>A press of one mouse button, as the library reports it.</summary>
    private static MouseHookEventArgs Mouse(MouseButton button, int x, int y)
        => new(new UioHookEvent
        {
            Type = EventType.MousePressed,
            Mouse = new MouseEventData { Button = button, X = (short)x, Y = (short)y },
        });
}
