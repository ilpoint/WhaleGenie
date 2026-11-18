using System;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// What the engine asks of libVIIPER, one call at a time: exactly the calls that library exports,
/// with the shapes it exports them in.
/// </summary>
/// <remarks>
/// The library is a DLL of somebody else's and cannot be handed in from a check the way a device
/// can, so this is the seam instead: a check gives <see cref="ViiperServer.Native"/> something that
/// writes down what it was asked and answers as it likes, and every decision above — when the
/// server comes up, when the devices go on the bus, what a report looks like — can be read without
/// a driver, a virtual device or a real library on the machine.
///
/// A call that answers false is the library saying it could not do that; a handle of zero is a
/// device that was not created. Nothing here throws for an answer the library gives.
/// </remarks>
internal interface IViiperNative
{
    /// <summary>Starts a server on the address given, and answers its handle, or zero.</summary>
    IntPtr OpenServer(string address);

    bool CloseServer(IntPtr server);

    /// <summary>
    /// Creates a bus and fills in its ID, which is how the devices are found afterwards. Zero means
    /// "let the server give it a number".
    /// </summary>
    bool CreateBus(IntPtr server, ref uint busId);

    bool RemoveBus(IntPtr server, uint busId);

    /// <summary>
    /// Creates a keyboard and hands it back, attached to this machine when
    /// <paramref name="autoAttach"/> is set — which is what makes it a keyboard the desktop hears
    /// rather than a device sitting unclaimed on a bus.
    /// </summary>
    bool CreateKeyboard(IntPtr server, uint busId, bool autoAttach, out IntPtr keyboard);

    bool SetKeyboard(IntPtr keyboard, LibViiper.KeyboardDeviceState state);

    bool RemoveKeyboard(IntPtr keyboard);

    bool CreateMouse(IntPtr server, uint busId, bool autoAttach, out IntPtr mouse);

    bool SetMouse(IntPtr mouse, LibViiper.MouseDeviceState state);

    bool RemoveMouse(IntPtr mouse);

    bool CreateXbox360(IntPtr server, uint busId, bool autoAttach, out IntPtr pad);

    bool SetXbox360(IntPtr pad, LibViiper.Xbox360DeviceState state);

    bool RemoveXbox360(IntPtr pad);
}

/// <summary>The calls above, made into the library itself.</summary>
internal sealed class NativeViiper : IViiperNative
{
    public IntPtr OpenServer(string address)
    {
        var config = new LibViiper.USBServerConfig { Address = address };

        // No log callback: what the library has to say about itself is not worth a file of ours,
        // and the failures a step can act on come back as a false answer or a missing device.
        return LibViiper.NewUSBServer(ref config, out var server, IntPtr.Zero) ? server : IntPtr.Zero;
    }

    public bool CloseServer(IntPtr server) => LibViiper.CloseUSBServer(server);

    public bool CreateBus(IntPtr server, ref uint busId) => LibViiper.CreateUSBBus(server, ref busId);

    public bool RemoveBus(IntPtr server, uint busId) => LibViiper.RemoveUSBBus(server, busId);

    public bool CreateKeyboard(IntPtr server, uint busId, bool autoAttach, out IntPtr keyboard)
        => LibViiper.CreateKeyboardDevice(server, out keyboard, busId, autoAttach, 0, 0);

    public bool SetKeyboard(IntPtr keyboard, LibViiper.KeyboardDeviceState state)
        => LibViiper.SetKeyboardDeviceState(keyboard, state);

    public bool RemoveKeyboard(IntPtr keyboard) => LibViiper.RemoveKeyboardDevice(keyboard);

    public bool CreateMouse(IntPtr server, uint busId, bool autoAttach, out IntPtr mouse)
        => LibViiper.CreateMouseDevice(server, out mouse, busId, autoAttach, 0, 0);

    public bool SetMouse(IntPtr mouse, LibViiper.MouseDeviceState state)
        => LibViiper.SetMouseDeviceState(mouse, state);

    public bool RemoveMouse(IntPtr mouse) => LibViiper.RemoveMouseDevice(mouse);

    /// <summary>
    /// A pad of the ordinary kind: no vendor or product ID of our own, and no XInput subtype, which
    /// is the library's "a gamepad".
    /// </summary>
    public bool CreateXbox360(IntPtr server, uint busId, bool autoAttach, out IntPtr pad)
        => LibViiper.CreateXbox360Device(server, out pad, busId, autoAttach, 0, 0, 0);

    public bool SetXbox360(IntPtr pad, LibViiper.Xbox360DeviceState state)
        => LibViiper.SetXbox360DeviceState(pad, state);

    public bool RemoveXbox360(IntPtr pad) => LibViiper.RemoveXbox360Device(pad);
}
