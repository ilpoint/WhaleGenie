using System.Collections.Generic;
using SharpHook.Data;
using Viiper.Client.Devices.Keyboard;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// Driver-level input: the key names a macro writes turned into what a virtual USB device sends,
/// and the reports the device comes out with. A connection of its own needs a server and a kernel
/// driver on the machine, so what is checked here is everything up to the wire — the device is
/// handed a link that writes down what it was told instead of sending it.
/// </summary>
public class ViiperInputTests
{
    [Theory]
    [InlineData("A", Key.A)]
    [InlineData("z", Key.Z)]
    [InlineData("1", Key.Num1)]
    [InlineData("0", Key.Num0)]
    [InlineData("F1", Key.F1)]
    [InlineData("F24", Key.F24)]
    [InlineData("Enter", Key.Enter)]
    [InlineData("Esc", Key.Escape)]
    [InlineData("Space", Key.Space)]
    [InlineData("Tab", Key.Tab)]
    [InlineData("Up", Key.Up)]
    [InlineData("-", Key.Minus)]
    [InlineData("[", Key.LeftBrace)]
    [InlineData("NumPad5", Key.Kp5)]
    public void The_names_a_macro_writes_reach_the_device(string name, Key expected)
        => Assert.Equal(expected, ViiperKeys.Usage(KeyNames.Resolve(name)!.Value));

    [Theory]
    [InlineData("Ctrl", Mod.LeftCtrl)]
    [InlineData("Shift", Mod.LeftShift)]
    [InlineData("Alt", Mod.LeftAlt)]
    [InlineData("Win", Mod.LeftGUI)]
    [InlineData("RightCtrl", Mod.RightCtrl)]
    [InlineData("右Shift", Mod.RightShift)]
    public void A_modifier_travels_in_the_modifier_byte_instead_of_the_key_list(string name, Mod expected)
    {
        var code = KeyNames.Resolve(name)!.Value;

        Assert.Equal(expected, ViiperKeys.Modifier(code));
        Assert.Null(ViiperKeys.Usage(code));
    }

    [Theory]
    [InlineData("Yen")]
    [InlineData("Underscore")]
    [InlineData("Katakana")]
    public void A_key_a_virtual_keyboard_has_not_got_is_left_empty(string name)
    {
        var code = KeyNames.Resolve(name)!.Value;

        Assert.Null(ViiperKeys.Usage(code));
        Assert.Null(ViiperKeys.Modifier(code));
    }

    [Theory]
    [InlineData('a', Key.A, false)]
    [InlineData('A', Key.A, true)]
    [InlineData('7', Key.Num7, false)]
    [InlineData('!', Key.Num1, true)]
    [InlineData('?', Key.Slash, true)]
    [InlineData(' ', Key.Space, false)]
    [InlineData('\n', Key.Enter, false)]
    public void A_character_knows_its_key_and_whether_shift_goes_with_it(
        char character, Key key, bool shift)
        => Assert.Equal((key, shift), ViiperKeys.Typing(character));

    [Fact]
    public void A_character_no_keyboard_can_type_is_left_for_the_run_to_report()
        => Assert.Null(ViiperKeys.Typing('中'));

    [Fact]
    public void The_server_is_only_connected_to_when_a_step_needs_it()
    {
        var opened = 0;
        using var device = new ViiperInputDevice(() =>
        {
            opened++;
            return new FakeLink(0, 0);
        });

        Assert.Equal(0, opened);

        device.MoveMouse(10, 10, 0);
        Assert.Equal(1, opened);

        device.MoveMouse(20, 20, 0);
        Assert.Equal(1, opened);
    }

    [Fact]
    public void A_key_goes_down_and_comes_up_again_in_what_the_keyboard_reports()
    {
        var (device, link) = Linked();

        device.KeyPress("A", 1);

        // A report is the whole keyboard rather than a change to it, so letting go is an empty one.
        Assert.Equal(["keyboard 0:4", "keyboard 0:"], link.Sent);
    }

