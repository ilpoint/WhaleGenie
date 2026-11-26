using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The file actions: reading and writing text, JSON and CSV, one file at a time, folders and
/// archives, and the snapshot of the variables a run keeps. A path is read here, with any name in
/// it already worked out, so every file action names its target the same way.
/// </summary>
internal static class FileActions
{
    /// <summary>
    /// The file actions, each answered by the method written for it. A step of another family is
    /// not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
            ["file.readText"] = (run, step, depth, _) =>
            {
                ReadTextFile(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.writeText"] = (run, step, depth, _) =>
            {
                WriteTextFile(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.appendLog"] = (run, step, depth, _) =>
            {
                AppendLog(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.zip"] = (run, step, depth, _) =>
            {
                ZipFolder(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.exists"] = (run, step, depth, _) =>
            {
                FileExists(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.delete"] = (run, step, depth, _) =>
            {
                DeleteFile(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.copy"] = (run, step, depth, _) =>
            {
                CopyFile(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.move"] = (run, step, depth, _) =>
            {
                MoveFile(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.createFolder"] = (run, step, depth, _) =>
            {
                CreateFolder(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.deleteFolder"] = (run, step, depth, _) =>
            {
                DeleteFolder(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.path"] = (run, step, depth, _) =>
            {
                PathPart(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.unzip"] = (run, step, depth, _) =>
            {
                UnzipFile(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.listFiles"] = (run, step, depth, _) =>
            {
                ListFiles(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.saveVariables"] = (run, step, depth, _) =>
            {
                SaveVariables(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.loadVariables"] = (run, step, depth, _) =>
            {
                LoadVariables(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },
        };

    // ------------------------------------------------------------------ text

    /// <summary>
    /// The encoding a step named, or UTF-8 when it says nothing. Older macros have no such field,
    /// and UTF-8 without a mark is what they were written with.
    /// </summary>
    internal static string EncodingOf(ExecutableStep step)
    {
        var name = step.Text("encoding").Trim();
        return name.Length == 0 ? TextEncoding.Default : name;
    }

    private static readonly JsonSerializerOptions Writable = new() { WriteIndented = true };

    private static void ReadTextFile(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var text = run.Devices.Files.ReadText(path, EncodingOf(step));

        // The whole file is one answer and its lines are another, so they are two results rather
        // than one result whose shape changes with a switch. The line results carry the filtering
        // options; the text result is the file exactly as it was read.
        run.Give(step, "text", Value.FromText(text));

        if (!run.Wants(step, "lines"))
        {
            run.Log(LogLevel.Info, depth, step.Type, LogKeys.ReadFile, path, text.Length);
            return;
        }

        var wanted = Lines(text, run.Flag(step, "skipBlankLines", false),
            run.Flag(step, "trimLines", false));
        var limit = run.Args(step).Number("limit");
        if (limit > 0 && wanted.Count > limit)
        {
            wanted = [.. wanted.Take(limit)];
        }

        run.Give(step, "lines", Value.FromList(wanted.Select(Value.FromText)));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ReadFileLines, path, wanted.Count);
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

    private static void WriteTextFile(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var text = run.Args(step).ValueOf("fileContent").AsText();
        var append = string.Equals(run.Args(step).Text("writeMode").Trim(), "append",
            StringComparison.OrdinalIgnoreCase);
        var encoding = EncodingOf(step);
        var newline = run.Args(step).Text("newline").Trim().ToLowerInvariant();

        if (newline == "end")
        {
            text += Environment.NewLine;
        }
        else if (newline == "start" && append && run.Devices.Files.HasOpenLine(path, encoding))
        {
            // Adding to a file is how a macro keeps a log or grows a list, and "a line of its own"
            // is the whole point: without this the new text lands on the end of the last line, which
            // is a mistake nobody sees until the file is read back.
            text = Environment.NewLine + text;
        }

        run.Devices.Files.WriteText(path, text, append, encoding);
        run.Log(LogLevel.Info, depth, step.Type,
            append ? LogKeys.AppendedFile : LogKeys.WroteFile, path, text.Length);
    }

    /// <summary>
    /// Adds one line to a log file. It is a step of its own rather than write-text-in-append-mode
    /// because keeping a log is a common enough thing to want: the line break and the time in front
    /// of it are the two things every caller would otherwise have to remember to add.
    /// </summary>
    private static void AppendLog(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var encoding = EncodingOf(step);
        var line = run.Args(step).ValueOf("logLine").AsText();
        if (run.Flag(step, "timestamp", true))
        {
            line = run.Evaluation.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                + " " + line;
        }

        // The action promises one line, so a file whose last line was never closed — written by
        // something else, or by a run that was interrupted halfway — gets a break of its own first.
        // A file that is not there yet has no open line, so its first line is not pushed down.
        var lead = run.Devices.Files.HasOpenLine(path, encoding) ? Environment.NewLine : string.Empty;
        run.Devices.Files.WriteText(path, lead + line + Environment.NewLine, append: true, encoding);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.AppendedFile, path, line.Length);
    }

    // ------------------------------------------------------------------ one file at a time

    private static void FileExists(IRunContext run, ExecutableStep step, int depth)
    {
        var found = run.Devices.Files.Exists(run.PathOf(step));
        run.Store(step, "exists", Value.FromBool(found), depth);
    }

    private static void DeleteFile(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var recycle = run.Flag(step, "toRecycleBin", false);
        run.Devices.Files.Delete(path, recycle);
        run.Log(LogLevel.Info, depth, step.Type,
            recycle ? LogKeys.RecycledFile : LogKeys.DeletedFile, path);
    }

    private static void CopyFile(IRunContext run, ExecutableStep step, int depth)
    {
        var from = run.PathOf(step, "from");
        var (to, go) = Landing(run, step, run.PathOf(step, "to"));
        if (!go)
        {
            run.Give(step, "path", Value.FromText(run.Devices.Files.Resolve(to)));
            run.Log(LogLevel.Info, depth, step.Type, LogKeys.FileLeftAlone, to);
            return;
        }

        run.Devices.Files.Copy(from, to);
        run.Give(step, "path", Value.FromText(run.Devices.Files.Resolve(to)));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.CopiedFile, from, to);
    }

    private static void MoveFile(IRunContext run, ExecutableStep step, int depth)
    {
        var from = run.PathOf(step, "from");
        var (to, go) = Landing(run, step, run.PathOf(step, "to"));
        if (!go)
        {
            run.Give(step, "path", Value.FromText(run.Devices.Files.Resolve(to)));
            run.Log(LogLevel.Info, depth, step.Type, LogKeys.FileLeftAlone, to);
            return;
        }

        run.Devices.Files.Move(from, to);
        run.Give(step, "path", Value.FromText(run.Devices.Files.Resolve(to)));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.MovedFile, from, to);
    }

    /// <summary>
    /// Where a copy or a move is to land, and what to do about a file already sitting there: put
    /// this one over it, leave the one that is there alone, or give the new one a number of its own
    /// beside it. Whether the name is taken is a question only the machine can answer.
    /// </summary>
    private static (string To, bool Go) Landing(IRunContext run, ExecutableStep step, string to)
    {
        var choice = run.Args(step).Text("ifExists").Trim().ToLowerInvariant();
        if (!run.Devices.Files.Exists(to))
        {
            return (to, true);
        }

        return choice switch
        {
            "skip" => (to, false),
            "unique" => (Beside(run, to), true),
            _ => (to, true),
        };
    }

    /// <summary>
    /// A free name next to a taken one — report (2).csv, then report (3).csv — which is the way
    /// Windows itself gives a second file a name of its own.
    /// </summary>
    private static string Beside(IRunContext run, string path)
    {
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var number = 2; number < 10_000; number++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({number}){extension}");
            if (!run.Devices.Files.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new StepFailure(RunKeys.NoFreeName, path);
    }

    // ------------------------------------------------------------------ folders and archives

    private static void CreateFolder(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        run.Devices.Files.CreateFolder(path);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.MadeFolder, path);
    }

    private static void DeleteFolder(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var recurse = run.Flag(step, "recurse", false);

        run.Devices.Files.DeleteFolder(path, recurse);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.RemovedFolder, path);
    }

    /// <summary>
    /// Works out one piece of a path. A macro that has to say "the file next to this one" or "the
    /// same name with .bak on the end" should not have to spell the whole path out again, and the
    /// two folder answers are the ones that are always in the same place on any machine.
    /// </summary>
    private static void PathPart(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var other = run.Args(step).ValueOf("fileName").AsText().Trim();

        var answer = run.Args(step).Text("operation").Trim().ToLowerInvariant() switch
        {
            "folder" => Path.GetDirectoryName(path) ?? string.Empty,
            "name" => Path.GetFileName(path),
            "basename" => Path.GetFileNameWithoutExtension(path),
            "extension" => Path.GetExtension(path),
            "full" => run.Devices.Files.Resolve(path),
            "temp" => Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            "macros" => run.Devices.Files.BaseFolder,
            _ => other.Length == 0 ? path : Path.Combine(path, other),
        };

        run.Store(step, "path", Value.FromText(answer), depth);
    }

    private static void UnzipFile(IRunContext run, ExecutableStep step, int depth)
    {
        var from = run.PathOf(step, "from");
        var folder = run.PathOf(step, "folder");
        var overwrite = run.Flag(step, "overwrite", true);

        run.Devices.Files.Unzip(from, folder, overwrite);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.Unzipped, from, folder);
    }

    private static void ZipFolder(IRunContext run, ExecutableStep step, int depth)
    {
        var folder = run.PathOf(step, "folder");
        var to = run.PathOf(step, "to");
        var includeTopFolder = run.Flag(step, "includeTopFolder", false);

        run.Devices.Files.Zip(folder, to, includeTopFolder);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.Zipped, folder, to);
    }

    private static void ListFiles(IRunContext run, ExecutableStep step, int depth)
    {
        var folder = run.PathOf(step, "folder");
        var recurse = run.Flag(step, "recurse", false);
        // A filter said with a semi-colon is several filters, the way the action this is modelled
        // on reads it: *.txt; *.csv is one question, not two steps.
        var patterns = run.Args(step).Text("pattern")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var levels = run.Args(step).Number("depth");
        // 收文件还是收文件夹：默认收文件，跟这个选择出现之前一样。
        var kind = run.Args(step).Text("kind").Trim().ToLowerInvariant() switch
        {
            "folders" => ListKind.Folders,
            "both" => ListKind.Both,
            _ => ListKind.Files,
        };
        var files = run.Devices.Files.List(folder, patterns, recurse,
            recurse ? (levels <= 0 ? -1 : levels) : 0, kind);

        var since = ChangedSince(run, step);
        var smallest = run.Args(step).Number("minKb") * 1024L;
        var largest = run.Args(step).Number("maxKb") * 1024L;
        var kept = files.Where(entry =>
            (since is null || entry.Modified >= since)
            && (smallest <= 0 || entry.Size >= smallest)
            && (largest <= 0 || entry.Size <= largest));

        // A listing a macro loops over is the same listing on every run, so the folder's own order
        // is never the answer: an order is always asked for, and the name settles the ties.
        var ordered = InOrder(kept, run.Args(step).Text("sortBy").Trim(),
            run.Flag(step, "descending", false));
        var root = run.Flag(step, "relative", false) ? run.Devices.Files.Resolve(folder) : "";
        var paths = ordered
            .Select(entry => root.Length == 0 ? entry.Path : Path.GetRelativePath(root, entry.Path))
            .ToList();

        run.Store(step, "files", Value.FromList(paths.Select(Value.FromText)), depth);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ListedFiles, folder, paths.Count);
    }

    /// <summary>
    /// The earliest a listing reaches back to. Nothing said is every file, which is what a step
    /// that only wants what is in a folder expects.
    /// </summary>
    private static DateTimeOffset? ChangedSince(IRunContext run, ExecutableStep step)
    {
        var count = run.Args(step).Number("withinCount");
        if (count <= 0)
        {
            return null;
        }

        var back = run.Args(step).Text("withinUnit").Trim().ToLowerInvariant() switch
        {
            "minutes" => TimeSpan.FromMinutes(count),
            "hours" => TimeSpan.FromHours(count),
            _ => TimeSpan.FromDays(count),
        };

        return run.Evaluation.Now - back;
    }

    /// <summary>
    /// Puts a folder listing in the order asked for, falling back to the name so that two files
    /// agreeing on the key still come out the same way every run.
    /// </summary>
    private static IEnumerable<FileEntry> InOrder(IEnumerable<FileEntry> files, string key,
        bool descending)
        => FileOrder.Of(files, key, descending);

    // ------------------------------------------------------------------ variables

    private static void SaveVariables(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var chosen = run.Args(step).Text("names").Trim();
        IEnumerable<string> names = chosen.Length > 0
            ? chosen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : run.Variables.Local.Values.Keys
                .Concat(run.Variables.Global.Values.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase);

        // A value WhaleGenie provides is not a snapshot's to keep: it is read off the machine at
        // the moment a step asks, and a file that wrote one down could only ever put it back.
        names = names.Where(name => !VariableName.IsSystem(name));

        var document = new JsonObject();
        foreach (var name in names)
        {
            if (run.Variables.TryGet(name, out var value))
            {
                document[name] = JsonValues.Node(value);
            }
        }

        var text = document.ToJsonString(Writable);
        run.Devices.Files.WriteText(path, text, false, EncodingOf(step));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.SavedVariables, path, document.Count);
    }

    private static void LoadVariables(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var document = FileJsonActions.Json(path,
            run.Devices.Files.ReadText(path, EncodingOf(step)));
        if (document is not JsonObject saved)
        {
            throw new StepFailure(RunKeys.BadJson, path);
        }

        foreach (var (name, node) in saved)
        {
            // A file may hold any name at all — it is written by hand as often as by the step
            // above — so this is where a name the program keeps for itself is refused. Writing it
            // would put a value in front of the clock or the clipboard for the rest of the run.
            if (VariableName.IsSystem(name))
            {
                throw new StepFailure(RunKeys.SystemVariableReadOnly, name);
            }

            // Shared variables are only written to when the Variable Center already made one,
            // the same rule the "set variable" step follows.
            var target = run.Variables.Global.Contains(name) ? run.Variables.Global : run.Variables.Local;
            target.Set(name, JsonValues.FromJson(node));
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.LoadedVariables, path, saved.Count);
    }
}
