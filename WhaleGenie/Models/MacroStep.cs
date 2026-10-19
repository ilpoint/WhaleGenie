using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Media;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// One parameter value captured for a step. The value stays as text while the
/// editor is open and is converted to its JSON type only when the tree is written.
/// </summary>
public class StepParameter
{
    public required string Name { get; init; }

    public required ActionParameterKind Kind { get; init; }

    public string Value { get; init; } = string.Empty;

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
    /// The rows of a parameter that holds a list of them, filled in when <see cref="Kind"/> is
    /// <see cref="ActionParameterKind.Region"/>, <see cref="ActionParameterKind.KeySequence"/> or
    /// <see cref="ActionParameterKind.Images"/>; a <see cref="ActionParameterKind.Returns"/> holds
    /// one row per result the step hands over, and a <see cref="ActionParameterKind.Narrow"/> one
    /// row per thing the element has to be like. What the columns of a row mean belongs to the
    /// action, so this holds the names and what they say rather than a shape of its own.
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
        or ActionParameterKind.Narrow;

    /// <summary>Everything written in the rows of a list parameter, by row.</summary>
    public IEnumerable<string> RowValues => Rows.SelectMany(row => row.Columns.Values);

    /// <summary>Chosen condition, filled in when <see cref="Kind"/> is <see cref="ActionParameterKind.Condition"/>.</summary>
    public MacroStep? Condition { get; init; }

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
            or ActionParameterKind.Returns or ActionParameterKind.Narrow => RowsJson(),
        ActionParameterKind.Condition => Condition?.ToJson(),
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
        var expectsCondition = definition?.Kind is ActionParameterKind.Condition;

