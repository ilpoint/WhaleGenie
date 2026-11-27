using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

/// <summary>The callable surface of a macro: the parameters and results it declares, and the fields of each.</summary>

internal sealed partial class HeadlessEditorSession
{
    public IReadOnlyList<EditorMacroParameterSnapshot> CallableParameters
        => [.. _model.DeclaredParameters.Select(parameter => new EditorMacroParameterSnapshot(
            parameter.Name,
            WhaleGenie.Models.VariableTypes.Name(parameter.Type),
            parameter.Required,
            Formula.Word(parameter.Default),
            parameter.Description,
            FieldSnapshots(parameter.Fields),
            ActionValueForm.Value.ToString(),
            EditorValueParts.Of(parameter.Default)))];

    public IReadOnlyList<EditorMacroResultSnapshot> CallableResults
        => [.. _model.DeclaredResults.Select(result => new EditorMacroResultSnapshot(
            result.Name,
            WhaleGenie.Models.VariableTypes.Name(result.Type),
            result.Description,
            FieldSnapshots(result.Fields)))];

    private static IReadOnlyList<EditorMacroFieldSnapshot> FieldSnapshots(List<MacroField> fields)
        => [.. fields.Select(field => new EditorMacroFieldSnapshot(
            field.Name, WhaleGenie.Models.VariableTypes.Name(field.Type)))];

    public void AddCallableParameter()
    {
        _model.DeclaredParameters.Add(new MacroParameter());
        _model.IsDirty = true;
    }

    public void PatchCallableParameter(int index, string field, string value,
        IReadOnlyList<EditorValuePartSnapshot>? parts = null)
    {
        if (index < 0 || index >= _model.DeclaredParameters.Count)
        {
            throw new InvalidOperationException("the macro has no parameter " + index);
        }

        var parameter = _model.DeclaredParameters[index];
        switch (field)
        {
            case "name":
                parameter.Name = value.Trim();
                break;
            case "type":
                parameter.Type = WhaleGenie.Models.VariableTypes.KindOf(value);
                break;
            case "required":
                parameter.Required = bool.TryParse(value, out var required) && required;
                break;
            case "default":
                parameter.Default = parts is { Count: > 0 }
                    ? EditorValueParts.ShapeOf(parts)
                    : Formula.Text(value);
                break;
            case "description":
                parameter.Description = value;
                break;
            default:
                throw new InvalidOperationException("unknown parameter field " + field);
        }

        _model.IsDirty = true;
    }

    public void MoveCallableParameter(int index, int offset)
    {
        var target = index + offset;
        if (index < 0 || index >= _model.DeclaredParameters.Count
            || target < 0 || target >= _model.DeclaredParameters.Count)
        {
            return;
        }

        (_model.DeclaredParameters[index], _model.DeclaredParameters[target]) =
            (_model.DeclaredParameters[target], _model.DeclaredParameters[index]);
        _model.IsDirty = true;
    }

    public void MoveCallableResult(int index, int offset)
    {
        var target = index + offset;
        if (index < 0 || index >= _model.DeclaredResults.Count
            || target < 0 || target >= _model.DeclaredResults.Count)
        {
            return;
        }

        (_model.DeclaredResults[index], _model.DeclaredResults[target]) =
            (_model.DeclaredResults[target], _model.DeclaredResults[index]);
        _model.IsDirty = true;
    }

    public void RemoveCallableParameter(int index)
    {
        if (index < 0 || index >= _model.DeclaredParameters.Count)
        {
            throw new InvalidOperationException("the macro has no parameter " + index);
        }

        _model.DeclaredParameters.RemoveAt(index);
        _model.IsDirty = true;
    }

    public void AddCallableParameterField(int index)
    {
        var parameter = CallableParameterAt(index);
        parameter.Fields.Add(new MacroField());
        _model.IsDirty = true;
    }

    public void PatchCallableParameterField(int index, int fieldIndex, string field, string value)
    {
        var parameter = CallableParameterAt(index);
        if (fieldIndex < 0 || fieldIndex >= parameter.Fields.Count)
        {
            throw new InvalidOperationException("the parameter has no field " + fieldIndex);
        }

        var entry = parameter.Fields[fieldIndex];
        switch (field)
        {
            case "name":
                entry.Name = value.Trim();
                break;
            case "type":
                entry.Type = WhaleGenie.Models.VariableTypes.KindOf(value);
                break;
            default:
                throw new InvalidOperationException("unknown field setting " + field);
        }

        _model.IsDirty = true;
    }

    public void RemoveCallableParameterField(int index, int fieldIndex)
    {
        var parameter = CallableParameterAt(index);
        if (fieldIndex < 0 || fieldIndex >= parameter.Fields.Count)
        {
            throw new InvalidOperationException("the parameter has no field " + fieldIndex);
        }

        parameter.Fields.RemoveAt(fieldIndex);
        _model.IsDirty = true;
    }

    public void AddCallableResultField(int index)
    {
        var result = CallableResultAt(index);
        result.Fields.Add(new MacroField());
        _model.IsDirty = true;
    }

    public void PatchCallableResultField(int index, int fieldIndex, string field, string value)
    {
        var result = CallableResultAt(index);
        if (fieldIndex < 0 || fieldIndex >= result.Fields.Count)
        {
            throw new InvalidOperationException("the result has no field " + fieldIndex);
        }

        var entry = result.Fields[fieldIndex];
        switch (field)
        {
            case "name":
                entry.Name = value.Trim();
                break;
            case "type":
                entry.Type = WhaleGenie.Models.VariableTypes.KindOf(value);
                break;
            default:
                throw new InvalidOperationException("unknown field setting " + field);
        }

        _model.IsDirty = true;
    }

    public void RemoveCallableResultField(int index, int fieldIndex)
    {
        var result = CallableResultAt(index);
        if (fieldIndex < 0 || fieldIndex >= result.Fields.Count)
        {
            throw new InvalidOperationException("the result has no field " + fieldIndex);
        }

        result.Fields.RemoveAt(fieldIndex);
        _model.IsDirty = true;
    }

    private MacroParameter CallableParameterAt(int index)
        => index >= 0 && index < _model.DeclaredParameters.Count
            ? _model.DeclaredParameters[index]
            : throw new InvalidOperationException("the macro has no parameter " + index);

    private MacroResult CallableResultAt(int index)
        => index >= 0 && index < _model.DeclaredResults.Count
            ? _model.DeclaredResults[index]
            : throw new InvalidOperationException("the macro has no result " + index);

    public void AddCallableResult()
    {
        _model.DeclaredResults.Add(new MacroResult());
        _model.IsDirty = true;
    }

    public void PatchCallableResult(int index, string field, string value)
    {
        if (index < 0 || index >= _model.DeclaredResults.Count)
        {
            throw new InvalidOperationException("the macro has no result " + index);
        }

        var result = _model.DeclaredResults[index];
        switch (field)
        {
            case "name":
                result.Name = value.Trim();
                break;
            case "type":
                result.Type = WhaleGenie.Models.VariableTypes.KindOf(value);
                break;
            case "description":
                result.Description = value;
                break;
            default:
                throw new InvalidOperationException("unknown result field " + field);
        }

        _model.IsDirty = true;
    }

    public void RemoveCallableResult(int index)
    {
        if (index < 0 || index >= _model.DeclaredResults.Count)
        {
            throw new InvalidOperationException("the macro has no result " + index);
        }

        _model.DeclaredResults.RemoveAt(index);
        _model.IsDirty = true;
    }

}
