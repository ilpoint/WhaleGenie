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

public sealed partial class MacroRunner
{
    // --------------------------------------------------------------------- files

    /// <summary>A path a step named, with any <c>$variable</c> already filled in.</summary>
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

    private void FileExists(ExecutableStep step, int depth)
    {
        var found = _devices.Files.Exists(PathOf(step));
        Store(step, "exists", Value.FromBool(found), depth);
    }

    private void DeleteFile(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var recycle = Flag(step, "toRecycleBin", false);
        _devices.Files.Delete(path, recycle);
        Log(LogLevel.Info, depth, step.Type,
            recycle ? LogKeys.RecycledFile : LogKeys.DeletedFile, path);
    }

    private void CopyFile(ExecutableStep step, int depth)
    {
        var from = PathOf(step, "from");
        var (to, go) = Landing(step, PathOf(step, "to"));
        if (!go)
        {
            Give(step, "path", Value.FromText(_devices.Files.Resolve(to)));
            Log(LogLevel.Info, depth, step.Type, LogKeys.FileLeftAlone, to);
            return;
        }

        _devices.Files.Copy(from, to);
        Give(step, "path", Value.FromText(_devices.Files.Resolve(to)));
        Log(LogLevel.Info, depth, step.Type, LogKeys.CopiedFile, from, to);
    }

    private void MoveFile(ExecutableStep step, int depth)
    {
        var from = PathOf(step, "from");
        var (to, go) = Landing(step, PathOf(step, "to"));
        if (!go)
        {
            Give(step, "path", Value.FromText(_devices.Files.Resolve(to)));
            Log(LogLevel.Info, depth, step.Type, LogKeys.FileLeftAlone, to);
            return;
        }

        _devices.Files.Move(from, to);
        Give(step, "path", Value.FromText(_devices.Files.Resolve(to)));
        Log(LogLevel.Info, depth, step.Type, LogKeys.MovedFile, from, to);
    }

    /// <summary>
    /// Where a copy or a move is to land, and what to do about a file already sitting there: put
    /// this one over it, leave the one that is there alone, or give the new one a number of its own
    /// beside it. Whether the name is taken is a question only the machine can answer.
    /// </summary>
    private (string To, bool Go) Landing(ExecutableStep step, string to)
    {
        var choice = ReadText(step, "ifExists").Trim().ToLowerInvariant();
        if (!_devices.Files.Exists(to))
        {
            return (to, true);
        }

        return choice switch
        {
            "skip" => (to, false),
            "unique" => (Beside(to), true),
            _ => (to, true),
        };
    }

    /// <summary>
    /// A free name next to a taken one — report (2).csv, then report (3).csv — which is the way
    /// Windows itself gives a second file a name of its own.
    /// </summary>
    private string Beside(string path)
    {
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var number = 2; number < 10_000; number++)
        {
            var candidate = Path.Combine(folder, $"{stem} ({number}){extension}");
            if (!_devices.Files.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new StepFailure(RunKeys.NoFreeName, path);
    }

    private void CreateFolder(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        _devices.Files.CreateFolder(path);
        Log(LogLevel.Info, depth, step.Type, LogKeys.MadeFolder, path);
    }

    private void DeleteFolder(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var recurse = Flag(step, "recurse", false);

        _devices.Files.DeleteFolder(path, recurse);
        Log(LogLevel.Info, depth, step.Type, LogKeys.RemovedFolder, path);
    }

    /// <summary>
    /// Works out one piece of a path. A macro that has to say "the file next to this one" or "the
    /// same name with .bak on the end" should not have to spell the whole path out again, and the
    /// two folder answers are the ones that are always in the same place on any machine.
    /// </summary>
    private void PathPart(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var other = ReadValue(step, "fileName").AsText().Trim();

        var answer = ReadText(step, "operation").Trim().ToLowerInvariant() switch
        {
            "folder" => Path.GetDirectoryName(path) ?? string.Empty,
            "name" => Path.GetFileName(path),
            "basename" => Path.GetFileNameWithoutExtension(path),
            "extension" => Path.GetExtension(path),
            "full" => _devices.Files.Resolve(path),
            "temp" => Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar),
            "macros" => _devices.Files.BaseFolder,
            _ => other.Length == 0 ? path : Path.Combine(path, other),
        };

        Store(step, "path", Value.FromText(answer), depth);
    }

