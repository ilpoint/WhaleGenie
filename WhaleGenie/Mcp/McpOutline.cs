using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Models;

namespace WhaleGenie.Mcp;

/// <summary>
/// What a client is told about a macro without being handed the whole thing: one line per step, and
/// the arguments and results a call would have to know. Both the tools and the resources read it
/// from here, so an answer and a resource cannot drift apart.
/// </summary>
internal static class McpOutline
{
    /// <summary>
    /// Every step of a macro, in the order it runs, with the macro each one belongs to. Submacros
    /// are listed after the outer steps rather than inside them: they are separate documents, and a
    /// path is only meaningful inside the macro it was read from.
    /// </summary>
    /// <remarks>
    /// The path is what an insert or a move names as its parent, so the next step can be placed
    /// without reading the whole macro back.
    /// </remarks>
    public static IReadOnlyList<object> Of(MacroItem macro)
        =>
        [
            .. Walk(macro.Steps, string.Empty, "main"),
            .. macro.SubMacros.SelectMany(sub => Walk(sub.Steps, string.Empty, "sub:" + sub.Name)),
        ];

    /// <summary>
    /// One macro in a list: what it is called, how long it is, and what a call to it would have to
    /// know. The names are here rather than behind another call because picking which macro to call
    /// is exactly when a client needs them — opening every macro to find out would be one call each.
    /// </summary>
    public static object Summary(MacroItem macro)
        => new
        {
            name = macro.Name,
            steps = macro.Steps.Count,
            subMacros = macro.SubMacros.Count,
            arguments = Arguments(macro),
            results = Results(macro),
        };

    /// <summary>
    /// One macro as a row of the project resource: the same facts a summary carries, plus where to
    /// read it and how many triggers start it. A dictionary rather than an anonymous object because
    /// the resource adds fields the tool answer does not have, and a client reading the project page
    /// should not have to tell which of two shapes it is looking at.
    /// </summary>
    public static Dictionary<string, object?> Entry(MacroItem macro, string uri, int triggers)
        => new(StringComparer.Ordinal)
        {
            ["name"] = macro.Name,
            ["steps"] = macro.Steps.Count,
            ["subMacros"] = macro.SubMacros.Count,
            ["triggers"] = triggers,
            ["arguments"] = Arguments(macro),
            ["results"] = Results(macro),
            ["uri"] = uri,
        };

    /// <summary>The argument rows a call has to fill in, named the way a call step names them.</summary>
    public static List<object> Arguments(MacroItem macro)
        => [.. CallableSignature.Arguments(macro.Callable).Select(argument => (object)new
        {
            name = argument.Name,
            type = argument.Type.ToString(),
            required = argument.Required,
        })];

    /// <summary>The result rows a call may ask for, named the way the called macro declared them.</summary>
    public static List<object> Results(MacroItem macro)
        => [.. CallableSignature.Results(macro.Callable)
            .Select(result => (object)new { name = result.Name, type = result.Type.ToString() })];

    private static IEnumerable<object> Walk(IReadOnlyList<MacroStep> steps, string prefix, string scope)
    {
        for (var at = 0; at < steps.Count; at++)
        {
            var step = steps[at];
            var path = prefix.Length == 0
                ? at.ToString(CultureInfo.InvariantCulture)
                : $"{prefix}.{at.ToString(CultureInfo.InvariantCulture)}";
            yield return new { id = step.Id, type = step.Type, path, macro = scope };

            foreach (var parameter in step.StepLists)
            {
                foreach (var child in Walk(parameter.Steps, $"{path}.{parameter.Name}", scope))
                {
                    yield return child;
                }
            }
        }
    }
}
