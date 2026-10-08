using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Viiper.Client;
using Viiper.Client.Devices.Keyboard;
using Viiper.Client.Devices.Mouse;
using Viiper.Client.Types;
using Ds4 = Viiper.Client.Devices.Dualshock4;
using Dse = Viiper.Client.Devices.Dualsense;
using Xbox = Viiper.Client.Devices.Xbox360;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The way driver-level reports reach the machine: one connection to the VIIPER server with a
/// keyboard and a mouse on it, which the machine takes for hardware that is really there.
/// </summary>
/// <remarks>
/// Kept behind an interface so the device's own bookkeeping — which keys are held, how far the
/// pointer still has to go — can be checked without a server to send anything to.
/// </remarks>
public interface IViiperLink : IDisposable
{
    /// <summary>Where the pointer is on this machine right now.</summary>
    ScreenPoint Cursor { get; }

    /// <summary>
    /// Sends one keyboard report: the whole picture of what is held down, not what changed,
    /// because that is what a keyboard sends.
    /// </summary>
    void SendKeyboard(byte modifiers, IReadOnlyList<byte> keys);

    /// <summary>
    /// Sends one mouse report. The movement and the wheel are how far to go, not where to end up,
    /// and the buttons stay as they are until the next report says otherwise.
    /// </summary>
    void SendMouse(byte buttons, short dx, short dy, short wheel, short pan);

    /// <summary>
    /// Puts a virtual controller of this kind on the bus, taking down a controller of another kind
    /// that was there. Asking for the kind that is already there changes nothing.
    /// </summary>
    void ConnectGamepad(GamepadKind kind);

    /// <summary>
    /// Sends the whole picture of the connected controller, which is what a controller report is:
    /// every button and axis together rather than what changed since last time.
    /// </summary>
    void SendGamepad(GamepadState state);
}

/// <summary>
/// A real connection to the VIIPER server running on this machine: a bus with a virtual keyboard
/// and a virtual mouse on it. Neither the driver nor the server is WhaleGenie's to install or start,
/// so when one of them is missing the failure says which.
/// </summary>
public sealed class ViiperLink : IViiperLink
{
    /// <summary>What the server calls one keyboard and one mouse.</summary>
    private const string Keyboard = "keyboard";

    private const string Mouse = "mouse";

    /// <summary>The names the server knows the controllers by.</summary>
    private const string Xbox360Type = "xbox360";

    private const string DualShock4Type = "dualshock4";

    private const string DualSenseType = "dualsense";

    private readonly ViiperClient _client;
    private readonly ViiperDevice _keyboard;
    private readonly ViiperDevice _mouse;
    private readonly uint _bus;
    private readonly string _keyboardId;
    private readonly string _mouseId;
    private ViiperDevice? _gamepad;
    private string _gamepadId = string.Empty;
    private GamepadKind? _gamepadKind;

    private ViiperLink(ViiperClient client, uint bus, string keyboardId, ViiperDevice keyboard,
        string mouseId, ViiperDevice mouse)
    {
        _client = client;
        _bus = bus;
        _keyboardId = keyboardId;
        _keyboard = keyboard;
        _mouseId = mouseId;
        _mouse = mouse;
    }

    public ScreenPoint Cursor => WindowsScreenDevice.CursorPosition();

    public void SendKeyboard(byte modifiers, IReadOnlyList<byte> keys)
        => Wait(() => _keyboard.SendAsync(new KeyboardInput
        {
            Modifiers = modifiers,
            Count = (byte)keys.Count,
            Keys = [.. keys],
        }));

    public void SendMouse(byte buttons, short dx, short dy, short wheel, short pan)
        => Wait(() => _mouse.SendAsync(new MouseInput
        {
            Buttons = buttons,
            Dx = dx,
            Dy = dy,
            Wheel = wheel,
            Pan = pan,
        }));

    public void ConnectGamepad(GamepadKind kind)
    {
        if (_gamepadKind == kind)
        {
            return;
        }

        // Only one controller is kept: a second one of another kind would sit on the same bus and
        // Windows would hand the first game it found whichever it noticed first.
        if (_gamepad is not null)
        {
            Do(() => _gamepad.Dispose());
            Remove(_gamepadId);
            _gamepad = null;
            _gamepadKind = null;
        }

        var added = Wait(() => _client.BusDeviceAddAsync(_bus,
            new DeviceCreateRequest { Type = ServerType(kind) }));

        _gamepadId = added.DevID;
        _gamepad = Wait(() => _client.ConnectDeviceAsync(_bus, added.DevID));
        _gamepadKind = kind;
    }

