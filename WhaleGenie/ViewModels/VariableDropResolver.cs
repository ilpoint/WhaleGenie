using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

public enum VariableDropStatus
{
    Direct,
    ChooseField,
    Rejected,
}

/// <summary>
/// What a variable drop should write, or which fields the person must choose between. The answer
/// is a shape, not a line: a dropped name is a reference and a record field is a call, so nobody
/// has to spell a name with a mark and read the line back into a value afterwards.
/// </summary>
public sealed record VariableDropResolution(
    VariableDropStatus Status,
    ExpressionNode? Shape = null,
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

        if (target.ValueForm is ActionValueForm.Text or ActionValueForm.Run)
        {
            return Direct(variable, target.NamesVariable);
        }

        if (target.ValueForm is ActionValueForm.Name)
        {
            return new VariableDropResolution(VariableDropStatus.Direct, Formula.Text(variable.Name));
        }

        if (target.Kind is ActionParameterKind.Text or ActionParameterKind.MultilineText
            or ActionParameterKind.Expression or ActionParameterKind.Condition
            or ActionParameterKind.Variable
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
            _ when target.Shape is ActionValueShape.Bool => "bool",
            _ when target.Shape is ActionValueShape.Number => "number",
            _ => string.Empty,
        };

        if (exact.Length == 0)
        {
            return VariableDropResolution.Reject();
        }

        if (string.Equals(VariableTypes.Name(variable.Type ?? ValueKind.Text), exact,
                StringComparison.OrdinalIgnoreCase))
        {
            return Direct(variable);
        }

        if (variable.Type is not ValueKind.Record || exact is not ("number" or "bool" or "text"))
        {
            return VariableDropResolution.Reject();
        }

        var candidates = variable.Fields
            .Where(field => TypeName(field.Type) == exact)
            .ToList();
        return candidates.Count switch
        {
            0 => VariableDropResolution.Reject(),
            1 => new VariableDropResolution(VariableDropStatus.Direct,
                FieldShape(variable, candidates[0])),
            _ => new VariableDropResolution(
                VariableDropStatus.ChooseField, Fields: candidates),
        };
    }

    /// <summary>
    /// A record's field as a call on the record. The field's name is plain text, not a name to
    /// look up: the engine reads it as the column of that record.
    /// </summary>
    public static ExpressionNode FieldShape(VariableDragPayload variable, VariableDragField field)
        => Formula.Call("field", Formula.Name(variable.Name), Formula.Text(field.Name));

    /// <summary>
    /// A field that names a variable wants the word itself; any other field wants a reference to
    /// it. Neither is written as a line with a mark in front of the name.
    /// </summary>
    private static VariableDropResolution Direct(VariableDragPayload variable, bool names = false)
        => new(VariableDropStatus.Direct,
            names ? Formula.Text(variable.Name) : Formula.Name(variable.Name));

    private static bool HasFields(VariableDragPayload variable, IReadOnlyList<string> wanted)
    {
        if (variable.Type is not ValueKind.Record)
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

    private static string TypeName(ValueKind type) => type switch
    {
        ValueKind.Number => "number",
        ValueKind.Bool => "bool",
        ValueKind.List => "list",
        ValueKind.Record => "record",
        ValueKind.Image => "image",
        ValueKind.Date => "date",
        _ => "text",
    };
}
