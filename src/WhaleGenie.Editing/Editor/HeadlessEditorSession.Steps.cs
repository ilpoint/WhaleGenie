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

/// <summary>Steps and their parameters: the body of the macro being edited.</summary>

internal sealed partial class HeadlessEditorSession
{
    public void Patch(string? name = null, string? description = null, string? group = null)
    {
        if (name is not null)
        {
            _model.Name = name;
        }

        if (description is not null)
        {
            _model.Description = description;
        }

        if (group is not null)
        {
            _model.Group = group;
        }
    }

    public void Undo()
    {
        if (!_model.CanUndo)
        {
            return;
        }

        _model.UndoCommand.Execute(null);
        _model.IsDirty = true;
    }

    public void Redo()
    {
        if (!_model.CanRedo)
        {
            return;
        }

        _model.RedoCommand.Execute(null);
        _model.IsDirty = true;
    }

    /// <summary>
    /// Adds one catalogue step at the root or inside a named child list of a block, then writes
    /// any values the caller already knows (a call step's macro, for one). The page selects the
    /// returned step, so the command answers with the object rather than only a fresh snapshot.
    /// </summary>
    public MacroStep AddStep(string type, string? parentId = null, string? listName = null,
        IReadOnlyDictionary<string, string>? presets = null)
    {
        var step = NewStep(type);
        if (parentId is not { Length: > 0 })
        {
            if (listName is { Length: > 0 })
            {
                throw new InvalidOperationException("a root step has no child list named " + listName);
            }

            _model.AddStep(step);
            _model.IsDirty = true;
        }
        else
        {
            var parent = FindStep(parentId)
                ?? throw new InvalidOperationException("the macro has no step " + parentId);
            var list = parent.Parameters.FirstOrDefault(parameter =>
                    parameter.Kind == ActionParameterKind.Steps
                    && (listName is not { Length: > 0 } || string.Equals(parameter.Name, listName,
                        StringComparison.Ordinal)))?.Steps
                ?? throw new InvalidOperationException(listName is { Length: > 0 }
                    ? "step " + parentId + " has no child list " + listName
                    : "step " + parentId + " holds no sub-steps");
            list.Add(step);
            _model.IsDirty = true;
        }

        if (presets is not null)
        {
            foreach (var (name, value) in presets)
            {
                WriteParameter(step, name, value);
            }
        }

        return step;
    }

    private static void WriteParameter(MacroStep step, string name, string value)
    {
        var parameter = step.Parameters.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("step " + step.Type + " has no parameter " + name);
        parameter.Written = WrittenFor(parameter.Kind, value);
    }


    public void PatchParameter(string stepId, string name, string value)
    {
        if (_actionSession is { } action)
        {
            var kind = action.Form.Parameters
                .FirstOrDefault(parameter => parameter.Definition.Name == name)
                ?.Definition.Kind ?? ActionParameterKind.Text;
            action.PatchField(name, kind switch
            {
                ActionParameterKind.Number => "number",
                ActionParameterKind.Bool => "bool",
                ActionParameterKind.Choice or ActionParameterKind.Step => "choice",
                ActionParameterKind.Key or ActionParameterKind.Keys => "key",
                _ => "text",
            }, value);
            return;
        }

        var step = FindStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        WriteParameter(step, name, value);
        _model.IsDirty = true;
    }

    public void SetStepsEnabled(IReadOnlyList<string> stepIds, bool enabled)
    {
        foreach (var step in FindSteps(stepIds))
        {
            step.Meta = step.Meta.WithEnabled(enabled);
        }

        _model.IsDirty = true;
    }

    public void RemoveSteps(IReadOnlyList<string> stepIds)
    {
        var steps = FindSteps(stepIds);
        if (steps.Count == 0)
        {
            return;
        }

        _model.SetSelection(steps);
        _model.DeleteSelectedCommand.Execute(null);
        _model.IsDirty = true;
    }

    public void DuplicateSteps(IReadOnlyList<string> stepIds)
    {
        var steps = FindSteps(stepIds);
        if (steps.Count == 0)
        {
            return;
        }

        _model.SetSelection(steps);
        _model.PasteSteps(steps);
        _model.IsDirty = true;
    }

