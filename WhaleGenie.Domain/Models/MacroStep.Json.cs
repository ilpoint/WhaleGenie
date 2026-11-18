using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// Writing a step to JSON and reading one back.
public partial class MacroStep : INotifyPropertyChanged
{
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

        step.EnsureDeclaredLists();

        return step;
    }

    /// <summary>Reads the give a parameter was written with, or zero when it has none.</summary>
    private static decimal JitterOf(JsonObject? jitters, string name)
        => jitters?[name] is JsonValue value && value.TryGetValue<decimal>(out var fraction)
            ? Math.Max(0m, fraction)
            : 0m;
}
