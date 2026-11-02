using System;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Models;

/// <summary>
/// One moment the interface says out loud that something changed. A person pressing the system key
/// is often not looking at the window, and arming a macro from the list is a click with no other
/// proof that it landed — these are the two moments a sound is worth more than a colour.
/// </summary>
public enum UiSound
{
    /// <summary>The system switch went from listening to not listening.</summary>
    SystemOff,

    /// <summary>The system switch is listening again.</summary>
    SystemOn,

    /// <summary>A macro was armed, so its triggers answer again.</summary>
    MacroOn,

    /// <summary>A macro was disarmed, so nothing will start it.</summary>
    MacroOff,
}

/// <summary>
/// What each of those moments plays when nothing else has been chosen, and how the choice is written
/// in the settings file. Every one of the four is a different sound: the pair about the whole program
/// has to be told apart from the pair about one macro without reading anything.
/// </summary>
public static class UiSounds
{
    public static SoundKind Default(UiSound which) => which switch
    {
        UiSound.SystemOn => SoundKind.Information,
        UiSound.SystemOff => SoundKind.Warning,
        UiSound.MacroOn => SoundKind.Default,
        _ => SoundKind.Question,
    };

    /// <summary>The sound written in the settings file, or that moment's default when it says nothing.</summary>
    public static SoundKind Read(string? written, UiSound which)
        => Enum.TryParse<SoundKind>(written?.Trim(), ignoreCase: true, out var kind)
            ? kind
            : Default(which);

    /// <summary>The choice as it is kept in the file.</summary>
    public static string Written(SoundKind kind) => kind.ToString().ToLowerInvariant();
}
