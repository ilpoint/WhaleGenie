using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// Text files: the path a step names, its encoding, reading it, writing it and appending to a log.
public sealed partial class MacroRunner
{
    // --------------------------------------------------------------------- files

    /// <summary>A path a step named, with any <c>variable</c> already filled in.</summary>
    private string PathOf(ExecutableStep step, string parameter = "path")
        => ReadValue(step, parameter).AsText();

    /// <summary>
    /// The encoding a step named, or UTF-8 when it says nothing. Older macros have no such field,
    /// and UTF-8 without a mark is what they were written with.
    /// </summary>
    private static string EncodingOf(ExecutableStep step)
    {
        var name = step.Text("encoding").Trim();
        return name.Length == 0 ? TextEncoding.Default : name;
    }

    private static readonly JsonSerializerOptions Writable = new() { WriteIndented = true };

    private void ReadTextFile(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var text = _devices.Files.ReadText(path, EncodingOf(step));

        // The whole file is one answer and its lines are another, so they are two results rather
        // than one result whose shape changes with a switch. The line results carry the filtering
        // options; the text result is the file exactly as it was read.
        Give(step, "text", Value.FromText(text));

        if (!Wants(step, "lines"))
        {
            Log(LogLevel.Info, depth, step.Type, LogKeys.ReadFile, path, text.Length);
            return;
        }

        var wanted = Lines(text, Flag(step, "skipBlankLines", false), Flag(step, "trimLines", false));
        var limit = Number(step, "limit");
        if (limit > 0 && wanted.Count > limit)
        {
            wanted = [.. wanted.Take(limit)];
        }

        Give(step, "lines", Value.FromList(wanted.Select(Value.FromText)));
        Log(LogLevel.Info, depth, step.Type, LogKeys.ReadFileLines, path, wanted.Count);
    }

    /// <summary>
    /// A text cut into lines the way every editor cuts one: all three ways of ending a line count,
    /// and the break at the very end of the file does not leave an empty line behind it.
    /// </summary>
    private static List<string> Lines(string text, bool skipBlank, bool trim)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var kept = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            var one = trim ? line.Trim() : line;
            if (skipBlank && one.Length == 0)
            {
                continue;
            }

            kept.Add(one);
        }

        return kept;
    }

    private void WriteTextFile(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var text = ReadValue(step, "fileContent").AsText();
        var append = string.Equals(ReadText(step, "writeMode").Trim(), "append", StringComparison.OrdinalIgnoreCase);
        var encoding = EncodingOf(step);
        var newline = ReadText(step, "newline").Trim().ToLowerInvariant();

        if (newline == "end")
        {
            text += Environment.NewLine;
        }
        else if (newline == "start" && append && _devices.Files.HasOpenLine(path, encoding))
        {
            // Adding to a file is how a macro keeps a log or grows a list, and "a line of its own"
            // is the whole point: without this the new text lands on the end of the last line, which
            // is a mistake nobody sees until the file is read back.
            text = Environment.NewLine + text;
        }

        _devices.Files.WriteText(path, text, append, encoding);
        Log(LogLevel.Info, depth, step.Type, append ? LogKeys.AppendedFile : LogKeys.WroteFile, path, text.Length);
    }

    /// <summary>
    /// Adds one line to a log file. It is a step of its own rather than write-text-in-append-mode
    /// because keeping a log is a common enough thing to want: the line break and the time in front
    /// of it are the two things every caller would otherwise have to remember to add.
    /// </summary>
    private void AppendLog(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var encoding = EncodingOf(step);
        var line = ReadValue(step, "logLine").AsText();
        if (Flag(step, "timestamp", true))
        {
            line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + " " + line;
        }

        // The action promises one line, so a file whose last line was never closed — written by
        // something else, or by a run that was interrupted halfway — gets a break of its own first.
        // A file that is not there yet has no open line, so its first line is not pushed down.
        var lead = _devices.Files.HasOpenLine(path, encoding) ? Environment.NewLine : string.Empty;
        _devices.Files.WriteText(path, lead + line + Environment.NewLine, append: true, encoding);
        Log(LogLevel.Info, depth, step.Type, LogKeys.AppendedFile, path, line.Length);
    }
}
