using System;
using System.Collections.Generic;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The way driver-level reports reach the machine: a virtual keyboard and a mouse on the bus of the
/// USB server this program runs in itself, which the machine takes for hardware that is really there.
/// </summary>
/// <remarks>
/// Kept behind an interface so the device's own bookkeeping — which keys are held, how far the
/// pointer still has to go — can be checked without any of those devices existing.
/// </remarks>
public interface IViiperLink : IDisposable
{
    /// <summary>Where the pointer is on this machine right now.</summary>
    ScreenPoint Cursor { get; }

    /// <summary>
    /// Sends one keyboard report: the whole picture of what is held down, not what changed, because
    /// that is what a keyboard sends. The keys are HID usage codes; the modifiers travel in their
    /// own byte.
    /// </summary>
    void SendKeyboard(byte modifiers, IReadOnlyList<byte> keys);

    /// <summary>
    /// Sends one mouse report. The movement and the wheel are how far to go, not where to end up,
    /// and the buttons stay as they are until the next report says otherwise.
    /// </summary>
    void SendMouse(byte buttons, short dx, short dy, short wheel, short pan);

    /// <summary>
    /// Puts the virtual controller on the machine. Asking for one that is already there changes
    /// nothing.
    /// </summary>
    void ConnectGamepad();

    /// <summary>
    /// Sends the whole picture of the connected controller, which is what a controller report is:
    /// every button and axis together rather than what changed since last time.
    /// </summary>
    void SendGamepad(GamepadState state);
}

/// <summary>
/// A real keyboard and mouse on this machine's virtual USB bus. The driver the machine needs is
/// still the user's to install — WhaleGenie only says where it comes from — but the server and the
/// devices come with the program: there is no second program to have running, and nothing to point
/// at before driver-level input works.
/// </summary>
public sealed class ViiperLink : IViiperLink
{
    private readonly ViiperUsbServer _server;
    private readonly IntPtr _keyboard;
    private readonly IntPtr _mouse;

    /// <summary>
    /// The screen the pointer is read from. The virtual mouse moves the machine's own pointer, so
    /// where it is is a fact about the screen rather than about this link, and the answer comes from
    /// the same place every other device gets it from.
    /// </summary>
    private readonly IScreenDevice _screen;

    /// <summary>The controller, once a macro has asked for one.</summary>
    private IntPtr _gamepad;

    private ViiperLink(ViiperUsbServer server, IntPtr keyboard, IntPtr mouse, IScreenDevice screen)
    {
        _server = server;
        _keyboard = keyboard;
        _mouse = mouse;
        _screen = screen;
    }

    public ScreenPoint Cursor => _screen.Cursor;

    public void SendKeyboard(byte modifiers, IReadOnlyList<byte> keys)
    {
        var state = new LibViiper.KeyboardDeviceState
        {
            Modifiers = modifiers,
            Keys = new byte[32],
        };

        foreach (var key in keys)
        {
            // A report has one bit per HID usage code: the byte it lands in, and the bit within it.
            state.Keys[key / 8] |= (byte)(1 << (key % 8));
        }

        Sent(_server.SetKeyboard(_keyboard, state));
    }

    public void SendMouse(byte buttons, short dx, short dy, short wheel, short pan)
        => Sent(_server.SetMouse(_mouse, new LibViiper.MouseDeviceState
        {
            Buttons = buttons,
            Dx = dx,
            Dy = dy,
            Wheel = wheel,
            Pan = pan,
        }));

    public void ConnectGamepad()
    {
        if (_gamepad != IntPtr.Zero)
        {
            return;
        }

        // The controller goes on the machine when a macro asks for one rather than with the
        // keyboard and mouse: a macro that only clicks and types never puts a pad on the machine.
        _gamepad = _server.AddXbox360();
        if (_gamepad == IntPtr.Zero)
        {
            throw new DeviceActionException(RunKeys.NoDriverServer);
        }
    }

    public void SendGamepad(GamepadState state)
    {
        if (_gamepad == IntPtr.Zero)
        {
            throw new DeviceActionException(RunKeys.NoGamepad);
        }

        Sent(_server.SetXbox360(_gamepad, ForXbox360(state)));
    }

