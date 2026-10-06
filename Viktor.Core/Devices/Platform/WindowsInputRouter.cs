using System;
using System.Collections.Generic;

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// Chooses how one step's input reaches the machine. The front device is the desktop's own
/// input; the background device posts messages at one window. Driver-level input is not wired
/// up yet, so asking for it fails loudly rather than quietly doing something else.
/// </summary>
internal sealed class WindowsInputRouter(IInputDevice front) : IInputRouter
{
    private readonly Dictionary<long, MessageInputDevice> _posting = [];

    public IInputDevice For(InputRoute route) => route.Delivery switch
    {
        InputDelivery.Background => Posting(route.WindowHandle),
        InputDelivery.Driver => throw new DeviceUnavailableException("driver-level input"),
        _ => front,
    };

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
}