    private void UnzipFile(ExecutableStep step, int depth)
    {
        var from = PathOf(step, "from");
        var folder = PathOf(step, "folder");
        var overwrite = Flag(step, "overwrite", true);

        _devices.Files.Unzip(from, folder, overwrite);
        Log(LogLevel.Info, depth, step.Type, LogKeys.Unzipped, from, folder);
    }

    private void ZipFolder(ExecutableStep step, int depth)
    {
        var folder = PathOf(step, "folder");
        var to = PathOf(step, "to");

        _devices.Files.Zip(folder, to);
        Log(LogLevel.Info, depth, step.Type, LogKeys.Zipped, folder, to);
    }

    private void ListFiles(ExecutableStep step, int depth)
    {
        var folder = PathOf(step, "folder");
        var recurse = Flag(step, "recurse", false);
        // A filter said with a semi-colon is several filters, the way the action this is modelled
        // on reads it: *.txt; *.csv is one question, not two steps.
        var patterns = ReadText(step, "pattern")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var levels = Number(step, "depth");
        var files = _devices.Files.List(folder, patterns, recurse,
            recurse ? (levels <= 0 ? -1 : levels) : 0);

        var since = ChangedSince(step);
        var smallest = Number(step, "minKb") * 1024L;
        var largest = Number(step, "maxKb") * 1024L;
        var kept = files.Where(entry =>
            (since is null || entry.Modified >= since)
            && (smallest <= 0 || entry.Size >= smallest)
            && (largest <= 0 || entry.Size <= largest));

        // A listing a macro loops over is the same listing on every run, so the folder's own order
        // is never the answer: an order is always asked for, and the name settles the ties.
        var ordered = InOrder(kept, ReadText(step, "sortBy").Trim(), Flag(step, "descending", false));
        var root = Flag(step, "relative", false) ? _devices.Files.Resolve(folder) : "";
        var paths = ordered
            .Select(entry => root.Length == 0 ? entry.Path : Path.GetRelativePath(root, entry.Path))
            .ToList();

        Store(step, "files", Value.FromList(paths.Select(Value.FromText)), depth);
        Log(LogLevel.Info, depth, step.Type, LogKeys.ListedFiles, folder, paths.Count);
    }

    /// <summary>
    /// The earliest a listing reaches back to. Nothing said is every file, which is what a step
    /// that only wants what is in a folder expects.
    /// </summary>
    private DateTimeOffset? ChangedSince(ExecutableStep step)
    {
        var count = Number(step, "withinCount");
        if (count <= 0)
        {
            return null;
        }

        var back = ReadText(step, "withinUnit").Trim().ToLowerInvariant() switch
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

    private void ReadJson(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var document = Json(path, _devices.Files.ReadText(path, EncodingOf(step)));
        var query = ReadValue(step, "query").AsText().Trim();
        var node = query.Length == 0
            ? document
            : Select(document, query) ?? throw new StepFailure(RunKeys.BadJsonPath, query);

        var value = FromJson(node);
        Store(step, "value", value, depth);
    }

    private void WriteJson(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var query = ReadValue(step, "query").AsText().Trim();
        if (query.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingJsonPath);
        }

        var document = _devices.Files.Exists(path)
            ? Json(path, _devices.Files.ReadText(path, EncodingOf(step)))
            : new JsonObject();

        Assign(document, query, ToJson(ReadValue(step, "writeValue")));

        var text = document.ToJsonString(Writable);
        _devices.Files.WriteText(path, text, false, EncodingOf(step));
        Log(LogLevel.Info, depth, step.Type, LogKeys.WroteFile, path, text.Length);
    }

    private void ReadCsv(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var rows = DelimitedFile.Read(
            _devices.Files.ReadText(path, EncodingOf(step)),
            Separator(step),
            Flag(step, "skipBlankLines", true),
            Flag(step, "trimCells", false));

        // The line the step starts at is the line the header is on, when there is one: "start at
        // row 3" is where reading begins, and everything else follows from there.
        var wanted = rows.Skip(Math.Max(1, Number(step, "startRow")) - 1);
        var header = Flag(step, "hasHeader", true) ? wanted.FirstOrDefault() : null;
        var body = header is null ? wanted : wanted.Skip(1);

        var limit = Number(step, "maxRows");
        if (limit > 0)
        {
            body = body.Take(limit);
        }

        // A CSV cell is text: a file that holds 1,234 or 2026-01-31 says nothing about whether that
        // is a number or a date, and guessing would be a macro quietly working on the wrong thing.
        var table = Narrow(step,
            header?.Select(Value.FromText).ToList(),
            body.Select(row => (IReadOnlyList<Value>)[.. row.Select(Value.FromText)]));
        Store(step, "rows", Shaped(step, table, path), depth);

        Log(LogLevel.Info, depth, step.Type, LogKeys.ReadFile, path, table.Rows.Count);
    }

