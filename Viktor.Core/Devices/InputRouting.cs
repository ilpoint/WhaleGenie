namespace Viktor.Core.Devices;

/// <summary>How a step's keyboard and mouse input should reach the machine.</summary>
public enum InputDelivery
{
    /// <summary>Straight at whatever window has the focus, the way a person's own input goes.</summary>
    Foreground,

    /// <summary>Posted to one window as messages, so that window does not need the focus.</summary>
    Background,

    /// <summary>Sent through a virtual USB device, so the machine sees real hardware.</summary>
    Driver,
}

/// <summary>Where one step wants its input to go.</summary>
public readonly record struct InputRoute(InputDelivery Delivery, long WindowHandle)
{
    /// <summary>Input straight to whatever window has the focus.</summary>
    public static InputRoute Front { get; } = new(InputDelivery.Foreground, 0);
}

/// <summary>Hands out the input device that delivers input the way a step asks for.</summary>
public interface IInputRouter
{
    /// <summary>The device that sends input along <paramref name="route"/>.</summary>
    IInputDevice For(InputRoute route);
}

/// <summary>
/// A router for a layer with only one way in: every route is answered with the same device.
/// Layers that can post messages or drive a virtual device answer with their own router.
/// </summary>
public sealed class SingleInputRouter(IInputDevice device) : IInputRouter
{
    public IInputDevice For(InputRoute route) => device;
}
