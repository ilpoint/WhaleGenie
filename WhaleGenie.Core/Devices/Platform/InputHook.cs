using System;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// The keyboard and the mouse as the operating system sees them: what the triggers, the pickers
/// and the recorder all listen to.
/// </summary>
/// <remarks>
/// A handler runs on the hook's own thread, inside the hook, and the input carries on the moment it
/// returns. That is what lets a picker take a click away from the window underneath: setting
/// <c>Suppress</c> on the event it was handed reaches the operating system only while the handler
/// is still running. Anything slow therefore belongs on another thread. The hook itself belongs to
/// the process rather than to a subscriber — a part of the program subscribes while it wants the
/// events and unsubscribes when it is done.
/// </remarks>
public interface IInputHook
{
    /// <summary>A key going down.</summary>
    event Action<KeyHookEvent>? KeyDown;

    /// <summary>That key coming back up.</summary>
    event Action<KeyHookEvent>? KeyUp;

    /// <summary>A mouse button going down.</summary>
    event Action<MouseHookEvent>? MouseDown;

    /// <summary>That button coming back up.</summary>
    event Action<MouseHookEvent>? MouseUp;

    /// <summary>The pointer moving to a place, including while a button is held.</summary>
    event Action<MouseMovedHookEvent>? Moved;

    /// <summary>The pointer moving by an amount rather than to a place.</summary>
    event Action<MouseMovedRelativeHookEvent>? MovedRelative;

    /// <summary>The wheel turning.</summary>
    event Action<WheelHookEvent>? Wheel;
}

/// <summary>One key going down or coming back up.</summary>
public sealed class KeyHookEvent(HookKey key, bool injected)
{
    /// <summary>Which key.</summary>
    public HookKey Key { get; } = key;

    /// <summary>True when a program sent this rather than the person at the keyboard.</summary>
    public bool Injected { get; } = injected;

    /// <summary>Set while the handler runs to keep this input from reaching the rest of the system.</summary>
    public bool Suppress { get; set; }
}

/// <summary>One mouse button going down or coming back up, at the place the pointer was.</summary>
public sealed class MouseHookEvent(HookMouseButton button, int x, int y, bool injected)
{
    /// <summary>Which button.</summary>
    public HookMouseButton Button { get; } = button;

    /// <summary>Where the pointer was, across, in screen pixels.</summary>
    public int X { get; } = x;

    /// <summary>Where the pointer was, down, in screen pixels.</summary>
    public int Y { get; } = y;

    /// <summary>True when a program sent this rather than the person at the mouse.</summary>
    public bool Injected { get; } = injected;

    /// <summary>Set while the handler runs to keep this input from reaching the rest of the system.</summary>
    public bool Suppress { get; set; }
}

/// <summary>The pointer moving to a place, in screen pixels.</summary>
public sealed class MouseMovedHookEvent(int x, int y, bool injected)
{
    /// <summary>Where it arrived, across.</summary>
    public int X { get; } = x;

    /// <summary>Where it arrived, down.</summary>
    public int Y { get; } = y;

    /// <summary>True when a program sent this rather than the person at the mouse.</summary>
    public bool Injected { get; } = injected;
}

/// <summary>The pointer moving by an amount, which is all some devices and remote sessions say.</summary>
public sealed class MouseMovedRelativeHookEvent(int deltaX, int deltaY, bool injected)
{
    /// <summary>How far it moved across, in pixels; negative is leftwards.</summary>
    public int DeltaX { get; } = deltaX;

    /// <summary>How far it moved down, in pixels; negative is upwards.</summary>
    public int DeltaY { get; } = deltaY;

    /// <summary>True when a program sent this rather than the person at the mouse.</summary>
    public bool Injected { get; } = injected;
}

/// <summary>The wheel turning.</summary>
public sealed class WheelHookEvent(
    HookWheelAxis axis, int rotation, int delta, int x, int y, bool injected)
{
    /// <summary>Which way round the wheel is set: the usual up and down, or the sideways tilt.</summary>
    public HookWheelAxis Axis { get; } = axis;

    /// <summary>
    /// How far the wheel turned, in the units the operating system counts in: one notch is 120 and
    /// the sign says which way — up on the vertical wheel, left on the horizontal one.
    /// </summary>
    public int Rotation { get; } = rotation;

    /// <summary>
    /// The number of notches the machine reported instead, for machines that do not count a turn
    /// in units of 120; 0 when the turn is already counted above.
    /// </summary>
    public int Delta { get; } = delta;

    /// <summary>Where the pointer was, across, in screen pixels.</summary>
    public int X { get; } = x;

    /// <summary>Where the pointer was, down, in screen pixels.</summary>
    public int Y { get; } = y;

    /// <summary>True when a program sent this rather than the person at the mouse.</summary>
    public bool Injected { get; } = injected;

    /// <summary>Set while the handler runs to keep this input from reaching the rest of the system.</summary>
    public bool Suppress { get; set; }
}