    private void WriteCsv(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var separator = DelimitedFile.Named(Separator(step))[0];
        var rows = ReadValue(step, "rows");
        if (!rows.IsList)
        {
            throw new StepFailure(RunKeys.NotAList, ReadText(step, "rows"));
        }

        var append = string.Equals(ReadText(step, "writeMode").Trim(), "append", StringComparison.OrdinalIgnoreCase);
        var table = new List<IReadOnlyList<string>>();

        // Appending to a file that is not there yet is the first line of a log, so the header goes
        // in with it; appending to one that is already there would leave a second header in the
        // middle of the data, which is the one thing nobody wants.
        var header = Header(step, separator);
        if (header.Count > 0 && (!append || !_devices.Files.Exists(path)))
        {
            table.Add(header);
        }

        // Adding by name is for the file somebody else's program keeps: the names in its first line
        // say which column is which, and the names of this step's cells say where each one goes, so
        // a column the file has and this step knows nothing about does not shift the rest along.
        var standing = Flag(step, "align", false) && append && _devices.Files.Exists(path)
            ? Standing(path, separator, step)
            : [];

        table.AddRange(rows.Items.Select(row => standing.Count > 0
            ? Placed(Cells(row), standing, RowNames(row, header))
            : (IReadOnlyList<string>)[.. Cells(row).Select(cell => cell.AsText())]));

        var text = DelimitedFile.Write(table, separator.ToString(), LineEnding(step),
            Flag(step, "quoteAll", false),
            string.Equals(ReadText(step, "emptyCells").Trim(), "quoted", StringComparison.OrdinalIgnoreCase));
        _devices.Files.WriteText(path, text, append, EncodingOf(step));
        Log(LogLevel.Info, depth, step.Type, LogKeys.WroteFile, path, table.Count);
    }

