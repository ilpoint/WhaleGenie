using System;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The server the library handed back, together with the one bus the devices go on.
///
/// The devices live on a bus that was already there rather than on a new one every time, so a
/// machine that runs macros all day does not collect buses it never removed.
/// </summary>
internal sealed class ViiperUsbServer
{
    /// <summary>One of the library's device calls, which is the same shape for all three devices.</summary>
    private delegate bool DeviceFactory(IntPtr server, uint bus, bool autoAttach, out IntPtr device);

    private readonly object _gate = new();

    private readonly IViiperNative _native;
    private readonly IntPtr _handle;

    private uint _bus;
    private bool _closed;

    private ViiperUsbServer(IViiperNative native, IntPtr handle, uint bus)
    {
        _native = native;
        _handle = handle;
        _bus = bus;
    }

    /// <summary>Brings a server up and puts one bus on it, or null when either of those fails.</summary>
    internal static ViiperUsbServer? Open(IViiperNative native)
    {
        var handle = native.OpenServer(ViiperServer.Address);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        // Zero is the library's "give it the next free number".
        var bus = 0u;
        if (!native.CreateBus(handle, ref bus))
        {
            native.CloseServer(handle);
            return null;
        }

        return new ViiperUsbServer(native, handle, bus);
    }

    /// <summary>Puts a virtual keyboard on the machine, or answers zero when it did not get on.</summary>
    internal IntPtr AddKeyboard() => Add(_native.CreateKeyboard);

    /// <summary>The same, for the mouse.</summary>
    internal IntPtr AddMouse() => Add(_native.CreateMouse);

    /// <summary>The same, for the controller.</summary>
    internal IntPtr AddXbox360() => Add(_native.CreateXbox360);

    /// <summary>
    /// Takes one device off the machine. The server would clear the devices up by itself once the
    /// server goes, but saying so is quicker and leaves nothing behind for the next run to wonder
    /// about; a removal that fails is not worth failing a run over.
    /// </summary>
    internal void RemoveKeyboard(IntPtr device) => Guarded(() => _native.RemoveKeyboard(device));

    /// <summary>The same, for the mouse.</summary>
    internal void RemoveMouse(IntPtr device) => Guarded(() => _native.RemoveMouse(device));

    /// <summary>The same, for the controller.</summary>
    internal void RemoveXbox360(IntPtr device) => Guarded(() => _native.RemoveXbox360(device));

    /// <summary>Sends one keyboard report, answering false when the server is gone.</summary>
    internal bool SetKeyboard(IntPtr device, LibViiper.KeyboardDeviceState state)
        => Guarded(() => _native.SetKeyboard(device, state));

    /// <summary>The same, for the mouse.</summary>
    internal bool SetMouse(IntPtr device, LibViiper.MouseDeviceState state)
        => Guarded(() => _native.SetMouse(device, state));

    /// <summary>The same, for the controller.</summary>
    internal bool SetXbox360(IntPtr device, LibViiper.Xbox360DeviceState state)
        => Guarded(() => _native.SetXbox360(device, state));

    /// <summary>Stops the server, which takes every device on it off the machine with it.</summary>
    internal void Close()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _native.CloseServer(_handle);
        }
    }

    /// <summary>
    /// Puts one device on the bus and attaches it to this machine, or answers zero.
    ///
    /// A device that cannot be created is tried once more on a fresh bus: the server takes an empty
    /// bus away after a few seconds, and a pair that was put on and taken off again leaves one
    /// behind. Creating a bus that is already there is what fails in that case, so the one we
    /// remember goes first.
    /// </summary>
    private IntPtr Add(DeviceFactory create)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return IntPtr.Zero;
            }

            if (create(_handle, _bus, true, out var device))
            {
                return device;
            }

            RebuildBus();
            return create(_handle, _bus, true, out device) ? device : IntPtr.Zero;
        }
    }

    /// <summary>
    /// Runs one call into the library with the server known to be there, or answers "no" without
    /// calling anything.
    ///
    /// The library keeps its handles in a table of its own on the Go side and panics — which takes
    /// the whole process with it, and nothing in managed code can catch that — when it is handed one
    /// that was already given back. Closing the server is what gives them back, and a run can still
    /// be sending at that moment, so the two are kept apart here: while the server is going, nothing
    /// else is inside the library.
    /// </summary>
    private bool Guarded(Func<bool> call)
    {
        lock (_gate)
        {
            return !_closed && call();
        }
    }

    private void RebuildBus()
    {
        _native.RemoveBus(_handle, _bus);

        var bus = 0u;
        if (_native.CreateBus(_handle, ref bus))
        {
            _bus = bus;
        }
    }
}
