using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;
using WhaleGenie.Views;

namespace WhaleGenie.Editing;

/// <summary>
/// The editor as a session rather than a window: what C1's page will talk to. It owns the same
/// <see cref="MacroEditorViewModel"/> and the same <see cref="ProjectMacroCommit"/> rule the native
/// window used, so opening a macro, changing a field, adding a step and saving go through one C#
/// model. Nothing here needs Avalonia or a native window.
/// </summary>
internal sealed class HeadlessEditorSession
{
    private readonly ProjectWorkspace _workspace;
    private readonly MacroEditorViewModel _model;
    private MacroItem? _existing;

    private HeadlessEditorSession(ProjectWorkspace workspace, MacroItem? existing)
    {
        _workspace = workspace;
        _existing = existing;
        _model = new MacroEditorViewModel
        {
            Variables = workspace.Variables,
            Persisted = workspace.Persisted,
        };
        _model.LoadProjectTriggerSettings(workspace.Triggers, [.. workspace.Macros]);
        if (existing is not null)
        {
            _model.LoadFrom(existing, workspace.Triggers.ForMacro(existing.Name));
        }
        else
        {
            _model.UseDefaultName(workspace.Macros.Select(macro => macro.Name));
        }
    }

    public static HeadlessEditorSession Open(ProjectWorkspace workspace, string? name)
    {
        if (name is not { Length: > 0 })
        {
            return new HeadlessEditorSession(workspace, null);
        }

        var existing = workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            throw new InvalidOperationException("the project has no macro named " + name);
        }

