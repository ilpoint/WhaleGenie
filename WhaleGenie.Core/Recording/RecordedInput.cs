namespace WhaleGenie.Core.Recording;

/// <summary>What the user did, as the hook saw it.</summary>
public enum RecordedInputKind
{
    KeyDown,
    KeyUp,
    MouseDown,
    MouseUp,
    MouseMove,
    MouseScroll,
}

/// <summary>
/// One captured event, in the order the hook reported it. <see cref="At"/> counts
/// milliseconds from the moment recording started, so translating a recording never has to
/// look at the wall clock and can be replayed from a saved list.
/// </summary>
public readonly record struct RecordedInput(
    RecordedInputKind Kind,
    string Key,
    string Button,
    string Direction,
    int Amount,
    int X,
    int Y,
    long At)
{
    public static RecordedInput KeyDown(string key, long at)
        => new(RecordedInputKind.KeyDown, key, string.Empty, string.Empty, 0, 0, 0, at);

    public static RecordedInput KeyUp(string key, long at)
        => new(RecordedInputKind.KeyUp, key, string.Empty, string.Empty, 0, 0, 0, at);

    public static RecordedInput MouseDown(string button, int x, int y, long at)
        => new(RecordedInputKind.MouseDown, string.Empty, button, string.Empty, 0, x, y, at);

    public static RecordedInput MouseUp(string button, int x, int y, long at)
        => new(RecordedInputKind.MouseUp, string.Empty, button, string.Empty, 0, x, y, at);

    public static RecordedInput MouseMove(int x, int y, long at)
        => new(RecordedInputKind.MouseMove, string.Empty, string.Empty, string.Empty, 0, x, y, at);

    public static RecordedInput Scroll(string direction, int amount, int x, int y, long at)
        => new(RecordedInputKind.MouseScroll, string.Empty, string.Empty, direction, amount, x, y, at);
}

/// <summary>Tells the modifier keys apart from the keys a chord is built around.</summary>
public static class KeyRoles
{
    private static readonly System.Collections.Generic.HashSet<string> Modifiers =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Ctrl", "Control", "Shift", "Alt", "Win", "Windows", "Meta", "Super",
        };

    /// <summary>True for a key that is held down while another key is pressed.</summary>
    public static bool IsModifier(string? key) => key is not null && Modifiers.Contains(key.Trim());
}