    public void GroupSteps(IReadOnlyList<string> stepIds)
    {
        var steps = FindSteps(stepIds);
        if (steps.Count == 0)
        {
            return;
        }

        _model.SetSelection(steps);
        _model.GroupSelectedCommand.Execute(null);
        _model.IsDirty = true;
    }

    public void LiftSteps(IReadOnlyList<string> stepIds)
    {
        var steps = FindSteps(stepIds);
        if (steps.Count == 0)
        {
            return;
        }

        _model.SetSelection(steps);
        _model.LiftSelectedCommand.Execute(null);
        _model.IsDirty = true;
    }

    public void MoveStepsBy(IReadOnlyList<string> stepIds, int delta)
    {
        if (delta == 0)
        {
            return;
        }

        var steps = FindSteps(stepIds);
        if (steps.Count == 0)
        {
            return;
        }

        _model.SetSelection(steps);
        for (var index = 0; index < Math.Abs(delta); index++)
        {
            if (delta < 0)
            {
                _model.MoveSelectedUpCommand.Execute(null);
            }
            else
            {
                _model.MoveSelectedDownCommand.Execute(null);
            }
        }

        _model.IsDirty = true;
    }

    public void MoveStepsTo(IReadOnlyList<string> stepIds, string targetId, bool before)
    {
        var steps = FindSteps(stepIds);
        var target = Find(targetId);
        if (steps.Count == 0 || target is null)
        {
            return;
        }

        _model.SetSelection(steps);
        _model.MoveSelectionTo(target.Value.List, target.Value.Index + (before ? 0 : 1));
        _model.IsDirty = true;
    }

    /// <summary>
    /// Puts the picked steps inside a block's child list. Naming the list is what lets a drop land
    /// in a branch that has no row of its own yet: the block says which branch it means and the
    /// index says where in it, and an index left out appends.
    /// </summary>
    public void MoveStepsInto(IReadOnlyList<string> stepIds, string parentId, string? listName,
        int? index)
    {
        var steps = FindSteps(stepIds);
        var parent = FindStep(parentId)
            ?? throw new InvalidOperationException("the macro has no step " + parentId);
        var list = parent.StepLists.FirstOrDefault(parameter =>
                listName is not { Length: > 0 }
                || string.Equals(parameter.Name, listName, StringComparison.Ordinal))?.Steps
            ?? throw new InvalidOperationException(listName is { Length: > 0 }
                ? "step " + parentId + " has no child list " + listName
                : "step " + parentId + " holds no sub-steps");
        if (steps.Count == 0)
        {
            return;
        }

        _model.SetSelection(steps);
        _model.MoveSelectionTo(list, Math.Clamp(index ?? list.Count, 0, list.Count));
        _model.IsDirty = true;
    }

    public string CopySteps(IReadOnlyList<string> stepIds)
    {
        var text = MacroEditorViewModel.SerializeSteps(FindSteps(stepIds));
        ClipboardText = text;
        return text;
    }

    public string CutSteps(IReadOnlyList<string> stepIds)
    {
        var text = CopySteps(stepIds);
        RemoveSteps(stepIds);
        return text;
    }

    public void PasteSteps(string? text, string? afterStepId)
    {
        var payload = string.IsNullOrWhiteSpace(text) ? ClipboardText : text;
        var steps = MacroEditorViewModel.DeserializeSteps(payload);
        if (steps is null)
        {
            return;
        }

        var anchor = afterStepId is { Length: > 0 } id ? FindStep(id) : null;
        _model.SetSelection(anchor is null ? [] : [anchor]);
        _model.PasteSteps(steps);
        _model.IsDirty = true;
    }

    public void SetStepEnabled(string stepId, bool enabled)
    {
        var step = FindStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        step.Meta = step.Meta.WithEnabled(enabled);
        _model.IsDirty = true;
    }

    public void SetStepMeta(string stepId, string field, string value)
    {
        var step = FindStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        var meta = step.Meta;
        step.Meta = field switch
        {
            "comment" => CopyMeta(meta, comment: value),
            "timeoutMs" => CopyMeta(meta, timeoutMs: Number(value, meta.TimeoutMs)),
            "retryCount" => CopyMeta(meta, retryCount: Number(value, meta.RetryCount)),
            "retryDelayMs" => CopyMeta(meta, retryDelayMs: Number(value, meta.RetryDelayMs)),
            "onError" => CopyMeta(meta, onError: Enum.Parse<StepErrorAction>(value, ignoreCase: true)),
            _ => throw new InvalidOperationException("unknown step meta field " + field),
        };
        _model.IsDirty = true;
    }

