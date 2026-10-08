using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Viiper.Client;
using Viiper.Client.Devices.Keyboard;
using Viiper.Client.Devices.Mouse;
using Viiper.Client.Types;
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
    /// Puts the virtual controller on the bus. Asking for one that is already there changes
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
/// A real connection to the VIIPER server running on this machine: a bus with a virtual keyboard
/// and a virtual mouse on it. Neither the driver nor the server is WhaleGenie's to install or start,
/// so when one of them is missing the failure says which.
/// </summary>
public sealed class ViiperLink : IViiperLink
{
    /// <summary>What the server calls one keyboard and one mouse.</summary>
    private const string Keyboard = "keyboard";

    private const string Mouse = "mouse";

    /// <summary>The name the server knows the controller by.</summary>
    private const string Xbox360Type = "xbox360";

    private readonly ViiperClient _client;
    private readonly ViiperDevice _keyboard;
    private readonly ViiperDevice _mouse;
    private readonly uint _bus;
    private readonly string _keyboardId;
    private readonly string _mouseId;
    private ViiperDevice? _gamepad;
    private string _gamepadId = string.Empty;

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

    public void ConnectGamepad()
    {
        if (_gamepad is not null)
        {
            return;
        }

        var added = Wait(() => _client.BusDeviceAddAsync(_bus,
            new DeviceCreateRequest { Type = Xbox360Type }));

        _gamepadId = added.DevID;
        _gamepad = Wait(() => _client.ConnectDeviceAsync(_bus, added.DevID));
    }

    public void SendGamepad(GamepadState state)
    {
        if (_gamepad is not { } gamepad)
        {
            throw new DeviceActionException("Run.NoGamepad");
        }

        Wait(() => gamepad.SendAsync(ForXbox360(state)));
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

    /// <summary>A trigger, from untouched to pulled all the way, as a byte.</summary>
    private static byte Ample(int percent) => (byte)(Math.Clamp(percent, 0, 100) * 255 / 100);

    /// <summary>A stick axis, from the centre, in the range this pad reports.</summary>
    private static short Lean(int percent) => (short)(Math.Clamp(percent, -100, 100) * 32767 / 100);

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
