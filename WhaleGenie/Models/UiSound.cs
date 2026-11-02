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
