namespace WhaleGenie.Core.Devices;

/// <summary>
/// One key, as the hook reports it and as a trigger binding and a recording name it.
/// </summary>
/// <remarks>
/// What is inside means something only in this process: it is how the platform layer spells a key.
/// It is kept behind a type of our own so that the triggers, the pickers and the recorder can say
/// which key went down without knowing which library read the keyboard — the library can be
/// swapped for another one without any of them noticing. Two of these compare, a set of them
/// works, and <see cref="KeyNames"/> says what to call one.
/// </remarks>
public readonly record struct HookKey
{
    internal HookKey(int value) => Value = value;

    internal int Value { get; }
}

/// <summary>
/// One of the mouse's buttons. Every button the hook reports is named here, which is also what
/// keeps a button nobody named from being taken for the left one.
/// </summary>
public enum HookMouseButton
{
    Left,
    Right,
    Middle,
    Back,
    Forward,
}

/// <summary>Which of the wheel's two ways round was turned.</summary>
public enum HookWheelAxis
{
    Vertical,
    Horizontal,
}
