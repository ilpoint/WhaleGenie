using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Media;
using Viktor.Core.Execution;
using Viktor.Localization;

namespace Viktor.Models;

/// <summary>
/// One parameter value captured for a step. The value stays as text while the
/// editor is open and is converted to its JSON type only when the tree is written.
/// </summary>
public class StepParameter
{
    public required string Name { get; init; }

    public required ActionParameterKind Kind { get; init; }

    public string Value { get; init; } = string.Empty;

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
    public static StepParameter FromJson(string name, JsonNode? node, ActionDefinition? owner)
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

    public List<StepParameter> Parameters { get; init; } = [];

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
        }
    }

    /// <summary>Note the user wrote about this step, shown under its name.</summary>
    public string Comment => Meta.Comment;

    public bool HasComment => Meta.Comment.Length > 0;

    /// <summary>True when the macro skips this step.</summary>
    public bool IsSkipped => !Meta.IsEnabled;

    /// <summary>Marker drawn on a step the macro will skip.</summary>
    public string SkipLabel => Strings.Get("Editor.StepDisabled");

    /// <summary>A skipped step fades so it is easy to tell apart while reading the list.</summary>
    public double RowOpacity => Meta.IsEnabled ? 1 : 0.45;

    public ActionDefinition? Definition => ActionCatalog.Find(Type);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Human readable name, falling back to the raw key when it is unknown.</summary>
    public string DisplayName => Definition?.LocalName ?? Type;

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
                            parts.Add($"{label} = {DescribeValue(parameter)}");
                        }
                        break;
                }
            }

            var values = string.Join(", ", parts);
            return values.Length == 0 ? Type : $"{Type} · {values}";
        }
    }

    public Geometry? Icon => Definition?.Icon;

    /// <summary>Localized label of a parameter, falling back to its raw name.</summary>
    private string ParameterLabel(StepParameter parameter)
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

    private ActionParameter? ParameterDefinition(StepParameter parameter)
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

    /// <summary>True when the parameter creates a variable rather than referencing one.</summary>
    private bool DefinesVariable(StepParameter parameter) => parameter.Name switch
    {
        "itemVariable" or "resultVariable" or "errorVariable" or "saveTo" => true,
        "name" => Type is "control.setVariable" or "control.listCreate",
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
            Meta = StepMeta.FromJson(node["meta"] as JsonObject),
        };

        if (node["params"] is JsonObject parameters)
        {
            var definition = ActionCatalog.Find(step.Type);
            foreach (var (name, value) in parameters)
            {
                step.Parameters.Add(StepParameter.FromJson(name, value, definition));
            }
        }

        return step;
    }
}
