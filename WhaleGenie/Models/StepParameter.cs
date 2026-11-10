using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// One captured parameter value and the row shape a table parameter carries.
/// <summary>
/// One parameter value captured for a step. The value stays as text while the
/// editor is open and is converted to its JSON type only when the tree is written.
/// </summary>
public class StepParameter
{
    public required string Name { get; init; }

    public required ActionParameterKind Kind { get; init; }

    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// How much the value may move from run to run, written as a fraction of it: 0.2 is "20%
    /// either way", so 500 lands somewhere between 400 and 600. Only lengths of time are given
    /// one, which is what keeps a wait from looking mechanical.
    /// </summary>
    public decimal Jitter { get; init; }

    /// <summary>True when the value was written with some give in it.</summary>
    public bool HasJitter => Jitter > 0m;

    /// <summary>Child steps, filled in when <see cref="Kind"/> is <see cref="ActionParameterKind.Steps"/>.</summary>
    public List<MacroStep> Steps { get; init; } = [];

    /// <summary>
    /// Whether this list is open in the editor. A block that runs several lists gives each of them
    /// a row of its own, and those rows fold like a block does. It is the editor's memory rather
    /// than part of the macro, so it is never written to the file.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// The rows of a parameter that holds a list of them, filled in when <see cref="Kind"/> is
    /// <see cref="ActionParameterKind.Region"/>, <see cref="ActionParameterKind.KeySequence"/> or
    /// <see cref="ActionParameterKind.Images"/>; a <see cref="ActionParameterKind.Returns"/> holds
    /// one row per result the step hands over, a <see cref="ActionParameterKind.Narrow"/> one row
    /// per thing the element has to be like, and a <see cref="ActionParameterKind.ColourPoints"/>
    /// one row per colour the pattern wants beside its base colour. What the columns of a row mean
    /// belongs to the action, so this holds the names and what they say rather than a shape of its
    /// own.
    /// </summary>
    public List<StepParameterRow> Rows { get; init; } = [];

    /// <summary>
    /// True when the value is a list of rows rather than one thing. Those are the parameters whose
    /// value is written in the file as an array of objects, and what a reader makes of one depends
    /// on the action rather than on the shape: every place a step looks is a name it has to have,
    /// while every press of a key run is only itself.
    /// </summary>
    public bool IsRowList => Kind is ActionParameterKind.Region
        or ActionParameterKind.KeySequence or ActionParameterKind.Images or ActionParameterKind.Returns
        or ActionParameterKind.Arguments or ActionParameterKind.Narrow
        or ActionParameterKind.ColourPoints;

    /// <summary>Everything written in the rows of a list parameter, by row.</summary>
    public IEnumerable<string> RowValues => Rows.SelectMany(row => row.Columns.Values);

