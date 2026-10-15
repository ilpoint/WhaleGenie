namespace WhaleGenie.Core.Devices;

/// <summary>
/// What a script step hands its text to. The three command kinds and the two file kinds all end up
/// at cmd.exe and differ in their file ending and in what their window does; the rest name the
/// program that reads them.
/// </summary>
public enum ScriptKind
{
    /// <summary>A command run with its window left open once it has finished.</summary>
    CommandKeepWindow,

    /// <summary>A command run in a window that closes when the command is done.</summary>
    CommandCloseWindow,

    /// <summary>A command run with no window at all.</summary>
    CommandHidden,

    /// <summary>A batch file, which is the same as a hidden command under a .bat name.</summary>
    BatchFile,

    /// <summary>The same under a .cmd name.</summary>
    CommandFile,

    /// <summary>A Windows PowerShell script.</summary>
    PowerShell,

    /// <summary>An AutoHotkey script, which needs AutoHotkey on the machine.</summary>
    AutoHotkey,

    /// <summary>A program of the macro's own choosing, named with its flags and a file ending.</summary>
    Custom,
}