        return node switch
        {
            JsonArray rows when definition?.Kind is ActionParameterKind.Region
                or ActionParameterKind.KeySequence or ActionParameterKind.Images
                or ActionParameterKind.Returns or ActionParameterKind.Narrow => new StepParameter
                {
                    Name = name,
                    Kind = definition!.Kind,
                    Rows = [.. rows.OfType<JsonObject>().Select(Row)],
                },
            JsonArray array => new StepParameter
            {
                Name = name,
                Kind = expectsCondition ? ActionParameterKind.Condition : ActionParameterKind.Steps,
                Steps = [.. array.OfType<JsonObject>().Select(MacroStep.FromJson)],
            },
            JsonObject child when child.ContainsKey("type") => new StepParameter
            {
                Name = name,
                Kind = ActionParameterKind.Condition,
                Condition = MacroStep.FromJson(child),
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
public class MacroStep : INotifyPropertyChanged
{
    /// <summary>Fully qualified action key, for example <c>input.keyPress</c>.</summary>
    public required string Type { get; init; }

    /// <summary>
    /// The short name this step is known by, written into the macro file so that it survives
    /// saving: a result variable carries it, a condition points at it, and a run reports the step
    /// under it. Empty on steps that have never been saved since ids were introduced, which is
    /// what <see cref="StepIds.Settle"/> fills in when the macro is opened.
    /// </summary>
    public string Id { get; internal set; } = string.Empty;

    public List<StepParameter> Parameters { get; init; } = [];

    /// <summary>
    /// True while the editor shows the steps written inside this one. It is editor state rather
    /// than part of the macro, so it is never written to the file: a macro saved with a block
    /// folded away is the same macro as one saved with it open.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>The parameters of this step that hold steps, in the order they were written.</summary>
    public IEnumerable<StepParameter> StepLists
        => Parameters.Where(parameter => parameter.Kind == ActionParameterKind.Steps);

    /// <summary>How many steps are written inside this one, all of its lists together.</summary>
    public int ChildCount => StepLists.Sum(list => list.Steps.Count);

    /// <summary>True when there are steps inside this one, so it can be folded open and shut.</summary>
    public bool HasChildren => ChildCount > 0;

    private StepMeta _meta = StepMeta.Empty;

    /// <summary>
    /// Settings that apply to the step itself whatever its action is: a note, whether it
    /// is skipped, its timeout, its retries and the pauses around it.
    /// </summary>
    public StepMeta Meta
    {
        get => _meta;
        set
        {
            _meta = value;
            Raise(nameof(Meta));
            Raise(nameof(Comment));
            Raise(nameof(HasComment));
            Raise(nameof(IsSkipped));
            Raise(nameof(SkipLabel));
            Raise(nameof(RowOpacity));
            Raise(nameof(MetaSummary));
            Raise(nameof(HasMetaSummary));
        }
    }

    /// <summary>
    /// The step settings worth seeing without opening the dialog: the retries it gets, the timeout
    /// it is held to, the pauses around it, and what a failure does instead of stopping the macro.
    /// Empty for an ordinary step, so the list stays quiet.
    /// </summary>
    public string MetaSummary
    {
        get
        {
            var meta = Meta;
            var parts = new List<string>();

            if (meta.RetryCount > 0)
            {
                parts.Add(Strings.Format(meta.RetryBackoff switch
                {
                    RetryBackoff.Doubling => "Editor.Meta.RetryDoubling",
                    RetryBackoff.Jitter => "Editor.Meta.RetryJitter",
                    _ => "Editor.Meta.Retry",
                }, meta.RetryCount));
            }

            if (meta.TimeoutMs > 0)
            {
                parts.Add(Strings.Format("Editor.Meta.Timeout", DurationUnit.Written(meta.TimeoutMs)));
            }

            if (meta.DelayBeforeMs > 0 || meta.DelayAfterMs > 0)
            {
                parts.Add(Strings.Format("Editor.Meta.Pauses",
                    DurationUnit.Written(meta.DelayBeforeMs),
                    DurationUnit.Written(meta.DelayAfterMs)));
            }

            switch (meta.OnError)
            {
                case StepErrorAction.Continue:
                    parts.Add(Strings.Get("Editor.Meta.OnErrorContinue"));
                    break;
                case StepErrorAction.NextIteration:
                    parts.Add(Strings.Get("Editor.Meta.OnErrorNextIteration"));
                    break;
                case StepErrorAction.AskUser:
                    parts.Add(Strings.Get("Editor.Meta.OnErrorAsk"));
                    break;
            }

            return string.Join(" · ", parts);
        }
    }

    /// <summary>True when there is something about this step worth showing.</summary>
    public bool HasMetaSummary => MetaSummary.Length > 0;

    /// <summary>Note the user wrote about this step, shown under its name.</summary>
    public string Comment => Meta.Comment;

    public bool HasComment => Meta.Comment.Length > 0;

    /// <summary>True when the macro skips this step.</summary>
    public bool IsSkipped => !Meta.IsEnabled;

    /// <summary>
    /// True when this is a condition rather than a step of the macro. A condition says what has to
    /// be true and is asked by the step it belongs to, so it never runs on its own and has no
    /// ending of its own for anything to ask about.
    /// </summary>
    public bool IsCondition => Definition?.IsCondition ?? false;

    /// <summary>Marker drawn on a step the macro will skip.</summary>
    public string SkipLabel => Strings.Get("Editor.StepDisabled");

    /// <summary>Name of the field that says where a step's input is sent.</summary>
    private const string InputModeParameter = "inputMode";

    /// <summary>
    /// 这一步的输入往哪儿送：前台 / 后台消息 / 驱动级。Empty for a step that sends no input at
    /// all, and for an action that has no such field.
    /// </summary>
    /// <remarks>
    /// Whether the input goes in front, is posted at a window, or goes through the virtual device is
    /// what the user pays for — a window that has to be in front, a window that has to be named
    /// rightly, or a driver installed and a server running — and it is the first thing to check when
    /// a step that works by hand does nothing in a macro. Reading it off the row beats opening the
    /// step. A step that never wrote the field is in front, because that is what the catalogue
    /// starts the field at.
    /// </remarks>
    public string InputMark
    {
        get
        {
            var parameter = Parameters.FirstOrDefault(item => item.Name == InputModeParameter);
            if (parameter is null)
            {
                var declared = Definition?.Parameters
                    .Any(item => item.Name == InputModeParameter) ?? false;
                return declared ? Strings.Get("Editor.InputForeground") : string.Empty;
            }

            return parameter.Value switch
            {
                "foreground" => Strings.Get("Editor.InputForeground"),
                "background" => Strings.Get("Editor.InputBackground"),
                "driver" => Strings.Get("Editor.InputDriver"),
                _ => string.Empty,
            };
        }
    }

    /// <summary>A skipped step fades so it is easy to tell apart while reading the list.</summary>
    public double RowOpacity => Meta.IsEnabled ? 1 : 0.45;

    public ActionDefinition? Definition => ActionCatalog.Find(Type);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Human readable name, falling back to the raw key when it is unknown.</summary>
    public string DisplayName => Definition?.LocalName ?? Type;

    /// <summary>
    /// This step named the way anything that has to point at one names it: the note the user wrote,
    /// the action it is, and the short name it goes by. The note comes first because that is the
    /// one part that says what the step is for; the name is last because that is what a condition
    /// stores. Whichever parts are empty are left out rather than leaving a gap.
    /// </summary>
    public string PickerLabel => string.Join(" · ",
        new[] { Comment, DisplayName, Id }.Where(part => part.Length > 0));

    /// <summary>Action key plus the parameters that have a value, shown under the name.</summary>
    public string Detail
    {
        get
        {
            var parts = new List<string>();
            foreach (var parameter in Parameters)
            {
                var label = ParameterLabel(parameter);
                switch (parameter.Kind)
                {
                    case ActionParameterKind.Steps when parameter.Steps.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Common.StepCount", parameter.Steps.Count)}");
                        break;
                    case ActionParameterKind.Condition when parameter.Condition is not null:
                        parts.Add($"{label} = {parameter.Condition.DisplayName}");
                        break;
                    case ActionParameterKind.Region when parameter.Rows.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Editor.RegionCount", parameter.Rows.Count)}");
                        break;
                    case ActionParameterKind.KeySequence when parameter.Rows.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Editor.KeyCount", parameter.Rows.Count)}");
                        break;
                    case ActionParameterKind.Images when parameter.Rows.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Editor.PictureCount", parameter.Rows.Count)}");
                        break;
                    case ActionParameterKind.Returns when parameter.Rows.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Editor.ReturnCount", parameter.Rows.Count)}");
                        break;
                    case ActionParameterKind.Narrow when parameter.Rows.Count > 0:
                        parts.Add($"{label} = {Strings.Format("Editor.NarrowCount", parameter.Rows.Count)}");
                        break;
                    case ActionParameterKind.Steps or ActionParameterKind.Condition
                        or ActionParameterKind.Region or ActionParameterKind.KeySequence
                        or ActionParameterKind.Images or ActionParameterKind.Returns
                        or ActionParameterKind.Narrow:
                        break;
                    default:
                        if (!string.IsNullOrWhiteSpace(parameter.Value))
                        {
                            parts.Add($"{label} = {DescribeValue(parameter)}{JitterNote(parameter)}");
                        }
                        break;
                }
            }

