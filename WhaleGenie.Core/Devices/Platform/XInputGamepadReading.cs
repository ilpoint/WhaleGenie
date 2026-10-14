using System.Runtime.InteropServices;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The controller in the person's hands, read through XInput.
/// </summary>
/// <remarks>
/// Windows hands a controller's state over by being asked rather than by saying so, so this is a
/// reading and not a listener: whoever wants it asks, which is the trigger's timer, the editor while
/// a binding is being taken, and a recording in progress. Nothing is kept here, so the three of them
/// can read it from their own threads at once.
///
/// Four controllers are asked each time and their buttons are put together: a macro has no way to
/// say which controller pressed something, and the person at the machine knows which one is in their
/// hands. One round of all four costs around 0.04 ms, which is what makes reading it at the pace the
/// triggers are read at worth doing.
///
/// Only what a binding can be is read: the buttons, and each trigger once it is pulled past a little
/// of its travel. The sticks are left alone — nothing outside the virtual controller reads a stick
/// of the pad in someone's hands — and the Guide button is left alone because Windows keeps it to
/// itself and <c>XInputGetState</c> never reports it.
/// </remarks>
public sealed class XInputGamepadReading : IGamepadReading
{
    /// <summary>How many controllers XInput has slots for.</summary>
    private const int SlotCount = 4;

    /// <summary>
    /// Where a trigger counts as pulled, in XInput's own zero-to-255 range. A trigger is a range
    /// rather than a switch, so a binding needs one place where it counts as down; a tenth of the
    /// travel is past the slack a finger resting on it moves it, and well short of a deliberate pull.
    /// </summary>
    private const byte TriggerThreshold = 30;

    private const ushort DpadUp = 0x0001;
    private const ushort DpadDown = 0x0002;
    private const ushort DpadLeft = 0x0004;
    private const ushort DpadRight = 0x0008;
    private const ushort ButtonStart = 0x0010;
    private const ushort ButtonBack = 0x0020;
    private const ushort LeftThumb = 0x0040;
    private const ushort RightThumb = 0x0080;
    private const ushort LeftShoulder = 0x0100;
    private const ushort RightShoulder = 0x0200;
    private const ushort ButtonA = 0x1000;
    private const ushort ButtonB = 0x2000;
    private const ushort ButtonX = 0x4000;
    private const ushort ButtonY = 0x8000;

    public bool Connected
    {
        get
        {
            for (var slot = 0; slot < SlotCount; slot++)
            {
                if (State(slot, out _))
                {
                    return true;
                }
            }

            return false;
        }
    }

    public GamepadButtons Held()
    {
        var held = GamepadButtons.None;
        for (var slot = 0; slot < SlotCount; slot++)
        {
            if (!State(slot, out var state))
            {
                continue;
            }

            held |= Buttons(state.Buttons);

            if (state.LeftTrigger >= TriggerThreshold)
            {
                held |= GamepadButtons.LeftTrigger;
            }

            if (state.RightTrigger >= TriggerThreshold)
            {
                held |= GamepadButtons.RightTrigger;
            }
        }

        return held;
    }

    /// <summary>
    /// One controller's state, or false when that slot is empty. A machine without XInput at all —
    /// there is none this program runs on, but the call would throw rather than answer — reads as a
    /// machine with nothing plugged in, so a trigger bound to a controller button is simply never
    /// answered rather than taking the timer down with it.
    /// </summary>
    private static bool State(int slot, out XInputState state)
    {
        try
        {
            return XInputGetState((uint)slot, out state) == 0;
        }
        catch (DllNotFoundException)
        {
            state = default;
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            state = default;
            return false;
        }
    }

    private static GamepadButtons Buttons(ushort flags)
    {
        var buttons = GamepadButtons.None;

        if ((flags & ButtonA) != 0)
        {
            buttons |= GamepadButtons.A;
        }

        if ((flags & ButtonB) != 0)
        {
            buttons |= GamepadButtons.B;
        }

        if ((flags & ButtonX) != 0)
        {
            buttons |= GamepadButtons.X;
        }

        if ((flags & ButtonY) != 0)
        {
            buttons |= GamepadButtons.Y;
        }

        if ((flags & LeftShoulder) != 0)
        {
            buttons |= GamepadButtons.LeftBumper;
        }

        if ((flags & RightShoulder) != 0)
        {
            buttons |= GamepadButtons.RightBumper;
        }

        if ((flags & LeftThumb) != 0)
        {
            buttons |= GamepadButtons.LeftStick;
        }

        if ((flags & RightThumb) != 0)
        {
            buttons |= GamepadButtons.RightStick;
        }

        if ((flags & DpadUp) != 0)
        {
            buttons |= GamepadButtons.Up;
        }

        if ((flags & DpadDown) != 0)
        {
            buttons |= GamepadButtons.Down;
        }

        if ((flags & DpadLeft) != 0)
        {
            buttons |= GamepadButtons.Left;
        }

        if ((flags & DpadRight) != 0)
        {
            buttons |= GamepadButtons.Right;
        }

        if ((flags & ButtonStart) != 0)
        {
            buttons |= GamepadButtons.Start;
        }

        if ((flags & ButtonBack) != 0)
        {
            buttons |= GamepadButtons.Back;
        }

        return buttons;
    }

    /// <summary>
    /// What XInput fills in for one controller. Only the buttons and the two triggers are read; the
    /// rest is here because the call writes the whole structure.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint PacketNumber;
        public ushort Buttons;
        public byte LeftTrigger;
        public byte RightTrigger;
        public short LeftThumbX;
        public short LeftThumbY;
        public short RightThumbX;
        public short RightThumbY;
    }

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern uint XInputGetState(uint slot, out XInputState state);
}