    /// <summary>
    /// The names the file already has, which is the line a log names its columns on. A file with
    /// nothing in it has no names, and adding to it is then an ordinary add.
    /// </summary>
    private IReadOnlyList<string> Standing(string path, char separator, ExecutableStep step)
    {
        var rows = DelimitedFile.Read(_devices.Files.ReadText(path, EncodingOf(step)),
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
    private IReadOnlyList<string> Header(ExecutableStep step, char separator)
    {
        var text = ReadText(step, "header").Trim();
        if (text.Length == 0)
        {
            return [];
        }

        var value = Read(text);
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
    private sealed record Table(IReadOnlyList<Value>? Header, IReadOnlyList<IReadOnlyList<Value>> Rows);

    /// <summary>
    /// The part of a table the step asked for: the rows that match, and then the columns it named.
    /// Reading a CSV takes the same words and means the same thing however many rows the file has,
    /// so what a step says about one table it says about them all.
    /// </summary>
    private Table Narrow(ExecutableStep step, IReadOnlyList<Value>? header,
        IEnumerable<IReadOnlyList<Value>> body)
    {
        var match = ColumnOf(step, header, "matchColumn");
        var wanted = ReadValue(step, "matchValue");
        var mode = ReadText(step, "filterMode");

        // Which rows to keep is worked out before the columns are taken off, because the column a
        // row is matched on need not be one of the columns that come back.
        var rows = body
            .Where(row => match < 0
                || CaseMatches(step, Cell(row, match), wanted.AsText(), mode))
            .ToList();

        var keep = Kept(step, header);
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
    private Value Shaped(ExecutableStep step, Table table, string path)
    {
        var width = table.Header?.Count ?? table.Rows.FirstOrDefault()?.Count ?? 0;

        if (string.Equals(ReadText(step, "shape").Trim(), "values", StringComparison.OrdinalIgnoreCase))
        {
            if (width > 1)
            {
                throw new StepFailure(RunKeys.OneColumnOnly, path);
            }

            return Value.FromList([.. table.Rows.Select(row => Cell(row, 0))]);
        }

        var names = ColumnNames(table.Header, width);
        return Value.FromList(
        [
            .. table.Rows.Select(row => Value.FromRecord(
                Enumerable.Range(0, width).Select(index => (
                    names[index],
                    index < row.Count ? row[index] : Value.FromText(string.Empty))))),
        ]);
    }

    /// <summary>
    /// What each cell of a row is called: the name written above it, or — for a file whose first row
    /// is not a header — the letter of its column, which is the same name the "only these columns"
    /// field takes. A column nobody named keeps its letter rather than taking the name of the one
    /// before it, and so does a column that repeats a name already taken: two columns under one name
    /// would put one of them out of reach of a record.
    /// </summary>
    private static IReadOnlyList<string> ColumnNames(IReadOnlyList<Value>? header, int width)
    {
        var names = new List<string>(width);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < width; index++)
        {
            var written = index < (header?.Count ?? 0) ? header![index].AsText().Trim() : string.Empty;
            var name = written.Length > 0 && taken.Add(written) ? written : Letter(index);
            names.Add(name);
            taken.Add(name);
        }

        return names;
    }

    /// <summary>The way a person writes a column place on its own: A, B, ... Z, AA.</summary>
    private static string Letter(int place)
    {
        var text = string.Empty;
        for (var at = place; at >= 0; at = (at / 26) - 1)
        {
            text = (char)('A' + (at % 26)) + text;
        }

        return text;
    }

    /// <summary>The columns a step named, in the order it named them; empty means all of them.</summary>
    private IReadOnlyList<int> Kept(ExecutableStep step, IReadOnlyList<Value>? header)
    {
        var wanted = ReadText(step, "columns").Trim();
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
    private int ColumnOf(ExecutableStep step, IReadOnlyList<Value>? header, string parameter)
    {
        var name = ReadText(step, parameter).Trim();
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
                if (string.Equals(header[at].AsText().Trim(), written, StringComparison.OrdinalIgnoreCase))
                {
                    return at;
                }
            }
        }

        if (Letters(written) is { } letter)
        {
            return letter;
        }

        if (int.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            && number >= 1)
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

    private void SaveVariables(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var chosen = ReadText(step, "names").Trim();
        var names = chosen.Length > 0
            ? chosen.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : [.. Variables.Local.Values.Keys
                .Concat(Variables.Global.Values.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)];

        var document = new JsonObject();
        foreach (var name in names)
        {
            if (Variables.TryGet(name, out var value))
            {
                document[name] = ToJson(value);
            }
        }

        var text = document.ToJsonString(Writable);
        _devices.Files.WriteText(path, text, false, EncodingOf(step));
        Log(LogLevel.Info, depth, step.Type, LogKeys.SavedVariables, path, document.Count);
    }

    private void LoadVariables(ExecutableStep step, int depth)
    {
        var path = PathOf(step);
        var document = Json(path, _devices.Files.ReadText(path, EncodingOf(step)));
        if (document is not JsonObject saved)
        {
            throw new StepFailure(RunKeys.BadJson, path);
        }

        foreach (var (name, node) in saved)
        {
            // Shared variables are only written to when the Variable Center already made one,
            // the same rule the "set variable" step follows.
            var target = Variables.Global.Contains(name) ? VariableTarget.Global : VariableTarget.Local;
            Variables.Set(name, FromJson(node), target);
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.LoadedVariables, path, saved.Count);
    }

    /// <summary>
    /// Encodes text as Base64. The text is read the way any other value is and then taken as
    /// UTF-8, which is the encoding every service a Base64 value is handed to expects.
    /// </summary>
    private void EncodeBase64(ExecutableStep step, int depth)
    {
        var text = ReadValue(step, "dataText").AsText();
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        Store(step, "encoded", Value.FromText(encoded), depth);
    }

    /// <summary>
    /// Decodes Base64 back into text. Text that is not Base64 fails the step rather than turning
    /// into a value the macro cannot explain: a macro that decoded nothing should say so.
    /// </summary>
    private void DecodeBase64(ExecutableStep step, int depth)
    {
        var text = ReadValue(step, "dataText").AsText();
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(text.Trim());
        }
        catch (FormatException)
        {
            throw new StepFailure(RunKeys.BadBase64, text);
        }

        Store(step, "decoded", Value.FromText(Encoding.UTF8.GetString(bytes)), depth);
    }

    /// <summary>
    /// Works out the checksum of a piece of text or of a file. A file is walked over a piece at a
    /// time rather than read whole, because the files a checksum is asked about — an installer, a
    /// download, a backup — are exactly the ones that do not fit in memory.
    /// </summary>
    private void HashText(ExecutableStep step, int depth)
    {
        var algorithm = ReadText(step, "hashAlgorithm");
        byte[] digest;
        if (string.Equals(ReadText(step, "hashSource").Trim(), "file", StringComparison.OrdinalIgnoreCase))
        {
            var path = PathOf(step);
            using var file = _devices.Files.OpenRead(path);
            digest = ComputeHash(algorithm, file);
        }
        else
        {
            var text = ReadValue(step, "dataText").AsText();
            digest = ComputeHash(algorithm, new MemoryStream(Encoding.UTF8.GetBytes(text)));
        }

        Store(step, "digest", Value.FromText(Convert.ToHexString(digest).ToLowerInvariant()), depth);
    }

    /// <summary>
    /// The checksum a named algorithm works out. An unrecognized name falls back to SHA-256 rather
    /// than failing: a step that asked for a checksum should get one the macro can compare.
    /// </summary>
    private static byte[] ComputeHash(string algorithm, Stream bytes) =>
        algorithm.Trim().ToLowerInvariant() switch
        {
            "md5" => MD5.HashData(bytes),
            "sha1" => SHA1.HashData(bytes),
            "sha512" => SHA512.HashData(bytes),
            _ => SHA256.HashData(bytes),
        };

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

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position)
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

    /// <summary>The character between two CSV cells.</summary>
    /// <summary>
    /// What the step said separates its cells: the character written beside the picker when there
    /// is one, and otherwise the one that was picked — which may be <c>auto</c>, and then the file
    /// itself decides.
    /// </summary>
    private string Separator(ExecutableStep step)
        => ReadText(step, "separatorText").Trim().Length > 0
            ? ReadText(step, "separatorText")
            : ReadText(step, "separator");


    /// <summary>
    /// The file actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchFile(ExecutableStep step, int depth,
        CancellationToken token)
    {
        switch (step.Type)
        {
            case "file.readText":
                ReadTextFile(step, depth);
                return Signal.Normal;
            case "file.writeText":
                WriteTextFile(step, depth);
                return Signal.Normal;
            case "file.appendLog":
                AppendLog(step, depth);
                return Signal.Normal;
            case "file.zip":
                ZipFolder(step, depth);
                return Signal.Normal;
            case "file.exists":
                FileExists(step, depth);
                return Signal.Normal;
            case "file.delete":
                DeleteFile(step, depth);
                return Signal.Normal;
            case "file.copy":
                CopyFile(step, depth);
                return Signal.Normal;
            case "file.move":
                MoveFile(step, depth);
                return Signal.Normal;
            case "file.createFolder":
                CreateFolder(step, depth);
                return Signal.Normal;
            case "file.deleteFolder":
                DeleteFolder(step, depth);
                return Signal.Normal;
            case "file.path":
                PathPart(step, depth);
                return Signal.Normal;
            case "file.unzip":
                UnzipFile(step, depth);
                return Signal.Normal;
            case "file.listFiles":
                ListFiles(step, depth);
                return Signal.Normal;
            case "file.readJson":
                ReadJson(step, depth);
                return Signal.Normal;
            case "file.writeJson":
                WriteJson(step, depth);
                return Signal.Normal;
            case "file.readCsv":
                ReadCsv(step, depth);
                return Signal.Normal;
            case "file.writeCsv":
                WriteCsv(step, depth);
                return Signal.Normal;
            case "file.saveVariables":
                SaveVariables(step, depth);
                return Signal.Normal;
            case "file.loadVariables":
                LoadVariables(step, depth);
                return Signal.Normal;

            // ----------------------------------------------------------------- data
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }

    /// <summary>
    /// The data actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchData(ExecutableStep step, int depth,
        CancellationToken token)
    {
        switch (step.Type)
        {
            case "data.base64Encode":
                EncodeBase64(step, depth);
                return Signal.Normal;
            case "data.base64Decode":
                DecodeBase64(step, depth);
                return Signal.Normal;
            case "data.hash":
                HashText(step, depth);
                return Signal.Normal;

            // -------------------------------------------------------------- clipboard
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }
}
