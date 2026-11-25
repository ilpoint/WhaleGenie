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

/// <summary>The values a macro reads and offers: project globals, local variables and the picker's choices.</summary>

internal sealed partial class HeadlessEditorSession
{
    public void AddGlobal()
    {
        var draft = EnsureGlobalDraft();
        var name = "variable";
        for (var index = 2; draft.Any(entry => string.Equals(entry.Variable.Name, name,
                StringComparison.OrdinalIgnoreCase)); index++)
        {
            name = "variable" + index.ToString(CultureInfo.InvariantCulture);
        }

        draft.Add(new GlobalDraftEntry(new VariableDefinition
        {
            Name = name,
            Scope = VariableScope.Global,
        }, name, true));
        _model.IsDirty = true;
    }

    public void PatchGlobal(string originalName, string field, string value)
    {
        var draft = EnsureGlobalDraft();
        var entry = draft.FirstOrDefault(candidate => string.Equals(
            candidate.Variable.Name, originalName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the project has no global " + originalName);
        var next = value.Trim();
        switch (field)
        {
            case "name":
                if (draft.Any(candidate => !ReferenceEquals(candidate, entry)
                    && string.Equals(candidate.Variable.Name, next, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("the project already has a global " + next);
                }

                entry.Variable.Name = next;
                break;
            case "type":
                entry.Variable.Type = WhaleGenie.Models.VariableTypes.KindOf(value);
                break;
            case "default":
                entry.Variable.Default = Formula.Text(value);
                break;
            case "description":
                entry.Variable.Description = value;
                break;
            case "persist":
                entry.Variable.PersistValue = bool.TryParse(value, out var persist) && persist;
                break;
            default:
                throw new InvalidOperationException("unknown global field " + field);
        }

        _model.IsDirty = true;
    }

    public void RemoveGlobal(string name)
    {
        var draft = EnsureGlobalDraft();
        var entry = draft.FirstOrDefault(candidate => string.Equals(
            candidate.Variable.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the project has no global " + name);
        draft.Remove(entry);
        _model.IsDirty = true;
    }

    private List<GlobalDraftEntry> EnsureGlobalDraft()
        => _globalDraft ??= [.. _workspace.Variables.Globals.Select(global =>
            new GlobalDraftEntry(CloneGlobal(global), global.Name, false))];

    private static VariableDefinition CloneGlobal(VariableDefinition global)
        => new()
        {
            Name = global.Name,
            Scope = VariableScope.Global,
            Type = global.Type,
            Description = global.Description,
            Default = global.Default,
            PersistValue = global.PersistValue,
        };

    private void CommitGlobals()
    {
        if (_globalDraft is null)
        {
            return;
        }

        var draftNames = _globalDraft.Select(entry => entry.OriginalName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var global in _workspace.Variables.Globals.ToList())
        {
            if (!draftNames.Contains(global.Name))
            {
                _workspace.Persisted.Forget(global.Name);
                _workspace.Variables.Values.Remove(global.Name);
            }
        }

        foreach (var entry in _globalDraft)
        {
            if (!entry.IsNew
                && !string.Equals(entry.OriginalName, entry.Variable.Name, StringComparison.Ordinal))
            {
                VariableRename.RenameGlobal([.. _workspace.Macros], entry.OriginalName,
                    entry.Variable.Name);
                _model.RenameGlobalReferences(entry.OriginalName, entry.Variable.Name);
                _workspace.Persisted.Rename(entry.OriginalName, entry.Variable.Name);
                _workspace.Variables.Values.Rename(entry.OriginalName, entry.Variable.Name);
            }
        }

        _workspace.Variables.ClearGlobals();
        foreach (var entry in _globalDraft)
        {
            _workspace.Variables.Adopt(entry.Variable);
        }

        _workspace.MarkGlobalsChanged();
        _globalDraft = null;
    }

    private sealed record GlobalDraftEntry(VariableDefinition Variable, string OriginalName, bool IsNew);

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

    public IReadOnlyList<EditorVariableSnapshot> Variables
        => [
            .. _model.DeclaredLocals.Select(local => new EditorVariableSnapshot(
                local.Name,
                WhaleGenie.Models.VariableTypes.Name(local.Type),
                Formula.Word(local.Initial),
                local.Description,
                Strings.Get("LocalVariables.Declared"),
                true)),
            .. _model.CollectVariables()
                .Where(choice => choice.Origin == VariableOrigin.Local)
                .OrderBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase)
                .Select(choice => new EditorVariableSnapshot(
                    choice.Name,
                    WhaleGenie.Models.VariableTypes.Name(choice.Type ?? ValueKind.Text),
                    string.Empty,
                    choice.Description,
                    choice.Source,
                    false)),
        ];

    public IReadOnlyList<EditorValueChoiceSnapshot> ValueChoices
        => [.. _model.CollectVariables()
            .Where(choice => choice.Origin != VariableOrigin.System)
            .Select(ChoiceSnapshot)];

    /// <summary>
    /// The values every macro can read, sent once with the catalogue rather than with every
    /// document snapshot. Uses counts are not part of this shape, so no macro-specific work is
    /// done here.
    /// </summary>
    public static IReadOnlyList<EditorValueChoiceSnapshot> SystemValueChoices
        => [.. VariableChoices.For([], []).Select(ChoiceSnapshot)];

    private static EditorValueChoiceSnapshot ChoiceSnapshot(VariableChoice choice)
        => new(
            choice.Name,
            choice.Type is { } type ? WhaleGenie.Models.VariableTypes.Name(type) : "text",
            choice.Origin.ToString(),
            choice.Source,
            choice.Description,
            choice.Group.ToString(),
            choice.Display,
            choice.IsPersistent);

    public IReadOnlyList<EditorGlobalSnapshot> Globals
        => [.. (_globalDraft?.Select(entry => entry.Variable) ?? _workspace.Variables.Globals)
            .Select(variable => new EditorGlobalSnapshot(
                variable.Name,
                WhaleGenie.Models.VariableTypes.Name(variable.Type),
                Formula.Word(variable.Default),
                variable.Description,
                variable.PersistValue))];

}
