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

            ["file.readJson"] = (run, step, depth, _) =>
            {
                ReadJson(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.writeJson"] = (run, step, depth, _) =>
            {
                WriteJson(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.readCsv"] = (run, step, depth, _) =>
            {
                ReadCsv(run, step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["file.writeCsv"] = (run, step, depth, _) =>
            {
                WriteCsv(run, step, depth);
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
    private static string EncodingOf(ExecutableStep step)
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
            line = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
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

        return DateTimeOffset.Now - back;
    }

    /// <summary>
    /// Puts a folder listing in the order asked for, falling back to the name so that two files
    /// agreeing on the key still come out the same way every run.
    /// </summary>
    private static IEnumerable<FileEntry> InOrder(IEnumerable<FileEntry> files, string key,
        bool descending)
    {
        var ordered = key.ToLowerInvariant() switch
        {
            "size" => files.OrderBy(entry => entry.Size),
            "modified" => files.OrderBy(entry => entry.Modified),
            "created" => files.OrderBy(entry => entry.Created),
            "extension" => files.OrderBy(entry => Path.GetExtension(entry.Name),
                StringComparer.OrdinalIgnoreCase),
            _ => files.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
        };

        var byName = ordered.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase);
        return descending ? byName.Reverse() : byName;
    }

    // ------------------------------------------------------------------ JSON

    private static void ReadJson(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var document = Json(path, run.Devices.Files.ReadText(path, EncodingOf(step)));
        var query = run.Args(step).ValueOf("query").AsText().Trim();
        var node = query.Length == 0
            ? document
            : Select(document, query) ?? throw new StepFailure(RunKeys.BadJsonPath, query);

        var value = FromJson(node);
        run.Store(step, "value", value, depth);
    }

    private static void WriteJson(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var query = run.Args(step).ValueOf("query").AsText().Trim();
        if (query.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingJsonPath);
        }

        var document = run.Devices.Files.Exists(path)
            ? Json(path, run.Devices.Files.ReadText(path, EncodingOf(step)))
            : new JsonObject();

        Assign(document, query, ToJson(run.Args(step).ValueOf("writeValue")));

        var text = document.ToJsonString(Writable);
        run.Devices.Files.WriteText(path, text, false, EncodingOf(step));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.WroteFile, path, text.Length);
    }

    /// <summary>Reads a JSON file, reporting a broken one as a failed step.</summary>
    private static JsonNode Json(string path, string text)
    {
        try
        {
            return JsonNode.Parse(text) ?? new JsonObject();
        }
        catch (JsonException error)
        {
            throw new StepFailure(RunKeys.BadJson, $"{path}: {error.Message}");
        }
    }

    /// <summary>Walks a path such as <c>server.name</c> or <c>items[0].id</c>.</summary>
    private static JsonNode? Select(JsonNode? root, string query)
    {
        var node = root;
        foreach (var token in Tokens(query))
        {
            node = Within(node, token);
            if (node is null)
            {
                break;
            }
        }

        return node;
    }

    /// <summary>The node one step along a path, or nothing when there is no such place.</summary>
    private static JsonNode? Within(JsonNode? node, Part token) => node switch
    {
        JsonArray array when token.IsIndex && token.Index >= 0 && token.Index < array.Count
            => array[token.Index],
        JsonObject parent when !token.IsIndex
            => parent.TryGetPropertyValue(token.Name, out var child) ? child : null,
        _ => null,
    };

    /// <summary>One step of a path: a name, or a place in a list.</summary>
    private readonly record struct Part(string Name, int Index, bool IsIndex);

    /// <summary>
    /// A path split into its steps: names separated by dots, and a place in a list written between
    /// brackets — by number, counting from zero, or by name in quotes for a name that has a dot in
    /// it. Reading and writing share this, so a path that can be read can also be written.
    /// </summary>
    private static List<Part> Tokens(string query)
    {
        var tokens = new List<Part>();
        var text = query.Trim();
        var index = 0;

        while (index < text.Length)
        {
            if (text[index] == '.')
            {
                index++;
                continue;
            }

            if (text[index] == '[')
            {
                var close = text.IndexOf(']', index);
                if (close < 0)
                {
                    throw new StepFailure(RunKeys.BadJsonPath, query);
                }

                tokens.Add(Token(text[(index + 1)..close].Trim().Trim('\'', '"'), query));
                index = close + 1;
                continue;
            }

            var end = index;
            while (end < text.Length && text[end] != '.' && text[end] != '[')
            {
                end++;
            }

            tokens.Add(Token(text[index..end], query));
            index = end;
        }

        return tokens;
    }

    /// <summary>One step, read from the words a path writes it with.</summary>
    private static Part Token(string written, string query)
    {
        var trimmed = written.Trim();
        if (trimmed.Length == 0)
        {
            throw new StepFailure(RunKeys.BadJsonPath, query);
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var position)
            ? new Part(string.Empty, position, true)
            : new Part(trimmed, 0, false);
    }

    /// <summary>
    /// Sets a value at a path, making the objects along the way. The path is read exactly the way
    /// <see cref="Select"/> reads one, so anything a macro can read it can also write: a path that
    /// names a place inside a list — the third item's price — is the everyday case, and refusing it
    /// would leave a file that can be read and not changed.
    /// </summary>
    private static void Assign(JsonNode root, string query, JsonNode? value)
    {
        var path = Tokens(query);
        if (path.Count == 0)
        {
            throw new StepFailure(RunKeys.BadJsonPath, query);
        }

        var node = root;
        for (var index = 0; index < path.Count - 1; index++)
        {
            node = Descend(node, path[index], query);
        }

        var last = path[^1];
        switch (node)
        {
            case JsonObject parent when !last.IsIndex:
                parent[last.Name] = value;
                return;
            case JsonArray list when last.IsIndex && last.Index >= 0 && last.Index < list.Count:
                list[last.Index] = value;
                return;
            default:
                throw new StepFailure(RunKeys.BadJsonPath, query);
        }
    }

    /// <summary>
    /// One step along a path, on the way to the place being written. An object that is not there
    /// yet is made, because "set this value" is how a macro writes a setting a file does not have;
    /// a place in a list is never made, because the place only means something inside a list that
    /// is already there and as long as it is.
    /// </summary>
    private static JsonNode Descend(JsonNode node, Part token, string query) => node switch
    {
        JsonObject parent when !token.IsIndex => parent[token.Name] is { } child
            ? child
            : parent[token.Name] = new JsonObject(),
        JsonArray list when token.IsIndex && token.Index >= 0 && token.Index < list.Count
            => list[token.Index] ?? throw new StepFailure(RunKeys.BadJsonPath, query),
        _ => throw new StepFailure(RunKeys.BadJsonPath, query),
    };

    // A document and the values a macro works with are the same shape, and the one place that
    // knows how the two are written as each other is JsonValues, so a record read out of a file is
    // the record that went into it.
    private static JsonNode? ToJson(Value value) => JsonValues.Node(value);

    private static Value FromJson(JsonNode? node) => JsonValues.FromJson(node);

    // ------------------------------------------------------------------ CSV

    /// <summary>
    /// What the step said separates its cells: the character written beside the picker when there
    /// is one, and otherwise the one that was picked — which may be <c>auto</c>, and then the file
    /// itself decides.
    /// </summary>
    private static string Separator(IRunContext run, ExecutableStep step)
        => run.Args(step).Text("separatorText").Trim().Length > 0
            ? run.Args(step).Text("separatorText")
            : run.Args(step).Text("separator");

    private static void ReadCsv(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var rows = DelimitedFile.Read(
            run.Devices.Files.ReadText(path, EncodingOf(step)),
            Separator(run, step),
            run.Flag(step, "skipBlankLines", true),
            run.Flag(step, "trimCells", false));

        // The line the step starts at is the line the header is on, when there is one: "start at
        // row 3" is where reading begins, and everything else follows from there.
        var wanted = rows.Skip(Math.Max(1, run.Args(step).Number("startRow")) - 1);
        var header = run.Flag(step, "hasHeader", true) ? wanted.FirstOrDefault() : null;
        var body = header is null ? wanted : wanted.Skip(1);

        var limit = run.Args(step).Number("maxRows");
        if (limit > 0)
        {
            body = body.Take(limit);
        }

        // A CSV cell is text: a file that holds 1,234 or 2026-01-31 says nothing about whether that
        // is a number or a date, and guessing would be a macro quietly working on the wrong thing.
        var table = Narrow(run, step,
            header?.Select(Value.FromText).ToList(),
            body.Select(row => (IReadOnlyList<Value>)[.. row.Select(Value.FromText)]));
        run.Store(step, "rows", Shaped(run, step, table, path), depth);

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ReadFile, path, table.Rows.Count);
    }

    private static void WriteCsv(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var separator = DelimitedFile.Named(Separator(run, step))[0];
        var rows = run.Args(step).ValueOf("rows");
        if (!rows.IsList)
        {
            throw new StepFailure(RunKeys.NotAList, run.Args(step).Text("rows"));
        }

        var append = string.Equals(run.Args(step).Text("writeMode").Trim(), "append",
            StringComparison.OrdinalIgnoreCase);
        var table = new List<IReadOnlyList<string>>();

        // Appending to a file that is not there yet is the first line of a log, so the header goes
        // in with it; appending to one that is already there would leave a second header in the
        // middle of the data, which is the one thing nobody wants.
        var header = Header(run, step, separator);
        if (header.Count > 0 && (!append || !run.Devices.Files.Exists(path)))
        {
            table.Add(header);
        }

        // Adding by name is for the file somebody else's program keeps: the names in its first line
        // say which column is which, and the names of this step's cells say where each one goes, so
        // a column the file has and this step knows nothing about does not shift the rest along.
        var standing = run.Flag(step, "align", false) && append && run.Devices.Files.Exists(path)
            ? Standing(run, path, separator, step)
            : [];

        table.AddRange(rows.Items.Select(row => standing.Count > 0
            ? Placed(Cells(row), standing, RowNames(row, header))
            : (IReadOnlyList<string>)[.. Cells(row).Select(cell => cell.AsText())]));

        var text = DelimitedFile.Write(table, separator.ToString(), LineEnding(step),
            run.Flag(step, "quoteAll", false),
            string.Equals(run.Args(step).Text("emptyCells").Trim(), "quoted",
                StringComparison.OrdinalIgnoreCase));
        run.Devices.Files.WriteText(path, text, append, EncodingOf(step));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.WroteFile, path, table.Count);
    }

    /// <summary>
    /// The names the file already has, which is the line a log names its columns on. A file with
    /// nothing in it has no names, and adding to it is then an ordinary add.
    /// </summary>
    private static IReadOnlyList<string> Standing(IRunContext run, string path, char separator,
        ExecutableStep step)
    {
        var rows = DelimitedFile.Read(run.Devices.Files.ReadText(path, EncodingOf(step)),
            separator.ToString(), skipBlankLines: false, trim: false);

        return rows.Count == 0 ? [] : rows[0];
    }

    /// <summary>
    /// One row of a CSV put where the file's own names say it goes. A cell the file has no column
    /// for, and a row with more cells than there are names for them, are both said out loud: the
    /// alternative is a value landing in whatever column happens to be tenth.
    /// </summary>
    private static IReadOnlyList<string> Placed(IReadOnlyList<Value> cells,
        IReadOnlyList<string> standing, IReadOnlyList<string> names)
    {
        if (names.Count < cells.Count)
        {
            throw new StepFailure(RunKeys.AlignNeedsNames,
                cells.Count.ToString(CultureInfo.InvariantCulture));
        }

        var line = new string[standing.Count];
        Array.Fill(line, string.Empty);
        for (var at = 0; at < cells.Count; at++)
        {
            line[ColumnOf(standing, names[at])] = cells[at].AsText();
        }

        return line;
    }

    /// <summary>The reading of a name for a table whose names came back as plain text, as a CSV's do.</summary>
    private static int ColumnOf(IReadOnlyList<string> names, string wanted)
        => ColumnOf([.. names.Select(Value.FromText)], wanted);

    /// <summary>
    /// The names to put above the data: a list variable, or the names written out on one line the
    /// way a row of the file would be. Nothing asked for is no names at all.
    /// </summary>
    private static IReadOnlyList<string> Header(IRunContext run, ExecutableStep step, char separator)
    {
        var text = run.Args(step).Text("header").Trim();
        if (text.Length == 0)
        {
            return [];
        }

        // The field is a line, so a name in it is worked out here rather than read from a shape the
        // way an ordinary field is: a macro writes "the columns I have" as the name of its list.
        var value = Expression.Evaluate(Formula.Text(text), run.Variables);
        if (value.IsList)
        {
            return [.. value.Items.Select(item => item.AsText())];
        }

        var written = value.AsText();
        return written.Length == 0
            ? []
            : [.. written.Split(separator, StringSplitOptions.TrimEntries)];
    }

    /// <summary>Which characters end a line, as the step asked for them.</summary>
    private static string LineEnding(ExecutableStep step)
        => step.Text("lineEnding").Trim().ToLowerInvariant() switch
        {
            "unix" or "lf" or "\\n" => "\n",
            _ => "\r\n",
        };

    /// <summary>
    /// One table a step read: the names above the columns, when the table has them, and the rows
    /// under them.
    /// </summary>
    private sealed record Table(IReadOnlyList<Value>? Header,
        IReadOnlyList<IReadOnlyList<Value>> Rows);

    /// <summary>
    /// The part of a table the step asked for: the rows that match, and then the columns it named.
    /// Reading a CSV takes the same words and means the same thing however many rows the file has,
    /// so what a step says about one table it says about them all.
    /// </summary>
    private static Table Narrow(IRunContext run, ExecutableStep step,
        IReadOnlyList<Value>? header, IEnumerable<IReadOnlyList<Value>> body)
    {
        var match = ColumnOf(run, step, header, "matchColumn");
        var wanted = run.Args(step).ValueOf("matchValue");
        var mode = run.Args(step).Text("filterMode");

        // Which rows to keep is worked out before the columns are taken off, because the column a
        // row is matched on need not be one of the columns that come back.
        var rows = body
            .Where(row => match < 0
                || run.CaseMatches(step, Cell(row, match), wanted.AsText(), mode))
            .ToList();

        var keep = Kept(run, step, header);
        if (keep.Count == 0)
        {
            return new Table(header, rows);
        }

        return new Table(
            header is null ? null : Project(header, keep),
            [.. rows.Select(row => Project(row, keep))]);
    }

    /// <summary>
    /// What the result variable holds. A list of rows is what a table is; a plain list of one
    /// column's values is what a macro walks through or adds up, and making that jump through a
    /// one-cell row first is the difference between reading a column and reading a table. A row is a
    /// record whose keys are the names written above the columns, so a macro reads a cell by the
    /// name it can see in the file rather than by counting along the line — which is how a table
    /// comes to be read out of the wrong column without anything saying so.
    /// </summary>
    private static Value Shaped(IRunContext run, ExecutableStep step, Table table, string path)
    {
        var width = table.Header?.Count ?? table.Rows.FirstOrDefault()?.Count ?? 0;

        if (string.Equals(run.Args(step).Text("shape").Trim(), "values",
                StringComparison.OrdinalIgnoreCase))
        {
            if (width > 1)
            {
                throw new StepFailure(RunKeys.OneColumnOnly, path);
            }

            return Value.FromList([.. table.Rows.Select(row => Cell(row, 0))]);
        }

        var names = TableColumns.Names(table.Header, width);
        return Value.FromList(
        [
            .. table.Rows.Select(row => Value.FromRecord(
                Enumerable.Range(0, width).Select(index => (
                    names[index],
                    index < row.Count ? row[index] : Value.FromText(string.Empty))))),
        ]);
    }

    /// <summary>The columns a step named, in the order it named them; empty means all of them.</summary>
    private static IReadOnlyList<int> Kept(IRunContext run, ExecutableStep step,
        IReadOnlyList<Value>? header)
    {
        var wanted = run.Args(step).Text("columns").Trim();
        if (wanted.Length == 0)
        {
            return [];
        }

        return
        [
            .. wanted
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => ColumnOf(header, name)),
        ];
    }

    /// <summary>Which column a step named with one of its own fields, or -1 when it named none.</summary>
    private static int ColumnOf(IRunContext run, ExecutableStep step, IReadOnlyList<Value>? header,
        string parameter)
    {
        var name = run.Args(step).Text(parameter).Trim();
        return name.Length == 0 ? -1 : ColumnOf(header, name);
    }

    /// <summary>
    /// Which column a step means: the name written above it when the table has names, or the way a
    /// person writes a column — <c>B</c>, or <c>3</c> for the third one. A name the table does not
    /// have is a step asking for the wrong thing, and says so rather than reading the wrong column.
    /// </summary>
    private static int ColumnOf(IReadOnlyList<Value>? header, string wanted)
    {
        var written = wanted.Trim();
        if (header is not null)
        {
            for (var at = 0; at < header.Count; at++)
            {
                if (string.Equals(header[at].AsText().Trim(), written,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return at;
                }
            }
        }

        if (Letters(written) is { } letter)
        {
            return letter;
        }

        if (int.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var number) && number >= 1)
        {
            return number - 1;
        }

        throw new StepFailure(RunKeys.NoSuchColumn, written);
    }

    /// <summary>The place a column letter names, counted from zero, or null when it is not letters.</summary>
    private static int? Letters(string text)
    {
        if (text.Length == 0 || text.Length > 3)
        {
            return null;
        }

        var place = 0;
        foreach (var character in text.ToUpperInvariant())
        {
            if (character is < 'A' or > 'Z')
            {
                return null;
            }

            place = place * 26 + (character - 'A' + 1);
        }

        return place - 1;
    }

    /// <summary>The cell in one column of a row, or an empty one when the row stops before it.</summary>
    private static Value Cell(IReadOnlyList<Value> row, int at)
        => at >= 0 && at < row.Count ? row[at] : Value.FromText(string.Empty);

    /// <summary>One row of a table, with only the named columns left in it and in that order.</summary>
    private static IReadOnlyList<Value> Project(IReadOnlyList<Value> row, IReadOnlyList<int> keep)
        => [.. keep.Select(at => Cell(row, at))];

    /// <summary>
    /// One row of a list as the cells of a line hold them. A row that is not a list is a single
    /// cell, which is what a macro that writes one value means.
    /// </summary>
    private static IReadOnlyList<Value> Cells(Value row) => row.Kind switch
    {
        // A record writes its fields in the order they are written in, which is the order the row
        // was read in: writing back a table read from a file gives the file it came from.
        ValueKind.Record => [.. row.Fields.Select(field => field.Value)],
        ValueKind.List => row.Items,
        _ => [row],
    };

    /// <summary>
    /// What each cell of a row is called on its way into a file. A record names its own cells, which
    /// is what lets a row be put under the file's own columns by name; anything else takes the names
    /// the step wrote out above them.
    /// </summary>
    private static IReadOnlyList<string> RowNames(Value row, IReadOnlyList<string> written)
        => row.IsRecord ? [.. row.Fields.Select(field => field.Name)] : written;

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
                document[name] = ToJson(value);
            }
        }

        var text = document.ToJsonString(Writable);
        run.Devices.Files.WriteText(path, text, false, EncodingOf(step));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.SavedVariables, path, document.Count);
    }

    private static void LoadVariables(IRunContext run, ExecutableStep step, int depth)
    {
        var path = run.PathOf(step);
        var document = Json(path, run.Devices.Files.ReadText(path, EncodingOf(step)));
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
            target.Set(name, FromJson(node));
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.LoadedVariables, path, saved.Count);
    }
}
