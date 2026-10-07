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
    private readonly object _gate = new();

    private readonly Dictionary<long, MessageInputDevice> _posting = [];

    private ViiperInputDevice? _driver;

    private bool _tookDriver;

    public IInputDevice For(InputRoute route) => route.Delivery switch
    {
        InputDelivery.Background => Posting(route.WindowHandle),
        InputDelivery.Driver => Driver,
        _ => front,
    };

    /// <summary>
    /// The virtual keyboard and mouse, asked for once and kept between steps: putting them on the
    /// machine opens a connection and a pair of devices, and a macro that sends input step after
    /// step should pay for that once. The pair itself belongs to the whole program, because the
    /// machine only answers one — see <see cref="SharedDriverInput"/>.
    /// </summary>
    private ViiperInputDevice Driver
    {
        get
        {
            lock (_gate)
            {
                if (_driver is null)
                {
                    _driver = SharedDriverInput.Take();
                    _tookDriver = true;
                }

                return _driver;
            }
        }
    }

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

    /// <summary>Gives back the program's virtual keyboard and mouse, if this ever asked for them.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _driver = null;
            if (!_tookDriver)
            {
                return;
            }

            _tookDriver = false;
        }

        SharedDriverInput.Give();
    }
}
