using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// CSV both ways: a file read into rows whose cells are named by the line above them, and rows
/// written back out with the same names. Which column a step means is decided here, so reading and
/// writing agree on what "the column called age" is.
/// </summary>
internal static class FileCsvActions
{
    /// <summary>
    /// The CSV actions, each answered by the method written for it. A step of another family is
    /// not in this table.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, RunActionHandler> Map =
        new Dictionary<string, RunActionHandler>(StringComparer.Ordinal)
        {
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
        };

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
            run.Devices.Files.ReadText(path, FileActions.EncodingOf(step)),
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
        run.Devices.Files.WriteText(path, text, append, FileActions.EncodingOf(step));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.WroteFile, path, table.Count);
    }

    /// <summary>
    /// The names the file already has, which is the line a log names its columns on. A file with
    /// nothing in it has no names, and adding to it is then an ordinary add.
    /// </summary>
    private static IReadOnlyList<string> Standing(IRunContext run, string path, char separator,
        ExecutableStep step)
    {
        var rows = DelimitedFile.Read(run.Devices.Files.ReadText(path, FileActions.EncodingOf(step)),
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

}
