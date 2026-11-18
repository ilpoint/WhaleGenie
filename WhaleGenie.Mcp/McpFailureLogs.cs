using System;
using System.IO;
using System.Linq;

namespace WhaleGenie.Mcp;

/// <summary>
/// Which file in the log folder is a failure log. A failed run writes <c>failure-…log</c> there, and
/// the server keeps its own call log in the same folder; "the newest .log" therefore answered with
/// the audit trail far more often than with the failure it promised. The rule is written once here,
/// with the folder passed in, so it can be checked without a real run failing.
/// </summary>
internal static class McpFailureLogs
{
    /// <summary>
    /// The failure log to read: the one at the path a client was given, or the newest one there is.
    /// Null when there is none, or when the path is not a failure log inside this folder.
    /// </summary>
    public static string? Named(string folder, string? path)
    {
        var full = Path.GetFullPath(folder);
        if (!Directory.Exists(full))
        {
            return null;
        }

        var written = (path ?? string.Empty).Trim();
        if (written.Length > 0)
        {
            var asked = Path.GetFullPath(written);
            return IsFailureLog(asked, full) && File.Exists(asked) ? asked : null;
        }

        return Directory.EnumerateFiles(full, "failure-*.log")
            .Select(Path.GetFullPath)
            .Where(file => IsFailureLog(file, full))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    /// <summary>True for a failure log inside the log folder and not somewhere else.</summary>
    private static bool IsFailureLog(string full, string folder)
        => full.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
           && Path.GetFileName(full).StartsWith("failure-", StringComparison.OrdinalIgnoreCase);
}
