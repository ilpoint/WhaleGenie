using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;


namespace WhaleGenie.Mcp;

// Finding the step an operation names, and the refusals that say why it was not found.
internal static partial class McpPatches
{
    private static MacroStep? Located(MacroItem macro, JsonObject operation)
    {
        var id = Text(operation["stepId"]).Trim();
        return Locate(Scope(macro, operation).Steps, id, out _, out _, out var step) ? step : null;
    }

    /// <summary>
    /// Which step tree an operation works on: the macro's own, or one of the submacros it carries.
    /// A submacro is written <c>sub:名字</c>; nothing at all means the macro itself.
    /// </summary>
    /// <remarks>
    /// The submacros are separate documents inside one package, and until now a patch could only
    /// reach the outer one: a project that used a submacro could only be changed by sending the
    /// whole macro again, which is exactly what a patch exists to avoid.
    /// </remarks>
    private static (List<MacroStep> Steps, string Label, Refusal? Error) Scope(
        MacroItem macro, JsonObject operation)
    {
        var written = Text(operation["macro"]).Trim();
        if (written.Length == 0 || string.Equals(written, TopLevel, StringComparison.OrdinalIgnoreCase))
        {
            return (macro.Steps, "main", null);
        }

        // A document is named by the path that reaches it: sub:a, or sub:a/sub:b for one written
        // inside a. Each segment is a name, so the walk goes one level at a time.
        var here = macro;
        var label = "main";
        foreach (var segment in written.Split('/'))
        {
            var asked = segment.Trim();
            var name = asked.StartsWith("sub:", StringComparison.OrdinalIgnoreCase)
                ? asked[4..].Trim()
                : asked;

            var sub = here.SubMacros.FirstOrDefault(candidate =>
                string.Equals(candidate.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
            if (sub is null)
            {
                return ([], written, Refused(McpErrors.BadArgument,
                    $"There is no submacro named \"{name}\" in \"{here.Name}\".",
                    "Call whalegenie_get_macro: every row of its outline says which macro it belongs to, and the label is the path to send back.",
                    true, new { macro = name, submacros = here.SubMacros.Select(child => child.Name).ToList() }));
            }

            label = label == "main" ? "sub:" + sub.Name.Trim() : label + "/sub:" + sub.Name.Trim();
            here = sub;
        }

        return (here.Steps, label, null);
    }

    /// <summary>Where one step sits: the list holding it and its place in that list.</summary>
    private static bool Locate(List<MacroStep> steps, string id, out List<MacroStep> list,
        out int index, out MacroStep step)
    {
        for (var at = 0; at < steps.Count; at++)
        {
            if (id.Length > 0 && string.Equals(steps[at].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                list = steps;
                index = at;
                step = steps[at];
                return true;
            }

            foreach (var parameter in steps[at].StepLists)
            {
                if (Locate(parameter.Steps, id, out list, out index, out step))
                {
                    return true;
                }
            }
        }

        list = steps;
        index = -1;
        step = null!;
        return false;
    }

    /// <summary>Every step list in the macro and in the submacros it carries, for one set of ids.</summary>
    private static IEnumerable<IEnumerable<MacroStep>> Documents(MacroItem macro)
    {
        yield return macro.Steps;
        foreach (var sub in macro.SubMacros)
        {
            foreach (var document in Documents(sub))
            {
                yield return document;
            }
        }
    }

    private static int Position(JsonNode? node, int count)
        => node is JsonValue value && value.TryGetValue<int>(out var at)
            ? Math.Clamp(at, 0, count)
            : count;

    /// <summary>The value as the field holds it: text, or the number the text says.</summary>
    private static string Written(JsonNode? value) => value switch
    {
        null => string.Empty,
        JsonValue number when number.TryGetValue<decimal>(out var written)
            => written.ToString(CultureInfo.InvariantCulture),
        JsonValue flag when flag.TryGetValue<bool>(out var answer) => answer ? "true" : "false",
        JsonValue text when text.TryGetValue<string>(out var written) => written,
        _ => value.ToJsonString(),
    };

    private static Refusal NoStep(JsonObject operation)
    {
        var id = Text(operation["stepId"]).Trim();
        return Refused(McpErrors.BadArgument, $"There is no step with the id \"{id}\".",
            "Step ids come from whalegenie_get_macro; a step written without one gets a new one on the next write.",
            true, new { stepId = id });
    }

    private static Refusal Refused(string code, string message, string hint, bool retryable,
        object? details) => new(code, message, hint, retryable, details);

    private static string Text(JsonNode? node)
        => node switch
        {
            null => string.Empty,
            JsonValue value when value.TryGetValue<string>(out var text) => text,

            // A number or a truth value written as JSON is still what the caller meant: refusing it
            // because it is not a string would make a client quote every count.
            JsonValue value => value.ToJsonString(),
            _ => node.ToJsonString(),
        };

    /// <summary>
    /// One cell of a row, as the file writes it. A number and a truth value are written the way the
    /// editor writes them rather than refused, because a client sending <c>3</c> for a count means
    /// the same thing as one sending <c>"3"</c>.
    /// </summary>
    private static string Cell(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return string.Empty;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (value.TryGetValue<decimal>(out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return value.TryGetValue<bool>(out var flag) && flag ? "true" : string.Empty;
    }
}