    public void SendGamepad(GamepadState state)
    {
        if (_gamepad is not { } gamepad || _gamepadKind is not { } kind)
        {
            throw new DeviceActionException("Run.NoGamepad");
        }

        switch (kind)
        {
            case GamepadKind.Xbox360:
                Wait(() => gamepad.SendAsync(ForXbox360(state)));
                break;
            case GamepadKind.DualShock4:
                Wait(() => gamepad.SendAsync(ForDualShock4(state)));
                break;
            case GamepadKind.DualSense:
                Wait(() => gamepad.SendAsync(ForDualSense(state)));
                break;
        }
    }

    /// <summary>
    /// Opens a keyboard and a mouse on the server. The devices go on the bus that is already there
    /// rather than a new one every time, so a machine that runs macros all day does not collect
    /// buses it never removed.
    /// </summary>
    public static IViiperLink Open()
    {
        var state = DriverInput.Check();

        // A server that is not there is the one piece of this that WhaleGenie can deal with by
        // itself: the settings window says where viiper.exe is, and this starts it. Asking the user
        // to have launched something first is what made driver-level input look broken.
        if (state == DriverInputState.ServerMissing && ViiperServer.Ensure())
        {
            state = DriverInputState.Ready;
        }

        switch (state)
        {
            case DriverInputState.DriverMissing:
                throw new DeviceActionException("Run.NoDriver");
            case DriverInputState.ServerMissing:
                throw new DeviceActionException("Run.NoDriverServer");
        }

        var client = new ViiperClient(DriverInput.ServerHost, DriverInput.ServerPort);
        try
        {
            var existing = Wait(() => client.BusListAsync()).Buses;
            var bus = existing.Length > 0
                ? existing[0]
                : Wait(() => client.BusCreateAsync(null)).BusID;

            var keyboard =
                Wait(() => client.BusDeviceAddAsync(bus, new DeviceCreateRequest { Type = Keyboard }));
            var mouse =
                Wait(() => client.BusDeviceAddAsync(bus, new DeviceCreateRequest { Type = Mouse }));

            return new ViiperLink(
                client,
                bus,
                keyboard.DevID,
                Wait(() => client.ConnectDeviceAsync(bus, keyboard.DevID)),
                mouse.DevID,
                Wait(() => client.ConnectDeviceAsync(bus, mouse.DevID)));
        }
        catch (Exception failure) when (failure is not DeviceActionException)
        {
            client.Dispose();
            throw new DeviceActionException("Run.NoDriverServer");
        }
    }

    /// <summary>
    /// Takes the two devices off the bus and closes the connection. The server would clear the
    /// devices up on its own once the streams are closed, but saying so is quicker and leaves
    /// nothing behind for the next run to wonder about.
    /// </summary>
    public void Dispose()
    {
        Do(() =>
        {
            _keyboard.Dispose();
            _mouse.Dispose();
            _gamepad?.Dispose();
        });

        Remove(_keyboardId);
        Remove(_mouseId);

        if (_gamepad is not null)
        {
            Remove(_gamepadId);
            _gamepad = null;
            _gamepadKind = null;
        }

        _client.Dispose();
    }

    /// <summary>
    /// Takes one device off the bus. The server would clear the devices up on its own once their
    /// streams are closed, so a removal that fails is left alone rather than raised.
    /// </summary>
    private void Remove(string device)
    {
        try
        {
            Wait(() => _client.BusDeviceRemoveAsync(_bus, device));
        }
        catch
        {
            // A device that cannot be taken off a server that is going away anyway is not worth
            // failing a run over.
        }
    }

    /// <summary>The name the server knows a controller by.</summary>
    private static string ServerType(GamepadKind kind) => kind switch
    {
        GamepadKind.DualShock4 => DualShock4Type,
        GamepadKind.DualSense => DualSenseType,
        _ => Xbox360Type,
    };

