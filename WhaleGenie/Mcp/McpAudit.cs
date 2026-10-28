using System;
using System.IO;
using System.Text;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Mcp;

/// <summary>
/// The record of what an outside client did: which tool, to what, and how it ended. It exists
/// because "what did the AI change" is a question the person at the machine asks afterwards, and
/// the framework's own logging is off on purpose — this is our record, not theirs.
/// </summary>
/// <remarks>
/// Only calls that changed something or reached the machine are written. A log that records every
/// look is one nobody reads when the question is what was changed, and a screen capture or a typing
/// call is worth a line while a catalogue search is not.
/// </remarks>
public static class McpAudit
{
    private static readonly object Gate = new();

    /// <summary>
    /// Where the record is written. It is settable so a check can write somewhere it may clean up
    /// rather than into the program's own folder; nothing else changes it.
    /// </summary>
    public static string Folder { get; set; } = AppPaths.Logs;

    /// <summary>The file today's calls are written to: one file a day, so an old day stays readable.</summary>
    public static string File => Path.Combine(Folder, $"mcp-{DateTime.Now:yyyy-MM-dd}.log");

    /// <summary>
    /// One line: the time, the tool, what it was pointed at, and how it ended. <paramref name="summary"/>
    /// is written by the caller rather than taken from the request, because a request can carry a
    /// whole macro and a typing call can carry a paragraph nobody wants in a file.
    /// </summary>
    public static void Note(string tool, string summary, string? outcome = null)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("HH:mm:ss"))
            .Append('\t').Append(tool)
            .Append('\t').Append(One(summary));

        if (outcome is { Length: > 0 })
        {
            line.Append('\t').Append(One(outcome));
        }

        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                System.IO.File.AppendAllText(File, line.Append('\n').ToString(), Encoding.UTF8);
            }
        }
        catch (IOException)
        {
            // A log that cannot be written must not stop the call it is about: an unwritable folder
            // is the person's business, not a reason for a tool to fail.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>A value that cannot break the one-line-per-call shape of the file.</summary>
    private static string One(string text) => text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');
}
