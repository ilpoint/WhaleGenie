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

    private readonly Dictionary<long, ThrottledInputDevice> _posting = [];

    private ViiperInputDevice? _driver;

    /// <summary>
    /// The driver device as one step sees it. Kept rather than made per call: a step that sends
    /// through the driver twice is sending through the same device, and a new wrapper each time
    /// would say otherwise to anything that compares them.
    /// </summary>
    private ThrottledInputDevice? _driverView;

    /// <summary>
    /// The device a step sends through: in front, one window, or the driver. The three ways share one
    /// input limit, so changing how a step sends does not change how fast it may send.
    /// </summary>
    public IInputDevice For(InputRoute route) => route.Delivery switch
    {
        InputDelivery.Background => Posting(route.WindowHandle),
        InputDelivery.Driver => DriverView,
        _ => front,
    };

    /// <summary>The driver device as a step sends through it, which is the one the limit counts.</summary>
    private ThrottledInputDevice DriverView
    {
        get
        {
            lock (_gate)
            {
                _driver ??= SharedDriverInput.Take(screen);
                return _driverView ??= (ThrottledInputDevice)ThrottledInputDevice.Of(_driver);
            }
        }
    }

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
    private ThrottledInputDevice Posting(long window)
    {
        if (window == 0)
        {
            throw new DeviceActionException(RunKeys.MissingTargetWindow);
        }

        if (!_posting.TryGetValue(window, out var device))
        {
            device = (ThrottledInputDevice)ThrottledInputDevice.Of(new MessageInputDevice((IntPtr)window, front));
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
            _driverView = null;
        }
    }
}
