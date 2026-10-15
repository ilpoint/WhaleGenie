using System;
using System.Collections.Generic;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Chooses how one step's input reaches the machine. The front device is the desktop's own input;
/// the background device posts messages at one window; the driver device sends it through a
/// virtual USB device.
/// </summary>
internal sealed class WindowsInputRouter(IInputDevice front, IScreenDevice screen)
    : IInputRouter, IDisposable
{
    private readonly object _gate = new();

    private readonly Dictionary<long, MessageInputDevice> _posting = [];

    private ViiperInputDevice? _driver;

    public IInputDevice For(InputRoute route) => route.Delivery switch
    {
        InputDelivery.Background => Posting(route.WindowHandle),
        InputDelivery.Driver => Driver,
        _ => front,
    };

    /// <summary>
    /// The virtual keyboard and mouse, asked for once and kept between steps: a macro that sends
    /// input step after step should reach for the devices once. The pair itself belongs to the whole
    /// program and stays on the machine — see <see cref="SharedDriverInput"/>.
    /// </summary>
    private ViiperInputDevice Driver
    {
        get
        {
            lock (_gate)
            {
                return _driver ??= SharedDriverInput.Take(screen);
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

    /// <summary>
    /// Lets go of what this run reached for. The virtual keyboard and mouse are not given back:
    /// they belong to the program and stay on the machine until it is done with them.
    /// </summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _driver = null;
        }
    }
}
