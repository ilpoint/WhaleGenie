using System;
using System.Globalization;


namespace WhaleGenie.Core.Devices;

// Programs, the registry, the environment and the machine own switches.
/// <summary>What a command line gave back once it finished.</summary>
public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// The two roots a macro may reach: this user's own settings and the ones that hold for everybody.
/// There is no third one on purpose — the classes and users roots are where a wrong path changes
/// what every program on the machine does, and nothing a macro does needs them.
/// </summary>
public enum RegistryBranch
{
    /// <summary>HKEY_CURRENT_USER, which needs no elevation and belongs to the person running the macro.</summary>
    CurrentUser,

    /// <summary>HKEY_LOCAL_MACHINE, which needs administrator rights to write.</summary>
    LocalMachine,
}

/// <summary>The kinds of value a macro may write: the three that carry a single thing worth setting.</summary>
public enum RegistryValueType
{
    /// <summary>Plain text.</summary>
    String,

    /// <summary>Text with <c>%NAME%</c> in it, which is filled in when a program reads it.</summary>
    ExpandString,

    /// <summary>A 32 bit number.</summary>
    DWord,
}

/// <summary>Whose name an environment variable is written under.</summary>
public enum EnvironmentScope
{
    /// <summary>This account only, which is what a macro almost always means.</summary>
    User,

    /// <summary>Every account on the machine, which needs administrator rights.</summary>
    Machine,
}

/// <summary>
/// The things a person can do to a machine from the Start menu's power button, which are also the
/// things a macro that runs overnight needs to be able to do to it.
/// </summary>
public enum PowerAction
{
    /// <summary>Locks the screen, leaving everything running behind it.</summary>
    Lock,

    /// <summary>Turns the screen off. Any key or movement of the mouse turns it back on.</summary>
    MonitorOff,

    /// <summary>Signs the current user out, closing what is open.</summary>
    SignOut,

    /// <summary>Suspends the machine, keeping what is open in memory.</summary>
    Sleep,

    /// <summary>Writes what is open to disk and turns the machine off.</summary>
    Hibernate,

    /// <summary>Closes what is open and starts the machine again.</summary>
    Restart,

    /// <summary>Closes what is open and turns the machine off.</summary>
    ShutDown,

    /// <summary>Cancels a restart or shutdown that is still counting down.</summary>
    AbortShutdown,
}

/// <summary>
/// The sounds a machine plays for the things that happen to it, which are also what a macro that
/// has finished — or gone wrong — can play to say so while nobody is looking at the screen.
/// Which sound each of these is, and whether any of them is heard at all, is the machine's own
/// sound scheme: WhaleGenie asks for an event, not for a file.
/// </summary>
public enum SoundKind
{
    /// <summary>Whatever this machine plays when nothing more particular is asked for.</summary>
    Default,

    /// <summary>The sound for an ordinary notice.</summary>
    Information,

    /// <summary>The sound for something worth looking at.</summary>
    Warning,

    /// <summary>The sound for something that went wrong.</summary>
    Error,

    /// <summary>The sound for a question.</summary>
    Question,
}

/// <summary>
/// How a notification looks when it appears: an ordinary note, something worth looking at, or
/// something that went wrong. The three correspond to the machine's own notification pictures, so
/// a macro picks a meaning rather than a drawing.
/// </summary>
public enum NotificationKind
{
    /// <summary>An ordinary note.</summary>
    Information,

    /// <summary>Something worth looking at.</summary>
    Warning,

    /// <summary>Something that went wrong.</summary>
    Error,
}

/// <summary>
/// How a program is to be started: what to run, where, and how it should come up. Gathered into
/// one thing rather than a row of loose arguments, because a call site should not have to be read
/// twice to tell one switch from another.
/// </summary>
public sealed record StartRequest(
    string FileName,
    string Arguments = "",
    string WorkingDirectory = "",
    bool Hidden = false,
    bool RunAsAdmin = false,
    IReadOnlyDictionary<string, string>? Environment = null);

/// <summary>
/// How a command line is to be run: what to run, where, how long it may take, and what it is
/// handed before it starts. <see cref="OnOutput"/> and <see cref="OnError"/> are for a caller
/// that wants each line while the program is still running; they are called on the thread that
/// asked for the run, and the whole of what was printed still comes back in the result.
/// <see cref="OutputEncoding"/> names the code page the program writes in, or is left alone for
/// this machine's own; reading a Chinese Windows program's output as UTF-8 turns it into question
/// marks, so the two ends have to agree on which it is.
/// </summary>
public sealed record CommandRequest(
    string FileName,
    string Arguments = "",
    string WorkingDirectory = "",
    int TimeoutMs = 30000,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? StandardInput = null,
    Action<string>? OnOutput = null,
    Action<string>? OnError = null,
    string? OutputEncoding = null);