    [Fact]
    public void A_chord_holds_its_keys_together_and_lets_go_in_reverse()
    {
        var (device, link) = Linked();

        device.Hotkey(["Ctrl", "S"], 1);

        Assert.Equal(
        [
            "keyboard 1:",   // Ctrl goes down
            "keyboard 1:22", // S goes down with Ctrl still held
            "keyboard 1:",   // S comes up
            "keyboard 0:",   // Ctrl comes up
        ], link.Sent);
    }

    [Fact]
    public void Typing_a_capital_letter_carries_shift()
    {
        var (device, link) = Linked();

        device.TypeText("Hi", 1);

        Assert.Equal(
        [
            "keyboard 2:11", // H, with shift
            "keyboard 0:",
            "keyboard 0:12", // i, without it
            "keyboard 0:",
        ], link.Sent);
    }

    [Fact]
    public void A_character_no_virtual_keyboard_can_type_fails_the_step()
    {
        var (device, _) = Linked();

        var failure = Assert.Throws<DeviceActionException>(() => device.TypeText("中", 0));

        Assert.Equal("Run.NoDriverTyping", failure.Key);
    }

    [Fact]
    public void A_key_a_virtual_keyboard_has_not_got_fails_the_step()
    {
        var (device, _) = Linked();

        var failure = Assert.Throws<DeviceActionException>(() => device.KeyDown("Yen"));

        Assert.Equal("Run.NoDriverKey", failure.Key);
    }

    [Fact]
    public void A_key_name_that_means_nothing_fails_the_way_it_always_did()
    {
        var (device, _) = Linked();

        var failure = Assert.Throws<DeviceActionException>(() => device.KeyPress("Nonsense", 1));

        Assert.Equal("Run.UnknownKey", failure.Key);
    }

    [Fact]
    public void A_click_walks_the_pointer_there_and_presses_once()
    {
        var (device, link) = Linked(10, 20);

        device.Click("left", 110, 70, 1, 0);

        Assert.Equal(
        [
            "mouse 0:100,50:0,0", // the distance still to cover
            "mouse 1:0,0:0,0",    // pressed
            "mouse 0:0,0:0,0",    // let go
        ], link.Sent);

        Assert.Equal(new ScreenPoint(110, 70), link.Cursor);
    }

    [Fact]
    public void The_button_a_macro_names_is_the_one_the_device_presses()
    {
        var (device, link) = Linked(50, 50);

        device.MouseDown("right", 50, 50);
        device.MouseUp("right", 50, 50);

        Assert.Equal(
        [
            "mouse 2:0,0:0,0", // pressed, where the pointer already is
            "mouse 0:0,0:0,0", // let go
        ], link.Sent);
    }

    [Fact]
    public void Scrolling_down_turns_the_wheel_away_from_the_user()
    {
        var (device, link) = Linked();

        device.Scroll("down", 120, 0, 0);

        // One notch at a time: the device counts in whole notches, and the machine turns each of
        // them into the 120 units a step asks in.
        Assert.Equal(["mouse 0:0,0:-1,0"], link.Sent);
    }

    [Fact]
    public void Scrolling_three_notches_sends_one_report_each()
    {
        var (device, link) = Linked();

        device.Scroll("down", 360, 0, 0);

        Assert.Equal(
        [
            "mouse 0:0,0:-1,0",
            "mouse 0:0,0:-1,0",
            "mouse 0:0,0:-1,0",
        ], link.Sent);
    }

    [Fact]
    public void Scrolling_right_turns_the_sideways_wheel_the_devices_way()
    {
        var (device, link) = Linked();

        device.Scroll("right", 120, 0, 0);

        Assert.Equal(["mouse 0:0,0:0,1"], link.Sent);
    }

