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
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// The table a CSV becomes: its header, rows, columns and the cells a step keeps.
public sealed partial class MacroRunner
{
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
        var text = Args(step).Text("header").Trim();
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
        var wanted = Args(step).ValueOf("matchValue");
        var mode = Args(step).Text("filterMode");

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

        if (string.Equals(Args(step).Text("shape").Trim(), "values", StringComparison.OrdinalIgnoreCase))
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
        var wanted = Args(step).Text("columns").Trim();
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
        var name = Args(step).Text(parameter).Trim();
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
}
