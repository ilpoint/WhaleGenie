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

/// <summary>
/// The editor as a session rather than a window: what C1's page will talk to. It owns the same
/// <see cref="MacroEditorViewModel"/> and the same <see cref="ProjectMacroCommit"/> rule the native
/// window used, so opening a macro, changing a field, adding a step and saving go through one C#
/// model. Nothing here needs Avalonia or a native window.
/// </summary>
internal sealed partial class HeadlessEditorSession
{
    private readonly ProjectWorkspace _workspace;
    private readonly MacroEditorViewModel _model;
    private readonly HeadlessEditorSession? _owner;
    private string _submacroOriginal = string.Empty;
    private string _originalName = string.Empty;
    private readonly Dictionary<StepParameter, ConditionDraft> _conditionDrafts = [];
    private List<GlobalDraftEntry>? _globalDraft;
    private readonly IDeviceLayer? _devices;
    private readonly IEditorSettings _settings;
    private HeadlessValueSession? _valueSession;
    private MacroItem? _existing;
    private HeadlessActionSession? _actionSession;
    private ActionTarget? _actionTarget;
    private int _revision;

    private sealed record ActionTarget(string ParentId, string ListName, string TargetStepId, bool Before);


    private HeadlessEditorSession(ProjectWorkspace workspace, MacroItem? existing,
        HeadlessEditorSession? owner = null, string submacroOriginal = "",
        IDeviceLayer? devices = null, IEditorSettings? settings = null)
    {
        _workspace = workspace;
        _existing = existing;
        _owner = owner;
        _devices = devices;
        _settings = settings ?? DefaultEditorSettings.Instance;
        _submacroOriginal = submacroOriginal;
        _model = new MacroEditorViewModel
        {
            Variables = workspace.Variables,
            Persisted = workspace.Persisted,
            Settings = _settings,
        };
        _originalName = existing is { } opened
            ? opened.Name.Trim()
            : submacroOriginal.Trim();
        _model.LoadProjectTriggerSettings(workspace.Triggers, [.. workspace.Macros]);
        if (existing is not null)
        {
            _model.LoadFrom(existing, owner is null
                ? workspace.Triggers.ForMacro(existing.Name)
                : []);
        }
        else
        {
            _model.UseDefaultName(workspace.Macros.Select(macro => macro.Name));
        }

        // A submacro document has no triggers and no arm key of its own; the editor form and the
        // tab strip read this flag.
        _model.IsSubMacro = owner is not null;
        _model.MacroLifted += lifted =>
        {
            _workspace.AddMacro(lifted);
            RefreshCallables();
        };
        RefreshCallables();

        // Reading the document into the model must not count as an edit. Some of the refresh
        // paths above write observable properties the dirty check hears; opening a macro and
        // closing it again is the one thing a person should never be asked to save.
        _model.IsDirty = false;
    }

    /// <summary>
    /// Hands the model the macros a call step may reach: the project's macros, this document's own
    /// children, and — for a submacro document — its siblings. The native window did this when it
    /// opened; without it every <c>control.runMacro</c> step reads as a call to a macro that is not
    /// there, and the call step's picker has nothing to offer.
    /// </summary>
    private void RefreshCallables()
    {
        IEnumerable<MacroItem> siblings = _owner is { } owner ? owner._model.SubMacros : [];
        _model.SetProjectMacros([.. _workspace.Macros
            .Where(macro => !ReferenceEquals(macro, _existing))
            .Select(macro => macro.Callable)
            .Concat(siblings
                .Where(macro => !ReferenceEquals(macro, _existing))
                .Select(macro => macro.Callable))
            .Concat(_model.SubMacros.Select(macro => macro.Callable))]);
    }

    public static HeadlessEditorSession OpenSubmacro(ProjectWorkspace workspace,
        HeadlessEditorSession owner, string name, IDeviceLayer? devices = null,
        IEditorSettings? settings = null)
    {
        var submacro = owner._model.SubMacros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("the macro has no sub-macro named " + name);
        return new HeadlessEditorSession(workspace, submacro, owner, submacro.Name,
            devices ?? owner._devices, settings ?? owner._settings);
    }

