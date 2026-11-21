using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
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

    public IReadOnlyList<string> StepTypes
        => [.. _model.Draft().Macro.Steps.Select(step => step.Type)];

    public IReadOnlyList<JsonObject> Steps
        => [.. _model.Draft().Macro.Steps.Select(step => step.ToJson())];

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

    public void AddStep(string type) => _model.AddStep(new MacroStep { Type = type });

    /// <summary>Writes the draft through the same rule the native editor and the MCP save use.</summary>
    public bool Save()
    {
        var commit = new ProjectMacroCommit(_workspace, _existing, MacroView.All,
            _ => { }, () => { }, () => { });
        var draft = _model.Draft();
        commit.Apply(draft, []);
        _existing = _workspace.Macros.FirstOrDefault(candidate => candidate.Name == draft.Macro.Name);
        return _existing is not null;
    }

    /// <summary>What the page will draw: the macro's fields and its step tree as the engine reads it.</summary>
    public EditorSnapshot Snapshot()
    {
        var macro = _model.Draft().Macro;
        return new EditorSnapshot(
            Name,
            Description,
            IsDirty,
            macro.Steps.Count,
            [.. macro.Steps.Select(step => step.ToJson().ToJsonString())]);
    }
}

internal sealed record EditorSnapshot(string Name, string Description, bool Dirty, int StepCount,
    IReadOnlyList<string> Steps);
