using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Models;

public enum VariableDropStatus
{
    Direct,
    ChooseField,
    Rejected,
}

/// <summary>What a variable drop should write, or which fields the person must choose between.</summary>
public sealed record VariableDropResolution(
    VariableDropStatus Status,
    string Text = "",
    IReadOnlyList<VariableDragField>? Fields = null)
{
    public static VariableDropResolution Reject() => new(VariableDropStatus.Rejected);
}

/// <summary>
/// Maps one variable onto a parameter without guessing a record field. Exact shapes go in whole;
/// a record with one matching field becomes field(); several matching fields are handed back for
/// the person to choose.
/// </summary>
public static class VariableDropResolver
{
    public static VariableDropResolution Resolve(ActionParameter target, VariableDragPayload variable)
    {
        if (target.Kind is ActionParameterKind.Choice or ActionParameterKind.Bool
            or ActionParameterKind.Steps
            or ActionParameterKind.Region or ActionParameterKind.KeySequence
            or ActionParameterKind.Images or ActionParameterKind.Returns
            or ActionParameterKind.Narrow)
        {
            return VariableDropResolution.Reject();
        }

        if (target.ReadMode is ActionReadMode.Text)
        {
            return Direct(variable, target.NamesVariable);
        }

        if (target.ReadMode is ActionReadMode.VariableName)
        {
            return new VariableDropResolution(VariableDropStatus.Direct, variable.Name);
        }

        if (target.Kind is ActionParameterKind.Text or ActionParameterKind.MultilineText
            or ActionParameterKind.Expression or ActionParameterKind.Variable
            or ActionParameterKind.Macro or ActionParameterKind.Step
            or ActionParameterKind.Color)
        {
            return Direct(variable);
        }

        if (target.Shape is ActionValueShape.Point or ActionValueShape.Rect
            or ActionValueShape.WindowRule)
        {
            return HasFields(variable, RequiredFields(target.Shape))
                ? Direct(variable)
                : VariableDropResolution.Reject();
        }

        var exact = target.Kind switch
        {
            ActionParameterKind.Number => "number",
            ActionParameterKind.Bool => "bool",
            ActionParameterKind.Record => "record",
            ActionParameterKind.Image => "image",
            _ when target.Shape is ActionValueShape.List => "list",
            _ when target.Shape is ActionValueShape.Record => "record",
            _ when target.Shape is ActionValueShape.Image => "image",
            _ when target.Shape is ActionValueShape.Flag => "bool",
            _ when target.Shape is ActionValueShape.Number => "number",
            _ => string.Empty,
        };

        if (exact.Length == 0)
        {
            return VariableDropResolution.Reject();
        }

        if (string.Equals(variable.Type, exact, StringComparison.OrdinalIgnoreCase))
        {
            return Direct(variable);
        }

        if (!string.Equals(variable.Type, "record", StringComparison.OrdinalIgnoreCase)
            || exact is not ("number" or "bool" or "text"))
        {
            return VariableDropResolution.Reject();
        }

        var candidates = variable.Fields
            .Where(field => TypeName(field.Type) == exact)
            .ToList();
        return candidates.Count switch
        {
            0 => VariableDropResolution.Reject(),
            1 => new VariableDropResolution(
                VariableDropStatus.Direct,
                $"field({variable.Token}, \"{candidates[0].Name}\")"),
            _ => new VariableDropResolution(
                VariableDropStatus.ChooseField, Fields: candidates),
        };
    }

    public static string FieldText(VariableDragPayload variable, VariableDragField field)
        => $"field({variable.Token}, \"{field.Name}\")";

    private static VariableDropResolution Direct(VariableDragPayload variable, bool bare = false)
        => new(VariableDropStatus.Direct, bare ? variable.Name : variable.Token);

    private static bool HasFields(VariableDragPayload variable, IReadOnlyList<string> wanted)
    {
        if (!string.Equals(variable.Type, "record", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var names = variable.Fields.Select(field => field.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return wanted.All(names.Contains);
    }

    private static IReadOnlyList<string> RequiredFields(ActionValueShape shape) => shape switch
    {
        ActionValueShape.Point => ["x", "y"],
        ActionValueShape.Rect => ["left", "top", "width", "height"],
        ActionValueShape.WindowRule =>
            ["title", "process", "class", "how", "caseSensitive", "trim", "multiline"],
        _ => [],
    };

    private static string TypeName(ActionReturnType type) => type switch
    {
        ActionReturnType.Number => "number",
        ActionReturnType.Flag => "bool",
        ActionReturnType.List => "list",
        ActionReturnType.Record => "record",
        ActionReturnType.Image => "image",
        _ => "text",
    };
}
