using System;
using System.IO;

namespace WhaleGenie.Models;

/// <summary>
/// Where WhaleGenie keeps the things that belong to the copy of it that is running. They sit beside
/// the program rather than somewhere in the user's profile, so a copy carried on a stick takes
/// its own choices and its own logs with it, and two copies side by side never share either.
/// </summary>
/// <remarks>
/// A copy installed where it may not write — under Program Files, for instance — simply keeps its
/// defaults and writes no logs, which is better than refusing to start.
/// </remarks>
public static class AppPaths
{
    /// <summary>The folder the program itself is in.</summary>
    public static string Root { get; } = AppContext.BaseDirectory;

    /// <summary>The choices made in the settings window.</summary>
    public static string Settings { get; } = Path.Combine(Root, "settings.json");

    /// <summary>The logs of the runs that stopped on a failure.</summary>
    public static string Logs { get; } = Path.Combine(Root, "logs");

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