    public static HeadlessEditorSession Open(ProjectWorkspace workspace, string? name,
        IDeviceLayer? devices = null, IEditorSettings? settings = null)
    {
        if (name is not { Length: > 0 })
        {
            return new HeadlessEditorSession(workspace, null, devices: devices, settings: settings);
        }

        var existing = workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            throw new InvalidOperationException("the project has no macro named " + name);
        }

        return new HeadlessEditorSession(workspace, existing, devices: devices, settings: settings);
    }

    public string Name => _model.Name;

    public string Description => _model.Description;

    public string Group => _model.Group;

    /// <summary>The project macro this document was opened from or last saved to, if any.</summary>
    internal MacroItem? Target => _existing;

    /// <summary>
    /// The name the document was opened under or last bound to. A document whose project target
    /// was replaced by another instance is still found by this name, so opening the same macro
    /// again activates the draft instead of creating a second tab.
    /// </summary>
    internal string OriginalName => _originalName;

    /// <summary>Whether this document stands for the given project macro, by instance or by name.</summary>
    internal bool Matches(MacroItem macro)
        => ReferenceEquals(_existing, macro)
           || (_existing is not null
               && string.Equals(_existing.Name.Trim(), macro.Name.Trim(),
                   StringComparison.OrdinalIgnoreCase))
           || (_existing is null
               && _originalName.Length > 0
               && string.Equals(_originalName, macro.Name.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool IsDirty => _model.IsDirty;

    /// <summary>
    /// Ends the transient work a page owns without saving or closing the document: an open action
    /// form, an open value field, and a recorder that is still writing into the draft.
    /// </summary>
    internal void Deactivate()
    {
        CloseValueEditor();
        CancelActionDraft();
        _model.StopRecording();
    }

    public bool CanSave => _model.CanSave;

    public bool CanUndo => _model.CanUndo;

    public bool CanRedo => _model.CanRedo;

    /// <summary>True for a submacro document: it has no triggers and no input limit of its own.</summary>
    public bool IsSubMacro => _model.IsSubMacro;

    /// <summary>What the tab strip shows: a document that cannot be saved right now has a problem.</summary>
    public bool HasProblem => !CanSave;

    public string? NameProblem => _model.NameProblem;

    public string ProblemSummary => _model.ProblemSummary;

    public string DeclarationProblemSummary => _model.DeclarationProblemSummary;

    public string CallImpactSummary => _model.CallImpactSummary;

    public string Notice => _model.Notice;

    public string TriggerProblemSummary => _model.TriggerProblemSummary;

    public string ToggleKeyProblem => _model.ToggleKeyProblem;

    public IReadOnlyList<string> SubMacros
        => [.. _model.SubMacros.Select(macro => macro.Name)];

    public string SaveProblem => _model.NameProblem is { Length: > 0 } nameProblem
        ? nameProblem
        : _model.DeclarationProblemSummary is { Length: > 0 } declarationProblem
            ? declarationProblem
            : string.Empty;

    public IReadOnlyList<string> StepTypes
        => [.. _model.Draft().Macro.Steps.Select(step => step.Type)];

    public static IReadOnlyList<EditorTriggerKindSnapshot> TriggerKindOptions
        => [.. TriggerKinds.Editable.Select(kind => new EditorTriggerKindSnapshot(
            kind.ToString(), TriggerKinds.Text(kind)))];

    public static IReadOnlyList<EditorVariableTypeSnapshot> VariableTypes
        => [.. WhaleGenie.Models.VariableTypes.Declarable.Select(kind => new EditorVariableTypeSnapshot(
            WhaleGenie.Models.VariableTypes.Name(kind),
            Strings.Get($"Variable.Type.{WhaleGenie.Models.VariableTypes.Name(kind)}",
                WhaleGenie.Models.VariableTypes.Name(kind))))];

    public static IReadOnlyList<EditorConditionCompareSnapshot> ConditionCompares
        => [.. Condition.CompareChoices.Select(choice => new EditorConditionCompareSnapshot(
            choice.Compare.ToString(), choice.Label))];

    public static IReadOnlyList<EditorValueFunctionSnapshot> ValueFunctions
        => [.. Expression.Functions.Select(function => new EditorValueFunctionSnapshot(
            function.Name,
            function.Signature,
            function.Description,
            Strings.Get("Expr.Group." + function.Group, function.Group)))];

}
