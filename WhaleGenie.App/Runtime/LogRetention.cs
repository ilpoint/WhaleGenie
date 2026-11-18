using System;
using System.IO;
using System.Linq;

namespace WhaleGenie.Runtime;

/// <summary>
/// Keeps the log folder from growing for ever. A program that writes one file per failed run and one
/// per day of MCP calls leaves thousands of files behind after a year, none of which anybody reads
/// twice — and the newest ones are the only ones a person ever opens.
/// </summary>
/// <remarks>
/// Only the two kinds this program writes one file per run or per day are counted: <c>failure-…log</c>
/// and <c>mcp-…log</c>. <c>runtime.log</c> rolls by its own size and is never deleted here, and a file
/// somebody put in the folder themselves is left where it is — a cleaner that removes what it does not
/// recognise eventually removes somebody's work.
/// </remarks>
public static class LogRetention
{
    /// <summary>How many run logs are kept when nothing has been chosen.</summary>
    public const int Usual = 100;

    /// <summary>The fewest that can be asked for: keeping nothing would leave no trace of a failure.</summary>
    public const int Fewest = 1;

    /// <summary>The most that can be asked for, so a slip of the hand cannot fill the disk.</summary>
    public const int Most = 999;

    /// <summary>
    /// Deletes the oldest run logs beyond <paramref name="keep"/>, newest first kept. Returns how many
    /// files went, which is what a check measures; a file that cannot be deleted is passed over, since
    /// a log left behind is not a reason to stop starting up.
    /// </summary>
    public static int Prune(string folder, int keep)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        var removed = 0;
        var kept = Math.Clamp(keep, Fewest, Most);
        foreach (var file in Directory.EnumerateFiles(folder)
                     .Where(file => IsRunLog(Path.GetFileName(file)))
                     .OrderByDescending(File.GetLastWriteTimeUtc)
                     .Skip(kept))
        {
            try
            {
                File.Delete(file);
                removed++;
            }
            catch
            {
                // A file another program is holding open is read again at the next start.
            }
        }

        return removed;
    }

    /// <summary>True for the logs written one file per run or per day, which are the ones counted.</summary>
    public static bool IsRunLog(string name)
        => name.StartsWith("failure-", StringComparison.OrdinalIgnoreCase)
           || name.StartsWith("mcp-", StringComparison.OrdinalIgnoreCase);
}
