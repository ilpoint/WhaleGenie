namespace Viktor.Core.Recording;

/// <summary>One action a recording is made of, ready to become a macro step.</summary>
public enum RecordedActionKind
{
    /// <summary>A pause; the length is in <see cref="RecordedAction.DurationMs"/>.</summary>
    Delay,

    /// <summary>A key pressed and let go again quickly.</summary>
    KeyPress,

    /// <summary>A key held down until a later Key Up.</summary>
    KeyDown,

    /// <summary>A key let go.</summary>
    KeyUp,

    /// <summary>Modifiers and a key pressed together, written as <c>Ctrl+C</c>.</summary>
    Hotkey,

    /// <summary>A press and release in one place.</summary>
    MouseClick,

    /// <summary>Two clicks of one button in quick succession.</summary>
    MouseDoubleClick,

    /// <summary>A button held down, for a drag or a long press.</summary>
    MouseDown,

    /// <summary>A button let go.</summary>
    MouseUp,

    /// <summary>The pointer moved to a screen position.</summary>
    MouseMove,

    /// <summary>The pointer moved by an offset, held in <see cref="RecordedAction.X"/> and <see cref="RecordedAction.Y"/>.</summary>
    MouseMoveRelative,

    /// <summary>The wheel turned.</summary>
    Scroll,
}

/// <summary>
/// A single action of a recording. Which fields matter depends on <see cref="Kind"/>;
/// the ones that do not apply keep their defaults.
/// </summary>
public sealed record RecordedAction(
    RecordedActionKind Kind,
    string Key = "",
    string Button = "left",
    string Direction = "down",
    int X = 0,
    int Y = 0,
    int Amount = 0,
    int DurationMs = 0);