    [Fact]
    public void A_drag_holds_the_button_from_the_first_point_to_the_last()
    {
        var (device, link) = Linked();

        device.Drag("left", 10, 0, 30, 0, 0, 2);

        Assert.Equal(
        [
            "mouse 0:10,0:0,0", // the pointer reaches the first point
            "mouse 1:0,0:0,0",  // and the button goes down
            "mouse 1:10,0:0,0", // the drag travels
            "mouse 1:10,0:0,0",
            "mouse 0:0,0:0,0",  // and lets go on the last one
        ], link.Sent);

        Assert.Equal(new ScreenPoint(30, 0), link.Cursor);
    }

    [Fact]
    public void A_move_that_takes_time_is_walked_in_several_reports()
    {
        var (device, link) = Linked();

        device.MoveMouse(200, 0, 160);

        Assert.True(link.Sent.Count > 1, "a move over time is one report per stop");
        Assert.Equal(new ScreenPoint(200, 0), link.Cursor);
    }

    [Fact]
    public void The_pointer_is_where_the_machine_says_it_is()
    {
        var (device, _) = Linked(7, 8);

        Assert.Equal(new ScreenPoint(7, 8), device.Cursor);
    }

    [Fact]
    public void WhaleGenie_recognises_its_own_driver_level_input()
    {
        var (device, _) = Linked();

        device.KeyDown("F9");

        // Driver-level input arrives at the machine as real hardware, so it comes back through the
        // hook the triggers listen on; without this the macro that sent it would set itself off.
        Assert.True(WhaleGenieInputGate.RecentlySent("F9"));
    }

    [Fact]
    public void A_device_the_machine_never_notices_is_reported_rather_than_typed_into_nothing()
    {
        // A VIIPER server started without its auto-attach flag leaves a device on a bus that the
        // machine never sees. Nothing would come of sending into it, so this has to fail instead.
        using var device = new ViiperInputDevice(() => new DeafLink());

        var failure = Assert.Throws<DeviceActionException>(() => device.KeyDown("A"));

        Assert.Equal("Run.NoDriverAttached", failure.Key);
    }

    [Fact]
    public void A_device_the_machine_has_not_noticed_yet_is_waited_for()
    {
        // A device that has just been plugged in is not polled for a moment, so what it sends in
        // that moment goes nowhere. Waiting is the whole point of the nudge, and it is undone so
        // that opening a connection does not move the pointer.
        var link = new FakeLink(10, 20, deaf: 5);
        using var device = new ViiperInputDevice(() => link);

        Assert.Equal(new ScreenPoint(10, 20), device.Cursor);
        Assert.Equal(new ScreenPoint(10, 20), link.Cursor);
    }

    [Fact]
    public void A_move_to_a_point_the_machine_will_not_allow_leaves_the_pointer_where_it_can()
    {
        // The machine keeps the pointer on the screen, so a macro that asks for a point past the edge
        // of it lands on the edge and carries on — which is what the same move does when it goes
        // through the front device, rather than failing the step.
        using var device = new ViiperInputDevice(() => new FakeLink(0, 0, limitX: 100));

        device.MoveMouse(500, 0, 0);

        Assert.Equal(new ScreenPoint(100, 0), device.Cursor);
    }

    private static (ViiperInputDevice Device, FakeLink Link) Linked(int x = 0, int y = 0)
    {
        var link = new FakeLink(x, y);
        var device = new ViiperInputDevice(() => link);

        // Asking where the pointer is opens the connection and waits for the device to be listened
        // to, which is a nudge and its undo. The checks are about what comes after that.
        _ = device.Cursor;
        link.Sent.Clear();

        return (device, link);
    }

    [Fact]
    public void A_button_brings_the_controller_up_that_games_look_for()
    {
        var link = new FakeLink(0, 0);
        using var device = new ViiperInputDevice(() => link);

        device.Button("a", true);

        Assert.Equal(1, link.Gamepads);
        Assert.Equal("gamepad A:0,0:0,0:0,0", link.Sent[^1]);
    }

    [Fact]
    public void Asking_for_the_controller_puts_it_on_the_machine_once()
    {
        var link = new FakeLink(0, 0);
        using var device = new ViiperInputDevice(() => link);

        device.Connect();
        device.Connect();

        Assert.Equal(1, link.Gamepads);
    }