    private static int Number(string value, int fallback)
        => int.TryParse(value, out var parsed) ? Math.Max(0, parsed) : fallback;

    private static StepMeta CopyMeta(StepMeta meta, string? comment = null, int? timeoutMs = null,
        int? retryCount = null, int? retryDelayMs = null, StepErrorAction? onError = null)
        => new()
        {
            Comment = comment ?? meta.Comment,
            IsEnabled = meta.IsEnabled,
            TimeoutMs = timeoutMs ?? meta.TimeoutMs,
            RetryCount = retryCount ?? meta.RetryCount,
            RetryDelayMs = retryDelayMs ?? meta.RetryDelayMs,
            RetryBackoff = meta.RetryBackoff,
            DelayBeforeMs = meta.DelayBeforeMs,
            DelayAfterMs = meta.DelayAfterMs,
            OnError = onError ?? meta.OnError,
        };

    public void AddParameterRow(string stepId, string name)
    {
        if (_actionSession is { } action)
        {
            action.AddRow(name);
            return;
        }

        var parameter = FindParameter(stepId, name);
        parameter.Rows.Add(new StepParameterRow());
        _model.IsDirty = true;
    }

    public void RemoveParameterRow(string stepId, string name, int index)
    {
        if (_actionSession is { } action)
        {
            action.RemoveRow(name, index);
            return;
        }

        var parameter = FindParameter(stepId, name);
        if (index < 0 || index >= parameter.Rows.Count)
        {
            throw new InvalidOperationException("step " + stepId + " has no row " + index);
        }

        parameter.Rows.RemoveAt(index);
        _model.IsDirty = true;
    }

    public void PatchParameterRow(string stepId, string name, int index, string column, string value)
    {
        if (_actionSession is { } action)
        {
            action.PatchRow(name, index, column, value);
            return;
        }

        var parameter = FindParameter(stepId, name);
        if (index < 0 || index >= parameter.Rows.Count)
        {
            throw new InvalidOperationException("step " + stepId + " has no row " + index);
        }

        var row = parameter.Rows[index];
        row.Set(column, ShapeOf(parameter, column, value));
        _model.IsDirty = true;
    }

    /// <summary>
    /// The shape a line typed into an inline row makes. A rectangle's four numbers are numbers;
    /// the one thing a region can name is read by the same rule the form uses, because a name that
    /// went through the line as text is how a region stopped pointing at its variable.
    /// </summary>
    private static ExpressionNode ShapeOf(StepParameter parameter, string column, string value)
    {
        if (parameter.Kind is ActionParameterKind.Region)
        {
            if (column == "text")
            {
                return RowShapes.RegionText(value);
            }

            if (decimal.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var number))
            {
                return Formula.Number((double)number);
            }
        }

        return Formula.Text(value);
    }

    private StepParameter FindParameter(string stepId, string name)
    {
        var step = FindStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        return step.Parameters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("step " + stepId + " has no parameter " + name);
    }

    public void MoveStep(string stepId, int delta)
    {
        var found = Find(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        var target = Math.Clamp(found.Index + delta, 0, found.List.Count - 1);
        if (target == found.Index)
        {
            return;
        }

        var step = found.List[found.Index];
        found.List.RemoveAt(found.Index);
        found.List.Insert(target, step);
        _model.IsDirty = true;
    }

    public void AppendSteps(IReadOnlyList<MacroStep> steps)
    {
        _model.AppendSteps(steps);
        _model.SettleStepIds();
        _model.IsDirty = true;
    }

    public void DuplicateStep(string stepId)
    {
        var found = Find(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        var copy = MacroStep.FromJson(found.List[found.Index].ToJson());
        found.List.Insert(found.Index + 1, copy);
        _model.SettleStepIds();
        _model.IsDirty = true;
    }

    public void RemoveStep(string stepId)
    {
        var found = Find(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        found.List.RemoveAt(found.Index);
        _model.IsDirty = true;
    }

}