    /// <summary>The controller state written the way an Xbox 360 pad reports it.</summary>
    private static Xbox.Xbox360Input ForXbox360(GamepadState state)
    {
        var held = state.Buttons;
        var buttons = 0u;

        if ((held & GamepadButtons.A) != 0) buttons |= (uint)Xbox.Button.A;
        if ((held & GamepadButtons.B) != 0) buttons |= (uint)Xbox.Button.B;
        if ((held & GamepadButtons.X) != 0) buttons |= (uint)Xbox.Button.X;
        if ((held & GamepadButtons.Y) != 0) buttons |= (uint)Xbox.Button.Y;
        if ((held & GamepadButtons.LeftBumper) != 0) buttons |= (uint)Xbox.Button.LShoulder;
        if ((held & GamepadButtons.RightBumper) != 0) buttons |= (uint)Xbox.Button.RShoulder;
        if ((held & GamepadButtons.LeftStick) != 0) buttons |= (uint)Xbox.Button.LThumb;
        if ((held & GamepadButtons.RightStick) != 0) buttons |= (uint)Xbox.Button.RThumb;
        if ((held & GamepadButtons.Up) != 0) buttons |= (uint)Xbox.Button.DPadUp;
        if ((held & GamepadButtons.Down) != 0) buttons |= (uint)Xbox.Button.DPadDown;
        if ((held & GamepadButtons.Left) != 0) buttons |= (uint)Xbox.Button.DPadLeft;
        if ((held & GamepadButtons.Right) != 0) buttons |= (uint)Xbox.Button.DPadRight;
        if ((held & GamepadButtons.Start) != 0) buttons |= (uint)Xbox.Button.Start;
        if ((held & GamepadButtons.Back) != 0) buttons |= (uint)Xbox.Button.Back;
        if ((held & GamepadButtons.Guide) != 0) buttons |= (uint)Xbox.Button.Guide;

        return new Xbox.Xbox360Input
        {
            Buttons = buttons,
            Lt = Ample(state.LeftTrigger),
            Rt = Ample(state.RightTrigger),
            Lx = Lean(state.LeftX),
            Ly = Lean(state.LeftY),
            Rx = Lean(state.RightX),
            Ry = Lean(state.RightY),
            Reserved = new byte[6],
        };
    }

    /// <summary>
    /// The controller state written the way a DualShock 4 reports it. The stick's Y axis grows
    /// downwards on this pad where a person reads it upwards, so it is turned round here, and the
    /// d-pad travels in a nibble of its own rather than with the buttons.
    /// </summary>
    private static Ds4.Dualshock4Input ForDualShock4(GamepadState state)
    {
        var held = state.Buttons;
        ushort buttons = 0;

        if ((held & GamepadButtons.A) != 0) buttons |= (ushort)Ds4.Button.Cross;
        if ((held & GamepadButtons.B) != 0) buttons |= (ushort)Ds4.Button.Circle;
        if ((held & GamepadButtons.X) != 0) buttons |= (ushort)Ds4.Button.Square;
        if ((held & GamepadButtons.Y) != 0) buttons |= (ushort)Ds4.Button.Triangle;
        if ((held & GamepadButtons.LeftBumper) != 0) buttons |= (ushort)Ds4.Button.L1;
        if ((held & GamepadButtons.RightBumper) != 0) buttons |= (ushort)Ds4.Button.R1;
        if ((held & GamepadButtons.LeftTrigger) != 0) buttons |= (ushort)Ds4.Button.L2;
        if ((held & GamepadButtons.RightTrigger) != 0) buttons |= (ushort)Ds4.Button.R2;
        if ((held & GamepadButtons.LeftStick) != 0) buttons |= (ushort)Ds4.Button.L3;
        if ((held & GamepadButtons.RightStick) != 0) buttons |= (ushort)Ds4.Button.R3;
        if ((held & GamepadButtons.Start) != 0) buttons |= (ushort)Ds4.Button.Options;
        if ((held & GamepadButtons.Back) != 0) buttons |= (ushort)Ds4.Button.Share;
        if ((held & GamepadButtons.Guide) != 0) buttons |= (ushort)Ds4.Button.PS;

        return new Ds4.Dualshock4Input
        {
            Buttons = buttons,
            Dpad = DPad(held),
            Sticklx = Lean8(state.LeftX),
            Stickly = Lean8(-state.LeftY),
            Stickrx = Lean8(state.RightX),
            Stickry = Lean8(-state.RightY),
            Triggerl2 = Ample(state.LeftTrigger),
            Triggerr2 = Ample(state.RightTrigger),
            // The touch pad and the motion sensors are not driven by any step yet; a report still
            // has to say what they are doing, and "untouched, still" is what they are.
            Touch1x = 0,
            Touch1y = 0,
            Touch1active = 0,
            Touch2x = 0,
            Touch2y = 0,
            Touch2active = 0,
            Gyrox = 0,
            Gyroy = 0,
            Gyroz = 0,
            Accelx = 0,
            Accely = 0,
            Accelz = 0,
        };
    }

