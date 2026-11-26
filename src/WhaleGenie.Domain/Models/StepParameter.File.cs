using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Models;

// How a parameter and its rows come to be written in a file, and how they are read back.
/// <summary>
/// The file half of a parameter: what a value looks like once it is saved, and how a saved one is
/// read again. It is kept apart from what a value <em>is</em> because the two questions change for
/// different reasons — one follows the shapes a field can hold, the other follows the file.
/// </summary>
public partial class StepParameter
{
    /// <summary>
    /// The rule text kept in a step is read by a person as well as by the parser, so a window
    /// title stays the writing it is instead of \uXXXX escapes.
    /// </summary>
    private static readonly JsonSerializerOptions Readable = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Writes the value as the JSON type the runtime expects.</summary>
    /// <remarks>
    /// A number is written as the number it is, and anything else is written as what it says. A
    /// number field may hold an expression — <c>sys.mouseX</c>, the place the pointer is at when
    /// the step runs — which the engine reads and works out; writing that as a zero because it is
    /// not digits would save a step that clicks the corner of the screen instead of the mouse, and
    /// the loss would be silent, because the file it is read back from says a number.
    /// </remarks>
    public JsonNode? ToJson() => Kind switch
    {
        ActionParameterKind.Number => NumberJson(),
        ActionParameterKind.Bool => JsonValue.Create(
            string.Equals(Formula.Plain(Written), "true", StringComparison.OrdinalIgnoreCase)),
        ActionParameterKind.Steps => BuildArray(Steps),
        ActionParameterKind.Region or ActionParameterKind.KeySequence or ActionParameterKind.Images
            or ActionParameterKind.Returns or ActionParameterKind.Arguments
            or ActionParameterKind.Narrow or ActionParameterKind.ColourPoints => RowsJson(),
        ActionParameterKind.WindowRule when Formula.Plain(Written).TrimStart().StartsWith('{')
            => JsonNode.Parse(Formula.Plain(Written)),
        _ => ScalarJson(),
    };

    /// <summary>
    /// One value as the file keeps it: the text it is where nothing is named, and the tagged shape
    /// it is where the field names anything. Text stays text so a hand-edited macro still reads the
    /// way it was written; anything with a name or a formula in it is kept as a shape, because a
    /// line cannot say which piece is a name and which is text.
    /// </summary>
    private JsonNode? ScalarJson()
    {
        if (Formula.IsNothing(Written))
        {
            return JsonValue.Create(string.Empty);
        }

        return Formula.IsPlain(Written)
            ? JsonValue.Create(Formula.Plain(Written))
            : FormulaJson.Write(Written);
    }

    /// <summary>
    /// A number field as the file holds it: the number it says, or the text of the expression it
    /// says instead. A field nobody answered — only ever written by hand, because the editor leaves
    /// an unanswered number out of the step — stays the zero it has always been read as.
    /// </summary>
    private JsonNode? NumberJson()
    {
        if (Formula.IsNothing(Written))
        {
            return JsonValue.Create(0m);
        }

        var written = Formula.Plain(Written);
        return decimal.TryParse(written, NumberStyles.Number, CultureInfo.InvariantCulture,
            out var number)
            ? JsonValue.Create(number)
            : ScalarJson();
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
            foreach (var (column, value) in row.Cells)
            {
                if (Formula.IsNothing(value))
                {
                    continue;
                }

                var said = Formula.Plain(value);
                written[column] = Formula.IsPlain(value)
                    && decimal.TryParse(said, NumberStyles.Number, CultureInfo.InvariantCulture,
                        out var number)
                        ? JsonValue.Create(number)
                        : FormulaJson.Write(value);
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
            JsonObject record when definition?.Kind is ActionParameterKind.WindowRule =>
                new StepParameter
                {
                    Name = name,
                    Kind = ActionParameterKind.WindowRule,
                    Written = IsShape(record)
                        ? FormulaJson.Read(record)
                        : Formula.Text(record.ToJsonString(Readable)),
                },
            _ => new StepParameter
            {
                Name = name,
                Kind = definition?.Kind ?? ActionParameterKind.Text,
                Written = ValueOf(node),
                Jitter = jitter,
            },
        };
    }

    /// <summary>
    /// True when the object is one of the tagged shapes a value is written as rather than a window
    /// rule written out. A rule is title/process/class/handle/how and the switches beside them;
    /// none of those keys is a shape tag, so a variable that holds a rule is not read as a literal
    /// rule with every field empty.
    /// </summary>
    private static bool IsShape(JsonObject written)
        => written.ContainsKey("name") || written.ContainsKey("run") || written.ContainsKey("call")
           || written.ContainsKey("op") || written.ContainsKey("list")
           || written.ContainsKey("group") || written.ContainsKey("date");

    /// <summary>One row of a list parameter, read back out of the object it was written as.</summary>
    private static StepParameterRow Row(JsonObject written)
    {
        var row = new StepParameterRow();
        foreach (var (column, value) in written)
        {
            if (value is not null)
            {
                row.Cells[column] = ValueOf(value);
            }
        }

        return row;
    }

    /// <summary>
    /// One value read back out of the file, in the one reader the shape has. The codec already
    /// tells a string from a shape, so this must not ask a second question first: a number whose
    /// CLR type the reader did not expect is exactly how a value became text.
    /// </summary>
    private static ExpressionNode ValueOf(JsonNode? node) => FormulaJson.Read(node);

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