        return new HeadlessEditorSession(workspace, existing);
    }

    public string Name => _model.Name;

    public string Description => _model.Description;

    public bool IsDirty => _model.IsDirty;

    public bool CanSave => _model.CanSave;

    public string SaveProblem => _model.NameProblem is { Length: > 0 } nameProblem
        ? nameProblem
        : _model.DeclarationProblemSummary is { Length: > 0 } declarationProblem
            ? declarationProblem
            : string.Empty;

    public IReadOnlyList<string> StepTypes
        => [.. _model.Draft().Macro.Steps.Select(step => step.Type)];

    public IReadOnlyList<EditorTriggerSnapshot> Triggers
        => [.. _model.TriggerEntries.Select(entry => new EditorTriggerSnapshot(
            entry.Id,
            entry.Kind.ToString(),
            entry.KindLabel,
            entry.Summary,
            entry.IsEnabled,
            entry.Spec is KeySpec key ? key.Binding : string.Empty))];

    public IReadOnlyList<EditorVariableSnapshot> Variables
        => [.. _model.DeclaredLocals.Select(local => new EditorVariableSnapshot(
            local.Name,
            WhaleGenie.Models.VariableTypes.Name(local.Type),
            Formula.Word(local.Initial),
            local.Description))];

    public static IReadOnlyList<EditorVariableTypeSnapshot> VariableTypes
        => [.. WhaleGenie.Models.VariableTypes.Declarable.Select(kind => new EditorVariableTypeSnapshot(
            WhaleGenie.Models.VariableTypes.Name(kind),
            Strings.Get($"Variable.Type.{WhaleGenie.Models.VariableTypes.Name(kind)}",
                WhaleGenie.Models.VariableTypes.Name(kind))))];

    public void Patch(string? name = null, string? description = null)
    {
        if (name is not null)
        {
            _model.Name = name;
        }

        if (description is not null)
        {
            _model.Description = description;
        }
    }

    public void AddStep(string type, string? parentId = null)
    {
        if (parentId is not { Length: > 0 })
        {
            _model.AddStep(new MacroStep { Type = type });
            return;
        }

        var parent = FindStep(parentId)
            ?? throw new InvalidOperationException("the macro has no step " + parentId);
        var list = parent.Parameters.FirstOrDefault(parameter =>
            parameter.Kind == ActionParameterKind.Steps)?.Steps
            ?? throw new InvalidOperationException("step " + parentId + " holds no sub-steps");
        list.Add(new MacroStep { Type = type });
        _model.IsDirty = true;
    }

    public void AddVariable()
    {
        _model.DeclaredLocals.Add(new MacroLocalVariable { Name = string.Empty, Type = ValueKind.Text });
        _model.IsDirty = true;
    }

    public void PatchVariable(string originalName, string field, string value)
    {
        var local = _model.DeclaredLocals.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), originalName.Trim(), StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no local variable " + originalName);
        switch (field)
        {
            case "name":
                if (!_model.RenameLocalVariable(originalName, value, out var problem))
                {
                    throw new InvalidOperationException(problem);
                }

                break;
            case "type":
                local.Type = WhaleGenie.Models.VariableTypes.KindOf(value);
                break;
            case "initial":
                local.Initial = Formula.Text(value);
                break;
            case "description":
                local.Description = value;
                break;
            default:
                throw new InvalidOperationException("unknown variable field " + field);
        }

        _model.IsDirty = true;
    }

    public void RemoveVariable(string name)
    {
        var local = _model.DeclaredLocals.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no local variable " + name);
        _model.DeclaredLocals.Remove(local);
        _model.IsDirty = true;
    }

    public void AddKeyTrigger()
    {
        _model.AddTriggerCommand.Execute(TriggerKind.Key);
        _model.IsDirty = true;
    }

    public void RemoveTrigger(string triggerId)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal));
        if (trigger is null)
        {
            throw new InvalidOperationException("the macro has no trigger " + triggerId);
        }

        _model.RemoveTriggerCommand.Execute(trigger);
    }

    public void SetTriggerBinding(string triggerId, string binding)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not KeySpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a key trigger");
        }

        // The drawer's fields are what BuildMacro commits; move the selection onto this entry and
        // write the binding there rather than changing a spec the next commit would overwrite.
        _model.SelectedTrigger = trigger;
        _model.BindKey = binding.Trim();
        _model.IsDirty = true;
    }

    public void PatchParameter(string stepId, string name, string value)
    {
        var step = FindStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        var parameter = step.Parameters.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("step " + stepId + " has no parameter " + name);
        parameter.Written = WrittenFor(parameter.Kind, value);
        _model.IsDirty = true;
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

    public void RemoveStep(string stepId)
    {
        var found = Find(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        found.List.RemoveAt(found.Index);
        _model.IsDirty = true;
    }

    /// <summary>Writes the draft through the same rule the native editor and the MCP save use.</summary>
    public bool Save()
    {
        if (!CanSave)
        {
            throw new InvalidOperationException(SaveProblem is { Length: > 0 } problem
                ? problem
                : Strings.Get("Editor.Problem.CannotSave"));
        }

        var commit = new ProjectMacroCommit(_workspace, _existing, MacroView.All,
            _ => { }, () => { }, () => { });
        var draft = _model.Draft();
        commit.Apply(draft, []);
        _model.IsDirty = false;
        _existing = _workspace.Macros.FirstOrDefault(candidate => candidate.Name == draft.Macro.Name);
        return _existing is not null;
    }

    /// <summary>What the page will draw: the macro's fields and its step tree as the engine reads it.</summary>
    public EditorSnapshot Snapshot()
    {
        _model.SettleStepIds();
        var macro = _model.Draft().Macro;
        return new EditorSnapshot(
            Name,
            Description,
            IsDirty,
            Count(macro.Steps),
            [.. macro.Steps.Select(StepSnapshot)],
            Triggers,
            Variables,
            VariableTypes);
    }

    /// <summary>The action catalogue, in the order the picker shows it and with its form fields.</summary>
    public static IReadOnlyList<EditorActionSnapshot> Actions()
        => [.. ActionCatalog.Definitions.Select(action => new EditorActionSnapshot(
            action.Key,
            action.LocalName,
            action.Category.ToString(),
            action.LocalDescription,
            action.Hidden,
            [.. action.Parameters
                .Where(parameter => parameter.Kind != ActionParameterKind.Steps)
                .Select(parameter => new EditorActionParameterSnapshot(
                    parameter.Name,
                    parameter.LocalLabel,
                    parameter.Kind.ToString(),
                    parameter.Required,
                    parameter.Placeholder,
                    [.. parameter.OptionChoices.Select(choice => new EditorChoiceSnapshot(
                        choice.Value, choice.Display))]))]))];

    private static int Count(IReadOnlyList<MacroStep> steps)
        => steps.Count + steps.Sum(step => step.Parameters
            .Where(parameter => parameter.Kind == ActionParameterKind.Steps)
            .Sum(parameter => Count(parameter.Steps)));

    private static ExpressionNode WrittenFor(ActionParameterKind kind, string value)
        => kind switch
        {
            ActionParameterKind.Bool or ActionParameterKind.Choice => Formula.Constant(value),
            ActionParameterKind.Number
                when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var number) => Formula.Number(value, number),
            _ => Formula.Text(value),
        };

    private static EditorStepSnapshot StepSnapshot(MacroStep step)
    {
        var definition = ActionCatalog.Definitions.FirstOrDefault(action => action.Key == step.Type);
        var parameters = step.Parameters
            .Where(parameter => parameter.Kind != ActionParameterKind.Steps)
            .Select(parameter =>
            {
                var field = definition?.Parameters.FirstOrDefault(candidate =>
                    candidate.Name == parameter.Name);
                return new EditorParameterSnapshot(
                    parameter.Name,
                    field?.LocalLabel ?? parameter.Name,
                    parameter.Kind.ToString(),
                    parameter.Value,
                    [.. field?.OptionChoices.Select(choice => new EditorChoiceSnapshot(
                        choice.Value, choice.Display)) ?? []]);
            })
            .ToList();
        var children = step.Parameters
            .Where(parameter => parameter.Kind == ActionParameterKind.Steps)
            .SelectMany(parameter => parameter.Steps)
            .Select(StepSnapshot)
            .ToList();
        var acceptsSteps = definition?.Parameters.Any(parameter =>
            parameter.Kind == ActionParameterKind.Steps) == true;
        return new EditorStepSnapshot(step.Id, step.Type,
            definition?.LocalName ?? step.Type, step.Meta.IsEnabled, acceptsSteps, parameters, children);
    }

    private MacroStep? FindStep(string stepId)
        => Find(stepId) is { } found ? found.List[found.Index] : null;

    private (IList<MacroStep> List, int Index)? Find(string stepId)
    {
        foreach (var (list, index) in Walk(_model.Steps))
        {
            if (string.Equals(list[index].Id, stepId, StringComparison.Ordinal))
            {
                return (list, index);
            }
        }

        return null;
    }

    private static IEnumerable<(IList<MacroStep> List, int Index)> Walk(IList<MacroStep> steps)
    {
        for (var index = 0; index < steps.Count; index++)
        {
            yield return (steps, index);
            foreach (var children in steps[index].Parameters
                         .Where(parameter => parameter.Kind == ActionParameterKind.Steps)
                         .SelectMany(parameter => Walk(parameter.Steps)))
            {
                yield return children;
            }
        }
    }
}

