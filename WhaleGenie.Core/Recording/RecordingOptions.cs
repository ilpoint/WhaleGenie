namespace WhaleGenie.Core.Recording;

/// <summary>
/// The knobs the "Record Actions" panel turns. The thresholds keep a recording readable:
/// a pointer that never stops moving would otherwise write a step for every pixel, and a
/// pause of a few milliseconds would be written as a wait nobody meant to ask for.
/// </summary>
public sealed record RecordingOptions
{
    /// <summary>Write the pointer's movement as steps of its own.</summary>
    public bool MouseMovement { get; init; } = true;

    /// <summary>Write mouse presses, releases and clicks.</summary>
    public bool MouseButtons { get; init; } = true;

    /// <summary>Write keys, held keys and chords.</summary>
    public bool KeyboardKeys { get; init; } = true;

    /// <summary>Write pauses between the captured actions as delay steps.</summary>
    public bool Delays { get; init; } = true;

    /// <summary>Pauses shorter than this are treated as part of the action, not as a wait.</summary>
    public int IgnoreDelaysBelowMs { get; init; } = 50;

    /// <summary>Write the pointer as movement relative to where it is rather than where it is.</summary>
    public bool RelativeMovement { get; init; }

    /// <summary>Shortest gap between two recorded pointer positions.</summary>
    public int MouseMoveIntervalMs { get; init; } = 60;

    /// <summary>Shortest gap between two positions recorded while a button is held.</summary>
    public int DragMoveIntervalMs { get; init; } = 25;

    /// <summary>How far the pointer may wander and still count as pressing in one place.</summary>
    public int DragTolerance { get; init; } = 6;

    /// <summary>Longest a press may last and still be recorded as a click.</summary>
    public int ClickMaxHoldMs { get; init; } = 350;

    /// <summary>Longest gap between two clicks of one button that counts as a double click.</summary>
    public int DoubleClickMaxGapMs { get; init; } = 400;

    /// <summary>How far a double click's two halves may be apart.</summary>
    public int DoubleClickMaxDistance { get; init; } = 6;

    /// <summary>Wheel turns of the same direction this close together become one step.</summary>
    public int ScrollCoalesceMs { get; init; } = 400;

    /// <summary>Longest a key may be held and still be recorded as a single press.</summary>
    public int KeyTapMaxHoldMs { get; init; } = 700;
}
