using System;
using System.IO;

namespace WhaleGenie.Runtime;

/// <summary>
/// Where the manual is read from. A package carries a copy of it beside the program, and that copy
/// is the one to open: somebody who has just installed this has no repository to browse and may
/// well have no network, and an item that opens nothing is worse than no item at all. The page in
/// the repository is what is left when there is no packaged copy, which is how a working copy of
/// the repository runs.
/// </summary>
public static class Manual
{
    /// <summary>The copy a package carries: a folder of its own beside the program.</summary>
    public static string Packaged(string folder) => Path.Combine(folder, "doc", "manual.md");

    /// <summary>
    /// What "get started" opens. <paramref name="folder"/> is where the program runs from; a check
    /// hands its own folder in rather than having to write a file next to the program running it.
    /// </summary>
    public static string Where(string? folder = null)
    {
        var packaged = Packaged(folder ?? AppContext.BaseDirectory);
        return File.Exists(packaged) ? packaged : ProjectLinks.Manual;
    }
}