    /// <summary>Writes the value as the JSON type the runtime expects.</summary>
    /// <remarks>
    /// A number is written as the number it is, and anything else is written as what it says. A
    /// number field may hold an expression — <c>$sys.mouseX</c>, the place the pointer is at when
    /// the step runs — which the engine reads and works out; writing that as a zero because it is
    /// not digits would save a step that clicks the corner of the screen instead of the mouse, and
    /// the loss would be silent, because the file it is read back from says a number.
    /// </remarks>
    public JsonNode? ToJson() => Kind switch
    {
        ActionParameterKind.Number => NumberJson(),
        ActionParameterKind.Bool => JsonValue.Create(
            string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase)),
        ActionParameterKind.Steps => BuildArray(Steps),
        ActionParameterKind.Region or ActionParameterKind.KeySequence or ActionParameterKind.Images
            or ActionParameterKind.Returns or ActionParameterKind.Arguments
            or ActionParameterKind.Narrow or ActionParameterKind.ColourPoints => RowsJson(),
        ActionParameterKind.WindowRule when Value.TrimStart().StartsWith('{')
            => JsonNode.Parse(Value),
        _ => JsonValue.Create(Value),
    };

    /// <summary>
    /// A number field as the file holds it: the number it says, or the text of the expression it
    /// says instead. A field nobody answered — only ever written by hand, because the editor leaves
    /// an unanswered number out of the step — stays the zero it has always been read as.
    /// </summary>
    private JsonNode? NumberJson()
    {
        if (Value.Trim().Length == 0)
        {
            return JsonValue.Create(0m);
        }

        return decimal.TryParse(Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? JsonValue.Create(number)
            : JsonValue.Create(Value);
    }

    /// <summary>
    /// The rows as a JSON array with one object per row and one member per column. A column that
    /// says a number is written as one, so the file reads the way the field was filled in, and a
    /// column that says nothing is left out rather than written down as nothing.
    /// </summary>
    private JsonArray RowsJson()
    {
        var array = new JsonArray();
        foreach (var row in Rows)
        {
            var written = new JsonObject();
            foreach (var (column, value) in row.Columns)
            {
                if (value.Length == 0)
                {
                    continue;
                }

                written[column] = decimal.TryParse(value, NumberStyles.Number,
                    CultureInfo.InvariantCulture, out var number)
                        ? JsonValue.Create(number)
                        : JsonValue.Create(value);
            }

            if (written.Count > 0)
            {
                array.Add(written);
            }
        }

        return array;
    }

    /// <summary>Rebuilds a parameter from the JSON node written by <see cref="ToJson"/>.</summary>
    public static StepParameter FromJson(string name, JsonNode? node, ActionDefinition? owner,
        decimal jitter = 0m)
    {
        var definition = owner?.Parameters.FirstOrDefault(candidate => candidate.Name == name);
        return node switch
        {
            JsonArray rows when definition?.Kind is ActionParameterKind.Region
                or ActionParameterKind.KeySequence or ActionParameterKind.Images
                or ActionParameterKind.Returns or ActionParameterKind.Arguments
                or ActionParameterKind.Narrow or ActionParameterKind.ColourPoints => new StepParameter
                {
                    Name = name,
                    Kind = definition!.Kind,
                    Rows = [.. rows.OfType<JsonObject>().Select(Row)],
                },
            JsonArray array => new StepParameter
            {
                Name = name,
                Kind = ActionParameterKind.Steps,
                Steps = [.. array.OfType<JsonObject>().Select(MacroStep.FromJson)],
            },
            JsonObject record when definition?.Kind is ActionParameterKind.WindowRule => new StepParameter
            {
                Name = name,
                Kind = ActionParameterKind.WindowRule,
                Value = record.ToJsonString(),
            },
            _ => new StepParameter
            {
                Name = name,
                Kind = definition?.Kind ?? ActionParameterKind.Text,
                Value = ReadValue(node),
                Jitter = jitter,
            },
        };
    }

    /// <summary>One row of a list parameter, read back out of the object it was written as.</summary>
    private static StepParameterRow Row(JsonObject written)
    {
        var row = new StepParameterRow();
        foreach (var (column, value) in written)
        {
            if (value is not null)
            {
                row.Columns[column] = ReadValue(value);
            }
        }

        return row;
    }

    /// <summary>Reads a scalar node back as the text the editors work with.</summary>
    private static string ReadValue(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return string.Empty;
        }

        return value.TryGetValue<string>(out var text) ? text : value.ToJsonString();
    }

    /// <summary>Writes a nested step list as a JSON array.</summary>
    public static JsonArray BuildArray(IEnumerable<MacroStep> steps)
    {
        var array = new JsonArray();
        foreach (var step in steps)
        {
            array.Add(step.ToJson());
        }

        return array;
    }
}

/// <summary>
/// One row of a parameter that holds a list of rows, such as the regions a search looks in: what
/// each of the row's columns says, by name. Which columns there are and what they mean belongs to
/// the action, so a row is written down as it is rather than as a shape of its own — the same row
/// therefore serves a search region and, later, a chord of a key sequence.
/// </summary>
public class StepParameterRow
{
    /// <summary>What each column of this row says, by column name.</summary>
    public Dictionary<string, string> Columns { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>What a column says, or nothing when the row has no such column.</summary>
    public string Text(string column) => Columns.TryGetValue(column, out var value) ? value : string.Empty;

    /// <summary>A row that says one thing in one column, which is how a named region is written.</summary>
    public static StepParameterRow Of(string column, string value) => new()
    {
        Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [column] = value },
    };

    /// <summary>A copy, so a duplicated step does not share its rows with the step it came from.</summary>
    public StepParameterRow Copy() => new()
    {
        Columns = new Dictionary<string, string>(Columns, StringComparer.OrdinalIgnoreCase),
    };
}

/// <summary>A single step of a macro, saved as one node of the macro's JSON tree.</summary>
