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

// JSON and CSV: reading one into a variable and writing one back out.
public sealed partial class MacroRunner
{
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
}
