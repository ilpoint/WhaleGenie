using System;
using System.IO;

namespace WhaleGenie.Models;

/// <summary>
/// Where WhaleGenie keeps the things that belong to the copy of it that is running: the choices made
/// in its windows, the log of what it did, and the snapshot of work not yet saved.
/// </summary>
/// <remarks>
/// These live in the person's own profile rather than beside the program. Beside the program looked
/// tidier, and a portable copy did carry its choices along — but an update, a reinstall or a move
/// replaces that folder, and what it takes with it is exactly the part somebody would miss: their
/// settings, their unsaved work, the log of the failure they were about to report. The folder under
/// Program Files is also read-only for an ordinary account, so a copy installed there never had
/// anywhere to write in the first place.
/// </remarks>
public static class AppPaths
{
    /// <summary>The folder the program itself is in.</summary>
    public static string Root { get; } = AppContext.BaseDirectory;

    /// <summary>
    /// What this copy of the program keeps for the person using it. Local rather than roaming: a
    /// window size or a log is a fact about this machine, and a profile copied between machines has
    /// no use for another machine's log.
    /// </summary>
    public static string State { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WhaleGenie");

    /// <summary>The choices made in the settings window.</summary>
    public static string Settings { get; } = Path.Combine(State, "settings.json");

    /// <summary>The logs of the runs that stopped on a failure.</summary>
    public static string Logs { get; } = Path.Combine(State, "logs");

    /// <summary>
    /// The snapshot of a project that had unsaved changes when the program stopped. It sits with the
    /// settings because it is the one thing a failed run must not take away.
    /// </summary>
    public static string Recovery { get; } = Path.Combine(State, "recovery.json");

    /// <summary>
    /// Callable macros shared by every project opened by this copy of WhaleGenie. It follows the
    /// program folder so a portable copy keeps its library with it.
    /// </summary>
    public static string PublicMacroLibrary { get; } =
        Path.Combine(Root, "library", "public-macros.wgmacro");

    /// <summary>Images unpacked from the public macro library.</summary>
    public static string PublicMacroAssets { get; } =
        Path.Combine(Root, "library", "public-macros.assets");

    /// <summary>
    /// The working files one macro package owns: the pictures unpacked out of it and the copy of the
    /// file the last save replaced. They live here rather than beside the package so a folder of
    /// projects holds projects — a `.assets` folder and a `.bak` next to every file is a folder
    /// nobody can read at a glance — and so that opening a package from a read-only place still has
    /// somewhere to unpack to.
    /// </summary>
    public static string Projects { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WhaleGenie", "projects");
}