    /// <summary>The same, the way a DualSense reports it.</summary>
    private static Dse.DualsenseInput ForDualSense(GamepadState state)
    {
        var held = state.Buttons;
        var buttons = 0u;

        if ((held & GamepadButtons.A) != 0) buttons |= (uint)Dse.Button.Cross;
        if ((held & GamepadButtons.B) != 0) buttons |= (uint)Dse.Button.Circle;
        if ((held & GamepadButtons.X) != 0) buttons |= (uint)Dse.Button.Square;
        if ((held & GamepadButtons.Y) != 0) buttons |= (uint)Dse.Button.Triangle;
        if ((held & GamepadButtons.LeftBumper) != 0) buttons |= (uint)Dse.Button.L1;
        if ((held & GamepadButtons.RightBumper) != 0) buttons |= (uint)Dse.Button.R1;
        if ((held & GamepadButtons.LeftTrigger) != 0) buttons |= (uint)Dse.Button.L2;
        if ((held & GamepadButtons.RightTrigger) != 0) buttons |= (uint)Dse.Button.R2;
        if ((held & GamepadButtons.LeftStick) != 0) buttons |= (uint)Dse.Button.L3;
        if ((held & GamepadButtons.RightStick) != 0) buttons |= (uint)Dse.Button.R3;
        if ((held & GamepadButtons.Start) != 0) buttons |= (uint)Dse.Button.Options;
        if ((held & GamepadButtons.Back) != 0) buttons |= (uint)Dse.Button.Create;
        if ((held & GamepadButtons.Guide) != 0) buttons |= (uint)Dse.Button.PS;

        return new Dse.DualsenseInput
        {
            Buttons = buttons,
            Dpad = DPad(held),
            Sticklx = Lean8(state.LeftX),
            Stickly = Lean8(-state.LeftY),
            Stickrx = Lean8(state.RightX),
            Stickry = Lean8(-state.RightY),
            Triggerl2 = Ample(state.LeftTrigger),
            Triggerr2 = Ample(state.RightTrigger),
            Touch1x = 0,
            Touch1y = 0,
            Touch1active = 0,
            Touch2x = 0,
            Touch2y = 0,
            Touch2active = 0,
            Gyrox = 0,
            Gyroy = 0,
            Gyroz = 0,
            Accelx = 0,
            Accely = 0,
            Accelz = 0,
        };
    }

    /// <summary>
    /// The d-pad as a PlayStation pad reports it: one nibble, counting round from up, with 8
    /// meaning that nothing is pressed. A pair of neighbours reads as the corner between them.
    /// </summary>
    private static byte DPad(GamepadButtons held)
    {
        var up = (held & GamepadButtons.Up) != 0;
        var down = (held & GamepadButtons.Down) != 0;
        var left = (held & GamepadButtons.Left) != 0;
        var right = (held & GamepadButtons.Right) != 0;

        return (up, down, left, right) switch
        {
            (true, _, _, true) => 1,
            (_, true, _, true) => 3,
            (_, true, true, _) => 5,
            (true, _, true, _) => 7,
            (true, _, _, _) => 0,
            (_, _, _, true) => 2,
            (_, true, _, _) => 4,
            (_, _, true, _) => 6,
            _ => 8,
        };
    }

    /// <summary>A trigger, from untouched to pulled all the way, as a byte.</summary>
    private static byte Ample(int percent) => (byte)(Math.Clamp(percent, 0, 100) * 255 / 100);

    /// <summary>A stick axis, from the centre, as the wider of the two ranges controllers use.</summary>
    private static short Lean(int percent) => (short)(Math.Clamp(percent, -100, 100) * 32767 / 100);

    /// <summary>The same, as the narrower range a PlayStation pad uses.</summary>
    private static sbyte Lean8(int percent) => (sbyte)(Math.Clamp(percent, -100, 100) * 127 / 100);

    /// <summary>
    /// Waits for one request. Everything here is a step in a macro, which is a sequential affair
    /// that asks for results rather than for tasks, so the work is waited out rather than passed on
    /// — but it is waited out on a thread of the pool, because the client's own waiting comes back
    /// through whatever context the call was made on, and a run started from the editor happens on
    /// the thread that draws the window: block that one and the client would be waiting for work
    /// that only the blocked thread could run.
    /// </summary>
    private static T Wait<T>(Func<Task<T>> request) => Task.Run(request).GetAwaiter().GetResult();

    private static void Wait(Func<Task> request) => Task.Run(request).GetAwaiter().GetResult();

    /// <summary>
    /// The same again, for a call into the client that comes back on its own rather than as a task.
    /// </summary>
    private static void Do(Action work) => Task.Run(work).GetAwaiter().GetResult();
}
