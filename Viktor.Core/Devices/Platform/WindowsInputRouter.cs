using System;
using System.Collections.Generic;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// Chooses how one step's input reaches the machine. The front device is the desktop's own input;
/// the background device posts messages at one window; the driver device sends it through a
/// virtual USB device.
/// </summary>
internal sealed class WindowsInputRouter(IInputDevice front) : IInputRouter, IDisposable
{
    private readonly Dictionary<long, MessageInputDevice> _posting = [];

    private ViiperInputDevice? _driver;

    public IInputDevice For(InputRoute route) => route.Delivery switch
    {
        InputDelivery.Background => Posting(route.WindowHandle),
        InputDelivery.Driver => Driver,
        _ => front,
    };

    /// <summary>
    /// The virtual keyboard and mouse, made once and kept between steps: putting them on the
    /// machine opens a connection and a pair of devices, and a macro that sends input step after
    /// step should pay for that once.
    /// </summary>
    private ViiperInputDevice Driver => _driver ??= new ViiperInputDevice();

    /// <summary>
    /// One posting device per window, kept between steps: a macro that works on the same window
    /// over and over should not build a new device every time.
    /// </summary>
    private MessageInputDevice Posting(long window)
    {
        if (window == 0)
        {
            throw new DeviceActionException("Run.MissingTargetWindow");
        }

        if (!_posting.TryGetValue(window, out var device))
        {
            device = new MessageInputDevice((IntPtr)window, front);
            _posting[window] = device;
        }

        return device;
    }

    /// <summary>Takes the virtual devices back off the machine, if any were ever asked for.</summary>
    public void Dispose() => _driver?.Dispose();
}