    [Fact]
    public void A_trigger_named_as_a_button_is_pulled_all_the_way_down()
    {
        var link = new FakeLink(0, 0);
        using var device = new ViiperInputDevice(() => link);

        device.Button("lt", true);
        Assert.Equal("gamepad LeftTrigger:100,0:0,0:0,0", link.Sent[^1]);

        device.Button("lt", false);
        Assert.Equal("gamepad None:0,0:0,0:0,0", link.Sent[^1]);
    }

    [Fact]
    public void A_stick_and_a_trigger_stay_where_they_were_put()
    {
        var link = new FakeLink(0, 0);
        using var device = new ViiperInputDevice(() => link);

        device.Stick("left", 50, 100);
        device.Trigger("right", 40);

        // A controller report is the whole picture, so the stick is still pushed in the report
        // that only changed the trigger.
        Assert.Equal("gamepad None:0,40:50,100:0,0", link.Sent[^1]);
    }

    [Fact]
    public void Letting_go_of_a_controller_puts_everything_back()
    {
        var link = new FakeLink(0, 0);
        using var device = new ViiperInputDevice(() => link);

        device.Button("b", true);
        device.Stick("right", -100, -50);
        device.ReleaseAll();

        Assert.Equal("gamepad None:0,0:0,0:0,0", link.Sent[^1]);
    }

    [Fact]
    public void A_control_no_controller_has_is_refused()
    {
        using var device = new ViiperInputDevice(() => new FakeLink(0, 0));

        Assert.Throws<DeviceActionException>(() => device.Button("turbo", true));
        Assert.Throws<DeviceActionException>(() => device.Stick("middle", 0, 0));
        Assert.Throws<DeviceActionException>(() => device.Trigger("both", 0));
    }

    /// <summary>
    /// A link that writes every report down instead of sending it, and moves its own idea of the
    /// pointer by what the reports say — which is what the machine on the other end does. It can be
    /// told to be deaf for a while, the way a device the machine has not noticed yet is, and to keep
    /// the pointer inside a limit, the way a screen edge does.
    /// </summary>
    private sealed class FakeLink(int x, int y, int deaf = 0, int? limitX = null) : IViiperLink
    {
        public List<string> Sent { get; } = [];

        /// <summary>How many times a controller was put on the bus.</summary>
        public int Gamepads { get; private set; }

        public ScreenPoint Cursor { get; private set; } = new(x, y);

        public void ConnectGamepad() => Gamepads++;

        public void SendGamepad(GamepadState state)
            => Sent.Add($"gamepad {state.Buttons}:{state.LeftTrigger},{state.RightTrigger}"
                + $":{state.LeftX},{state.LeftY}:{state.RightX},{state.RightY}");

        public void SendKeyboard(byte modifiers, IReadOnlyList<byte> keys)
            => Sent.Add($"keyboard {modifiers}:{string.Join(".", keys)}");

        public void SendMouse(byte buttons, short dx, short dy, short wheel, short pan)
        {
            Sent.Add($"mouse {buttons}:{dx},{dy}:{wheel},{pan}");

            if (deaf > 0)
            {
                deaf--;
                return;
            }

            Cursor = new ScreenPoint(Cursor.X + dx, Cursor.Y + dy);
            if (limitX is { } bound && Cursor.X > bound)
            {
                Cursor = new ScreenPoint(bound, Cursor.Y);
            }
        }

        public void Dispose()
        {
        }
    }

    /// <summary>A link the machine never answers, the way a device that was never attached behaves.</summary>
    private sealed class DeafLink : IViiperLink
    {
        public ScreenPoint Cursor { get; } = new(0, 0);

        public void ConnectGamepad()
        {
        }

        public void SendGamepad(GamepadState state)
        {
        }

        public void SendKeyboard(byte modifiers, IReadOnlyList<byte> keys)
        {
        }

        public void SendMouse(byte buttons, short dx, short dy, short wheel, short pan)
        {
        }

        public void Dispose()
        {
        }
    }
}
