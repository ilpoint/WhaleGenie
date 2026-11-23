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

/// <summary>The value editor: scalar and condition values a step opens.</summary>

internal sealed partial class HeadlessEditorSession
{
    public EditorValueSnapshot OpenValue(string stepId, string parameterName)
    {
        if (_actionSession is { } action)
        {
            return action.OpenValue(parameterName);
        }

        var step = FindStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        var definition = ActionCatalog.Find(step.Type)
            ?? throw new InvalidOperationException("unknown action " + step.Type);
        var parameter = definition.Parameters.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, parameterName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the step has no parameter " + parameterName);
        var stored = step.Parameters.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, parameterName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the step has no stored parameter " + parameterName);

        var field = new StepParameterViewModel(parameter, _model.CollectVariables(), [],
            _model.StepChoices());
        field.ApplyValue(stored);
        var picker = new ValuePickerViewModel(field.CurrentShape, field.Variables,
            parameter.ValueForm, field.Label, field.AllowsNewName);

        _valueSession = new HeadlessValueSession(picker, value =>
        {
            field.WriteValue(value);
            var updated = field.ToStepParameter();
            var index = step.Parameters.FindIndex(candidate =>
                string.Equals(candidate.Name, parameterName, StringComparison.Ordinal));
            if (index >= 0)
            {
                step.Parameters[index] = updated;
            }
            else
            {
                step.Parameters.Add(updated);
            }
        }, () => _model.IsDirty = true);

        return _valueSession.Snapshot();
    }

    public EditorValueSnapshot OpenConditionValue(string stepId, string name, int[] path, string field)
    {
        if (_actionSession is { } action)
        {
            return action.OpenConditionValue(name, path, field);
        }

        var parameter = FindParameter(stepId, name);
        var draft = EnsureConditionDraft(parameter);
        var row = ConditionRowAt(draft.Root, path);
        var seed = field switch
        {
            "left" => row.Left,
            "right" => row.Right,
            _ => throw new InvalidOperationException("unknown condition field " + field),
        };
        var part = field == "left"
            ? Strings.Get("Editor.ConditionLeft")
            : Strings.Get("Editor.ConditionRight");
        var picker = new ValuePickerViewModel(seed, _model.CollectVariables(),
            ActionValueForm.Value, parameter.Name + " · " + part);
        _valueSession = new HeadlessValueSession(picker, value =>
        {
            if (field == "left")
            {
                row.Left = value;
            }
            else
            {
                row.Right = value;
            }

            parameter.Written = Condition.ToNode(draft.Root.ToShape()) ?? Formula.Nothing;
            _model.IsDirty = true;
        }, () => _model.IsDirty = true);
        return _valueSession.Snapshot();
    }

    public EditorValueSnapshot OpenRowValue(string stepId, string name, int rowIndex, string column)
    {
        if (_actionSession is { } action)
        {
            return action.OpenRowValue(name, rowIndex, column);
        }

        var parameter = FindParameter(stepId, name);
        if (rowIndex < 0 || rowIndex >= parameter.Rows.Count)
        {
            throw new InvalidOperationException("the row table has no row " + rowIndex);
        }

        var row = parameter.Rows[rowIndex];
        var seed = row.Cells.TryGetValue(column, out var existing) ? existing : Formula.Nothing;
        var picker = new ValuePickerViewModel(seed, _model.CollectVariables(),
            ActionValueForm.Value, parameter.Name + " · " + column);
        _valueSession = new HeadlessValueSession(picker, value =>
        {
            row.Cells[column] = value;
            _model.IsDirty = true;
        }, () => _model.IsDirty = true);
        return _valueSession.Snapshot();
    }

    public EditorValueSnapshot ValueState()
        => _actionSession?.ValueState()
           ?? _valueSession?.Snapshot()
           ?? throw new InvalidOperationException("no value field is open");

    public EditorValueSnapshot ValueCommand(ValueEditorCommand command)
        => _actionSession?.ValueCommand(command)
           ?? _valueSession?.Execute(command)
           ?? throw new InvalidOperationException("no value field is open");

    public EditorValueOutcome ConfirmValue()
    {
        if (_actionSession is { } action)
        {
            return action.ConfirmValue();
        }

        var session = _valueSession
            ?? throw new InvalidOperationException("no value field is open");
        var outcome = session.Confirm();
        if (outcome.Closed)
        {
            _valueSession = null;
        }

        return outcome;
    }

    public EditorValueOutcome CancelValue()
    {
        if (_actionSession is { } action)
        {
            return action.CancelValue();
        }

        var session = _valueSession
            ?? throw new InvalidOperationException("no value field is open");
        var outcome = session.Cancel();
        _valueSession = null;
        return outcome;
    }

    /// <summary>Switching or closing a document must not leave a picker holding the old field.</summary>
    public void CloseValueEditor()
    {
        _valueSession = null;
        _actionSession?.CloseValueEditor();
    }

}