            var values = string.Join(", ", parts);
            return values.Length == 0 ? Type : $"{Type} · {values}";
        }
    }

    /// <summary>How much a value is allowed to move, shown after it, or nothing when it is fixed.</summary>
    private static string JitterNote(StepParameter parameter)
        => parameter.HasJitter
            ? " " + Strings.Format("Editor.JitterDetail",
                (parameter.Jitter * 100m).ToString("0.##", CultureInfo.InvariantCulture))
            : string.Empty;

    public Geometry? Icon => Definition?.Icon;

    /// <summary>Localized label of a parameter, falling back to its raw name.</summary>
    public string ParameterLabel(StepParameter parameter)
        => ParameterDefinition(parameter)?.LocalLabel ?? parameter.Name;

    /// <summary>Choice values are shown with their display text, everything else as typed.</summary>
    private string DescribeValue(StepParameter parameter)
    {
        if (parameter.Kind is ActionParameterKind.Choice
            && ParameterDefinition(parameter) is { } definition)
        {
            return Strings.Get($"{definition.OwnerKey}.{definition.Name}.option.{parameter.Value}",
                parameter.Value);
        }

        return parameter.Value;
    }

    /// <summary>The catalogue entry for a parameter of this step, or null when it is unknown.</summary>
    public ActionParameter? ParameterDefinition(StepParameter parameter)
        => Definition?.Parameters.FirstOrDefault(parameterDefinition =>
            parameterDefinition.Name == parameter.Name);

    /// <summary>The text of a named parameter, or nothing when the step has no such parameter.</summary>
    /// <remarks>
    /// Read by the things that look at a step without running it — the design-time check and the
    /// dialog that fills one in — which is the same question the engine asks of the step it runs.
    /// </remarks>
    public string Text(string name)
        => Parameters.FirstOrDefault(parameter => parameter.Name == name)?.Value ?? string.Empty;

    /// <summary>
    /// Collects the variables this step creates, including the ones nested inside it,
    /// so the variable pickers can offer them.
    /// </summary>
    public void CollectVariables(ICollection<string> names)
    {
        foreach (var parameter in Parameters)
        {
            switch (parameter.Kind)
            {
                case ActionParameterKind.Steps:
                    foreach (var child in parameter.Steps)
                    {
                        child.CollectVariables(names);
                    }
                    break;
                case ActionParameterKind.Condition:
                    parameter.Condition?.CollectVariables(names);
                    break;
                // The results a step hands over are named one by one on the returns page, so what
                // this step creates is read from those rows rather than from a single value.
                case ActionParameterKind.Returns:
                    foreach (var (_, name) in ReturnedResults(parameter))
                    {
                        names.Add(name);
                    }
                    break;
                default:
                    if (DefinesVariable(parameter) && !string.IsNullOrWhiteSpace(parameter.Value))
                    {
                        names.Add(parameter.Value.Trim());
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// The results this step was told to hand over, in the order they are written: one per ticked
    /// result, each row of the <c>returns</c> parameter carrying the key of the result and the name
    /// it goes by.
    /// </summary>
    public IEnumerable<(string Key, string Name)> ReturnedResults(StepParameter parameter)
    {
        foreach (var row in parameter.Rows)
        {
            var name = row.Text(FieldNames.Name).Trim();
            if (name.Length > 0)
            {
                yield return (row.Text(FieldNames.Key).Trim(), name);
            }
        }
    }

    /// <summary>
    /// True when the parameter creates a variable rather than referencing one. What a step leaves
    /// behind is now named one result at a time on the returns page, so this is left with the names
    /// that are part of writing the step itself: the loop's own variables, and the name a "set
    /// variable", "calculate" or "create list" step writes into.
    /// </summary>
    public bool DefinesVariable(StepParameter parameter) => DefinesVariable(Type, parameter.Name);

    /// <summary>
    /// The same question asked of a step that is still being written, which is a type and a field
    /// name rather than a step: the dialog has both while the step is on the screen, and asking it
    /// this way keeps one answer to "which fields have a macro make a variable" rather than two.
    /// </summary>
    public static bool DefinesVariable(string? type, string parameterName) => parameterName switch
    {
        "itemVariable" or "indexVariable" => true,
        "name" => type is "control.setVariable" or "control.calculate" or "control.listCreate"
            or "control.for",
        "runHandleName" => type is "control.startMacro",
        _ => false,
    };

    /// <summary>Builds the <c>{ "type": …, "params": { … } }</c> node for this step.</summary>
    public JsonObject ToJson()
    {
        var parameters = new JsonObject();
        foreach (var parameter in Parameters)
        {
            parameters[parameter.Name] = parameter.ToJson();
        }

        var node = new JsonObject
        {
            ["type"] = Type,
            ["params"] = parameters,
        };

        // Written only when there is one, so a step edited by hand or saved by an older build
        // still round-trips exactly as it was.
        if (Id.Length > 0)
        {
            node["id"] = Id;
        }

        // How much a length of time may move is written beside the parameters rather than inside
        // them, so a step saved before this existed still reads back exactly as it was.
        var jitter = new JsonObject();
        foreach (var parameter in Parameters.Where(parameter => parameter.HasJitter))
        {
            jitter[parameter.Name] = JsonValue.Create(parameter.Jitter);
        }

        if (jitter.Count > 0)
        {
            node["jitter"] = jitter;
        }

        if (!Meta.IsEmpty)
        {
            node["meta"] = Meta.ToJson();
        }

        return node;
    }

    /// <summary>Rebuilds a step from the JSON node written by <see cref="ToJson"/>.</summary>
    public static MacroStep FromJson(JsonObject node)
    {
        var step = new MacroStep
        {
            Type = node["type"]?.GetValue<string>() ?? string.Empty,
            Id = node["id"]?.GetValue<string>()?.Trim() ?? string.Empty,
            Meta = StepMeta.FromJson(node["meta"] as JsonObject),
        };

        if (node["params"] is JsonObject parameters)
        {
            var definition = ActionCatalog.Find(step.Type);
            var jitters = node["jitter"] as JsonObject;
            foreach (var (name, value) in parameters)
            {
                step.Parameters.Add(
                    StepParameter.FromJson(name, value, definition, JitterOf(jitters, name)));
            }
        }

        return step;
    }

    /// <summary>Reads the give a parameter was written with, or zero when it has none.</summary>
    private static decimal JitterOf(JsonObject? jitters, string name)
        => jitters?[name] is JsonValue value && value.TryGetValue<decimal>(out var fraction)
            ? Math.Max(0m, fraction)
            : 0m;
}
