using SharpHook;

namespace WhaleGenie.Core.Devices.Platform;

/// <summary>
/// Where the two vocabularies meet: what the hook library reports, turned into what WhaleGenie's
/// own event types say.
/// </summary>
/// <remarks>
/// Everything that listens to the hook is written in our own terms, so this is the only place that
/// has to know how the library spells what it saw. It is a file of its own rather than a handful of
/// lines inside the hook so that the turning can be checked without installing a hook.
/// </remarks>
internal static class HookTranslation
{
    /// <summary>A key going down.</summary>
    internal static KeyHookEvent KeyDown(KeyboardHookEventArgs e)
        => new(SharpKeys.Key(e.Data.KeyCode), e.IsEventSimulated);

    /// <summary>A key coming back up.</summary>
    internal static KeyHookEvent KeyUp(KeyboardHookEventArgs e)
        => new(SharpKeys.Key(e.Data.KeyCode), e.IsEventSimulated);

    /// <summary>
    /// A mouse button going down, or null for a button this program has no name for. Naming only
    /// the buttons a macro can be bound to is what keeps an unnamed one from being taken for the
    /// left button and firing something the user never asked for.
    /// </summary>
    internal static MouseHookEvent? MouseDown(MouseHookEventArgs e)
        => SharpKeys.Mouse(e.Data.Button) is { } button
            ? new MouseHookEvent(button, e.Data.X, e.Data.Y, e.IsEventSimulated)
            : null;

    /// <summary>A mouse button coming back up, or null for a button this program has no name for.</summary>
    internal static MouseHookEvent? MouseUp(MouseHookEventArgs e) => MouseDown(e);

    /// <summary>The pointer arriving at a place. A move while a button is held arrives here too.</summary>
    internal static MouseMovedHookEvent Moved(MouseHookEventArgs e)
        => new(e.Data.X, e.Data.Y, e.IsEventSimulated);

    /// <summary>
    /// The pointer moving by an amount. The library carries that amount where a place would
    /// otherwise go, which is the one thing about its shape worth writing down.
    /// </summary>
    internal static MouseMovedRelativeHookEvent MovedRelative(MouseHookEventArgs e)
        => new(e.Data.X, e.Data.Y, e.IsEventSimulated);

    /// <summary>The wheel turning.</summary>
    internal static WheelHookEvent Wheel(MouseWheelHookEventArgs e)
        => new(
            SharpKeys.Axis(e.Data.Direction),
            e.Data.Rotation,
            e.Data.Delta,
            e.Data.X,
            e.Data.Y,
            e.IsEventSimulated);

    /// <summary>
    /// Takes a handler's answer about swallowing the input back to the library. It has to happen
    /// while that event is still the one being reported, because that is when the library asks:
    /// this is what lets a picker take a click away from the window underneath it.
    /// </summary>
    internal static void Swallow(bool asked, HookEventArgs e)
    {
        if (asked)
        {
            e.SuppressEvent = true;
        }
    }
}
