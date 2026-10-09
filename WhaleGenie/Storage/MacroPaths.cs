using System;
using System.IO;
using WhaleGenie.Core.Devices.Platform;

namespace WhaleGenie.Storage;

/// <summary>
/// Turning a path the user picked into the value a step should hold.
/// </summary>
/// <remarks>
/// A file inside the macros folder is written as a path relative to it, so a macro that is copied
/// to another machine — or handed to somebody else — still finds its own data. Everything else is
/// written in full: a file on another drive cannot be reached by a relative path at all, and
/// pretending otherwise would be worse than the longer text.
/// </remarks>
public static class MacroPaths
{
    /// <summary>The folder relative paths are read from, which is where the macros live.</summary>
    public static string Folder => LocalFileDevice.DefaultBaseFolder;

    /// <summary>The value to store for a file or folder the user picked out of a dialog.</summary>
    public static string ForMacro(string picked)
    {
        var full = (picked ?? string.Empty).Trim();
        if (full.Length == 0)
        {
            return string.Empty;
        }

        try
        {
            var from = Path.GetFullPath(Folder);
            var to = Path.GetFullPath(full);
            var relative = Path.GetRelativePath(from, to);

            // ".." anywhere in it means the file is above the folder, which a relative path can
            // still spell but nobody would recognise; a path that starts with one is left alone.
            return relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)
                ? to
                : relative;
        }
        catch (Exception failure) when (failure is ArgumentException or NotSupportedException
                                           or PathTooLongException)
        {
            // Text that is not a valid path is kept as the user typed it: refusing to store it
            // would lose what they wrote, and the run will say plainly that it cannot be read.
            return full;
        }
    }
}