internal sealed record EditorSnapshot(string Name, string Description, bool Dirty, int StepCount,
    IReadOnlyList<EditorStepSnapshot> Steps, IReadOnlyList<EditorTriggerSnapshot> Triggers,
    IReadOnlyList<EditorVariableSnapshot> Variables, IReadOnlyList<EditorVariableTypeSnapshot> VariableTypes);

internal sealed record EditorVariableSnapshot(string Name, string Type, string Initial, string Description);

internal sealed record EditorVariableTypeSnapshot(string Value, string Label);

internal sealed record EditorTriggerSnapshot(string Id, string Kind, string KindLabel, string Summary,
    bool Enabled, string Binding);

internal sealed record EditorStepSnapshot(string Id, string Type, string Name, bool Enabled,
    bool AcceptsSteps, IReadOnlyList<EditorParameterSnapshot> Parameters,
    IReadOnlyList<EditorStepSnapshot> Steps);

internal sealed record EditorParameterSnapshot(string Name, string Label, string Kind, string Value,
    IReadOnlyList<EditorChoiceSnapshot> Options);

internal sealed record EditorChoiceSnapshot(string Value, string Label);

internal sealed record EditorActionSnapshot(string Key, string Name, string Category, string Description,
    bool Hidden, IReadOnlyList<EditorActionParameterSnapshot> Parameters);

internal sealed record EditorActionParameterSnapshot(string Name, string Label, string Kind, bool Required,
    string Placeholder, IReadOnlyList<EditorChoiceSnapshot> Options);
