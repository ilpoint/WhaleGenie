namespace WhaleGenie.Models;

/// <summary>
/// One moment the interface says out loud that something changed. A person pressing the system
/// hotkey is often not looking at the window, and arming a macro from the list is a click with no
/// other proof that it landed.
/// </summary>
public enum UiSound
{
    /// <summary>The system switch stopped listening.</summary>
    SystemOff,

    /// <summary>The system switch is listening again.</summary>
    SystemOn,

    /// <summary>A macro was armed, so its triggers answer again.</summary>
    MacroOn,

    /// <summary>A macro was disarmed, so nothing will start it.</summary>
    MacroOff,
}

/// <summary>
/// What each of those moments sounds like: one tone of fixed pitch and length, made on the spot
/// rather than taken from the sound scheme. Two pairs, told apart by ear: the program's own switch
/// is the lower, longer pair, and one macro is the higher, shorter pair.
/// </summary>
public static class UiSounds
{
    /// <summary>The tone for this moment, in hertz and milliseconds.</summary>
    public static (int Frequency, int Milliseconds) Tone(UiSound which) => which switch
    {
        UiSound.SystemOn => (880, 120),
        UiSound.SystemOff => (440, 120),
        UiSound.MacroOn => (1320, 80),
        _ => (660, 80),
    };
}