    /// <summary>
    /// Opens a keyboard and a mouse on the machine's virtual USB bus.
    ///
    /// The server is brought up here rather than when the program starts, so a machine that never
    /// asks for driver-level input never has one; everything after that shares it.
    /// </summary>
    public static IViiperLink Open(IScreenDevice screen)
    {
        var state = DriverInput.Check();
        switch (state)
        {
            case DriverInputState.DriverMissing:
                throw new DeviceActionException(RunKeys.NoDriver);
            case DriverInputState.LibraryMissing:
                throw new DeviceActionException(RunKeys.NoDriverLibrary);
        }

        ViiperUsbServer? server;
        try
        {
            server = ViiperServer.Ensure();
        }
        catch (Exception failure) when (failure is DllNotFoundException
            or BadImageFormatException or EntryPointNotFoundException)
        {
            // The file is where it should be and Windows will not have it: an install that was cut
            // short, or a library of another shape. The run cannot do anything about that either.
            throw new DeviceActionException(RunKeys.NoDriverLibrary);
        }

        if (server is null)
        {
            throw new DeviceActionException(RunKeys.NoDriverServer);
        }

        var keyboard = server.AddKeyboard();
        var mouse = server.AddMouse();
        if (keyboard == IntPtr.Zero || mouse == IntPtr.Zero)
        {
            // Half a pair on the machine is worse than none: the machine answers the pair that got
            // there first, and it would be answering this one.
            if (keyboard != IntPtr.Zero)
            {
                server.RemoveKeyboard(keyboard);
            }

            if (mouse != IntPtr.Zero)
            {
                server.RemoveMouse(mouse);
            }

            throw new DeviceActionException(RunKeys.NoDriverServer);
        }

        return new ViiperLink(server, keyboard, mouse, screen);
    }

    /// <summary>
    /// Takes the three devices off the machine. The server stays: it belongs to the program, and
    /// the next link puts its own devices on the same bus.
    /// </summary>
    public void Dispose()
    {
        _server.RemoveKeyboard(_keyboard);
        _server.RemoveMouse(_mouse);

        if (_gamepad != IntPtr.Zero)
        {
            _server.RemoveXbox360(_gamepad);
            _gamepad = IntPtr.Zero;
        }
    }

    /// <summary>
    /// What the library answers when a report is sent: false means the device is not there any
    /// more, so the input did not happen and the step has to hear about it rather than carry on as
    /// if it had.
    /// </summary>
    private static void Sent(bool sent)
    {
        if (!sent)
        {
            throw new DeviceActionException(RunKeys.NoDriverAttached);
        }
    }

    /// <summary>The controller state written the way an Xbox 360 pad reports it.</summary>
    private static LibViiper.Xbox360DeviceState ForXbox360(GamepadState state)
    {
        var held = state.Buttons;
        var buttons = 0u;

        if ((held & GamepadButtons.A) != 0) buttons |= LibViiper.PadA;
        if ((held & GamepadButtons.B) != 0) buttons |= LibViiper.PadB;
        if ((held & GamepadButtons.X) != 0) buttons |= LibViiper.PadX;
        if ((held & GamepadButtons.Y) != 0) buttons |= LibViiper.PadY;
        if ((held & GamepadButtons.LeftBumper) != 0) buttons |= LibViiper.PadLeftShoulder;
        if ((held & GamepadButtons.RightBumper) != 0) buttons |= LibViiper.PadRightShoulder;
        if ((held & GamepadButtons.LeftStick) != 0) buttons |= LibViiper.PadLeftThumb;
        if ((held & GamepadButtons.RightStick) != 0) buttons |= LibViiper.PadRightThumb;
        if ((held & GamepadButtons.Up) != 0) buttons |= LibViiper.PadUp;
        if ((held & GamepadButtons.Down) != 0) buttons |= LibViiper.PadDown;
        if ((held & GamepadButtons.Left) != 0) buttons |= LibViiper.PadLeft;
        if ((held & GamepadButtons.Right) != 0) buttons |= LibViiper.PadRight;
        if ((held & GamepadButtons.Start) != 0) buttons |= LibViiper.PadStart;
        if ((held & GamepadButtons.Back) != 0) buttons |= LibViiper.PadBack;
        if ((held & GamepadButtons.Guide) != 0) buttons |= LibViiper.PadGuide;

        return new LibViiper.Xbox360DeviceState
        {
            Buttons = buttons,
            LeftTrigger = Ample(state.LeftTrigger),
            RightTrigger = Ample(state.RightTrigger),
            LeftX = Lean(state.LeftX),
            LeftY = Lean(state.LeftY),
            RightX = Lean(state.RightX),
            RightY = Lean(state.RightY),
            Reserved = new byte[6],
        };
    }

    /// <summary>A trigger, from untouched to pulled all the way, as a byte.</summary>
    private static byte Ample(int percent) => (byte)(Math.Clamp(percent, 0, 100) * 255 / 100);

    /// <summary>A stick axis, from the centre, in the range this pad reports.</summary>
    private static short Lean(int percent) => (short)(Math.Clamp(percent, -100, 100) * 32767 / 100);
}
