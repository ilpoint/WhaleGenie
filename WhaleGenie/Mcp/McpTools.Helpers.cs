using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.Mcp;

// Answers built from the project: a validation, a signature, a step found by id, and the problems.
public sealed partial class McpTools
{
    /// <summary>The validation answer as a client reads it: what blocks, what is only worth knowing.</summary>
    private static object Shape(McpDraftValidation validation) => new
    {
        ok = validation.Ok,
        counts = new { blocking = validation.Blocking.Count, hints = validation.Hints.Count },
        blocking = validation.Blocking,
        hints = validation.Hints,
    };

    /// <summary>
    /// What a macro asks a call to fill in and what it may hand back, with a skeleton a client can
    /// copy into a call step. Both are rows: an argument row names the argument and gives its
    /// value, a result row names the result and says where it lands — facts that live in the called
    /// macro, which is why no action entry can state them.
    /// </summary>
    private static object Signature(MacroItem macro)
    {
        var call = macro.Callable;
        var arguments = CallableSignature.Arguments(call)
            .Select(argument => new
            {
                name = argument.Name,
                type = argument.Type.ToString(),
                required = argument.Required,
                @default = ExpressionPhrase.Display(argument.Default),
                defaultShape = FormulaJson.Write(argument.Default),
                description = argument.Description,
                fields = argument.Fields
                    .Select(field => new { name = field.Name, type = field.Type.ToString() })
                    .ToList(),
            })
            .ToList();

        var results = CallableSignature.Results(call)
            .Select(result => new
            {
                name = result.Name,
                type = result.Type.ToString(),
                description = result.Description,
                fields = result.Fields
                    .Select(field => new { name = field.Name, type = field.Type.ToString() })
                    .ToList(),
            })
            .ToList();

        // Only the arguments a call has to fill in are in the skeleton: leaving an optional one out
        // is how its declared default applies, and writing it down empty would say something else.
        var template = new JsonObject
        {
            [FieldNames.Macro] = macro.Name.Trim(),
            [FieldNames.Arguments] = new JsonArray(
                [.. arguments
                    .Where(argument => argument.required)
                    .Select(argument => new JsonObject
                    {
                        [FieldNames.Name] = argument.name,
                        [FieldNames.Value] = string.Empty,
                    })]),
            ["returns"] = new JsonArray([.. results.Select(result => new JsonObject
            {
                [FieldNames.Key] = result.name,
                [FieldNames.Name] = result.name,
            })]),
        };

        return new
        {
            name = macro.Name.Trim(),
            arguments,
            results,
            argumentColumns = new[] { FieldNames.Name, FieldNames.Value, FieldNames.Given },
            callTemplate = template,
            notes = new[]
            {
                "A call step names the macro in params.macro and passes its arguments in params.arguments, one row per argument: name is the name the called macro declared, value is what goes into it, and given says the empty value is meant rather than left out.",
                "An argument with no row takes its declared default; a required one with no row fails the call with Run.MissingArgument before the called macro runs.",
                "To pass an empty value instead of the default, write the row with an empty value and given true. An empty value without given means the argument was left out.",
                "Results are asked for in params.returns, one row per value: key is the name the called macro declared, name is the local variable it lands in.",
                "control.runMacro waits for the called macro, and its results exist when it returns.",
            },
        };
    }

    /// <summary>Where a step sits: the step itself and the path an insert or a move would name.</summary>
    private static (MacroStep Step, string Path)? Find(IReadOnlyList<MacroStep> steps, string id, string prefix)
    {
        for (var at = 0; at < steps.Count; at++)
        {
            var step = steps[at];
            var path = prefix.Length == 0
                ? at.ToString(CultureInfo.InvariantCulture)
                : $"{prefix}.{at.ToString(CultureInfo.InvariantCulture)}";
            if (string.Equals(step.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return (step, path);
            }

            foreach (var parameter in step.StepLists)
            {
                if (Find(parameter.Steps, id, $"{path}.{parameter.Name}") is { } child)
                {
                    return child;
                }
            }
        }

        return null;
    }

    /// <summary>What validation says about one step, both what blocks and what is only worth reading.</summary>
    private static IReadOnlyList<object> Problems(McpDraft draft, string macro, string stepId)
    {
        var validation = McpDraftStore.Validate(draft);
        return
        [
            .. validation.Blocking
                .Where(problem => problem.Macro == macro && problem.StepId == stepId)
                .Select(problem => (object)new { kind = "blocking", problem.Code, problem.Message }),
            .. validation.Hints
                .Where(problem => problem.Macro == macro && problem.StepId == stepId)
                .Select(problem => (object)new { kind = "hint", problem.Code, problem.Message }),
        ];
    }
}
