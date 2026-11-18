using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The file a failed run leaves behind: which macro it was, when, and every line the run wrote with
/// the time it was written. It is written once, at the end of a run that failed, which is why it is
/// a piece of work rather than something the run keeps.
/// </summary>
/// <remarks>
/// Everything the run wrote goes down, not only the line that failed: what went wrong is read next
/// to the steps that led up to it. The file is written whether or not anything goes wrong while
/// writing it — a run that has already failed must not be brought down by the act of recording it,
/// so a failure here is only logged.
/// </remarks>
internal sealed class FailureLog(IRunContext run, IRunHost host, IReadOnlyList<LogEntry> entries,
    string folder, string name)
{
    /// <summary>How much of the macro's name a file name keeps before it is cut short.</summary>
    private const int LongestNameInFile = 40;

    /// <summary>Writes the log of this run down, and says where it went.</summary>
    public void Write()
    {
        try
        {
            var now = DateTimeOffset.Now;
            var stem = "failure-" + NameForFile()
                + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var relative = Path.Combine(folder, stem + ".log");
            for (var counter = 1; run.Devices.Files.Exists(relative); counter++)
            {
                relative = Path.Combine(folder,
                    string.Create(CultureInfo.InvariantCulture, $"{stem}-{counter}.log"));
            }

            run.Devices.Files.WriteText(relative, Text(now), append: false, TextEncoding.Utf8Bom);
            run.Log(LogLevel.Info, 0, string.Empty, LogKeys.FailureLog,
                run.Devices.Files.Resolve(relative));
        }
        catch (Exception failure)
        {
            run.Log(LogLevel.Warn, 0, string.Empty, LogKeys.FailureLogFailed, failure.Message);
        }
    }

    /// <summary>
    /// What the file says: a header naming the macro and the moment, then every line the run wrote.
    /// The lines are indented by two spaces for each level, because a text file has no other way of
    /// saying that a step is inside a block.
    /// </summary>
    private string Text(DateTimeOffset when)
    {
        var text = new StringBuilder();
        text.Append(host.Text(new LogEntry(when, LogLevel.Info, 0, string.Empty, LogKeys.LogFileHeader,
                [name, when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)])))
            .Append(Environment.NewLine)
            .Append(Environment.NewLine);

        foreach (var entry in entries)
        {
            text.Append(entry.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture))
                .Append("  ")
                .Append(' ', entry.Depth * 2)
                .Append(host.Text(entry))
                .Append(Environment.NewLine);
        }

        return text.ToString();
    }

    /// <summary>
    /// The run's own name cut down to what a file name can carry, with a dash after it, or nothing
    /// at all when the name has nothing usable in it. The name is the user's, so anything a file
    /// name cannot hold becomes an underscore rather than stopping the file from being written.
    /// </summary>
    private string NameForFile()
    {
        var forbidden = Path.GetInvalidFileNameChars();
        var kept = new StringBuilder();
        foreach (var letter in name.Trim())
        {
            if (kept.Length == LongestNameInFile)
            {
                break;
            }

            kept.Append(forbidden.Contains(letter) ? '_' : letter);
        }

        // A name may not end in a dot or a space: Windows reads that as the end of the name.
        var keptName = kept.ToString().Trim(' ', '.');
        return keptName.Length == 0 ? string.Empty : keptName + "-";
    }
}
