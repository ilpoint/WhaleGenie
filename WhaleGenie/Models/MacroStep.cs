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

    /// <summary>Chosen condition, filled in when <see cref="Kind"/> is <see cref="ActionParameterKind.Condition"/>.</summary>
    public MacroStep? Condition { get; init; }

    /// <summary>Writes the value as the JSON type the runtime expects.</summary>
    public JsonNode? ToJson() => Kind switch
    {
        ActionParameterKind.Number => JsonValue.Create(
            decimal.TryParse(Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
                ? number
                : 0m),
        ActionParameterKind.Bool => JsonValue.Create(
            string.Equals(Value, "true", StringComparison.OrdinalIgnoreCase)),
        ActionParameterKind.Steps => BuildArray(Steps),
        ActionParameterKind.Condition => Condition?.ToJson(),
        _ => JsonValue.Create(Value),
    };

    /// <summary>Rebuilds a parameter from the JSON node written by <see cref="ToJson"/>.</summary>
    public static StepParameter FromJson(string name, JsonNode? node, ActionDefinition? owner,
        decimal jitter = 0m)
    {
        var definition = owner?.Parameters.FirstOrDefault(candidate => candidate.Name == name);
        var expectsCondition = definition?.Kind is ActionParameterKind.Condition;

        return node switch
        {
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
            _ => new StepParameter
            {
                Name = name,
                Kind = definition?.Kind ?? ActionParameterKind.Text,
                Value = ReadValue(node),
                Jitter = jitter,
            },
        };
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
    public bool IsCondition => Type.StartsWith("condition.", StringComparison.Ordinal);

    /// <summary>Marker drawn on a step the macro will skip.</summary>
    public string SkipLabel => Strings.Get("Editor.StepDisabled");

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
                    case ActionParameterKind.Steps or ActionParameterKind.Condition:
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
    /// True when the parameter creates a variable rather than referencing one. The names are the
    /// ones the engine writes those variables under, so this is the one list of them: a picker that
    /// offered anything else would offer a name no run ever makes.
    /// </summary>
    public bool DefinesVariable(StepParameter parameter) => parameter.Name switch
    {
        "itemVariable" or "indexVariable" or "resultVariable" or "errorVariable"
            or "exitCodeVariable" or "saveTo" => true,
        "name" => Type is "control.setVariable" or "control.calculate" or "control.listCreate",
        "variable" => Type is "control.for",
        "elapsedVariable" => Type is "control.waitUntil",
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
