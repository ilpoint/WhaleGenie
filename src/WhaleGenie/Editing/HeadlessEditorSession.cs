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
    private readonly HeadlessEditorSession? _owner;
    private readonly string _submacroOriginal = string.Empty;
    private readonly Dictionary<StepParameter, ConditionDraft> _conditionDrafts = [];
    private List<GlobalDraftEntry>? _globalDraft;
    private readonly IDeviceLayer? _devices;
    private HeadlessValueSession? _valueSession;
    private MacroItem? _existing;
    private HeadlessActionSession? _actionSession;
    private ActionTarget? _actionTarget;
    private int _revision;

    private sealed record ActionTarget(string ParentId, string ListName, string TargetStepId, bool Before);


    private HeadlessEditorSession(ProjectWorkspace workspace, MacroItem? existing,
        HeadlessEditorSession? owner = null, string submacroOriginal = "",
        IDeviceLayer? devices = null)
    {
        _workspace = workspace;
        _existing = existing;
        _owner = owner;
        _devices = devices;
        _submacroOriginal = submacroOriginal;
        _model = new MacroEditorViewModel
        {
            Variables = workspace.Variables,
            Persisted = workspace.Persisted,
        };
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
        _model.MacroLifted += lifted => _workspace.AddMacro(lifted);
    }

    public static HeadlessEditorSession OpenSubmacro(ProjectWorkspace workspace,
        HeadlessEditorSession owner, string name, IDeviceLayer? devices = null)
    {
        var submacro = owner._model.SubMacros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("the macro has no sub-macro named " + name);
        return new HeadlessEditorSession(workspace, submacro, owner, submacro.Name,
            devices ?? owner._devices);
    }

    public static HeadlessEditorSession Open(ProjectWorkspace workspace, string? name,
        IDeviceLayer? devices = null)
    {
        if (name is not { Length: > 0 })
        {
            return new HeadlessEditorSession(workspace, null, devices: devices);
        }

        var existing = workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            throw new InvalidOperationException("the project has no macro named " + name);
        }

        return new HeadlessEditorSession(workspace, existing, devices: devices);
    }

    public string Name => _model.Name;

    public string Description => _model.Description;

    public string Group => _model.Group;

    public bool IsDirty => _model.IsDirty;

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

    public IReadOnlyList<EditorMacroParameterSnapshot> CallableParameters
        => [.. _model.DeclaredParameters.Select(parameter => new EditorMacroParameterSnapshot(
            parameter.Name,
            WhaleGenie.Models.VariableTypes.Name(parameter.Type),
            parameter.Required,
            Formula.Word(parameter.Default),
            parameter.Description,
            FieldSnapshots(parameter.Fields)))];

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

    public void PatchCallableParameter(int index, string field, string value)
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
                parameter.Default = Formula.Text(value);
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

    public bool FollowInputLimit => _model.FollowProgramInputRate;

    public int InputLimit => (int)Math.Round(_model.InputLimitValue);

    public string ToggleKey => _model.ToggleKey;

    public void SetMacroSetting(string field, string value)
    {
        switch (field)
        {
            case "followInputLimit":
                _model.FollowProgramInputRate = bool.TryParse(value, out var follow) && follow;
                break;
            case "inputLimit":
                _model.FollowProgramInputRate = false;
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var limit))
                {
                    _model.InputLimitValue = Math.Clamp(limit, 0, 999);
                }

                break;
            case "toggleKey":
                _model.ToggleKey = value.Trim();
                break;
            default:
                throw new InvalidOperationException("unknown macro setting " + field);
        }

        _model.IsDirty = true;
    }

    private void ReplaceSubmacro(string originalName, MacroItem updated)
    {
        var index = _model.SubMacros.FindIndex(macro =>
            string.Equals(macro.Name, originalName, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            _model.SubMacros.Add(updated);
        }
        else
        {
            _model.SubMacros[index] = updated;
        }

        if (_existing is not null)
        {
            var existingIndex = _existing.SubMacros.FindIndex(macro =>
                string.Equals(macro.Name, originalName, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                _existing.SubMacros[existingIndex] = updated;
            }
        }

        _model.IsDirty = true;
    }

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
            entry.Spec is KeySpec key ? key.Binding : string.Empty,
            entry.Spec is KeySpec pressed ? pressed.Loop.ToString()
                : entry.Spec is ColorSpec colourLoop ? colourLoop.Loop.ToString() : string.Empty,
            WhaleGenie.Models.TriggerKinds.RunsOnce(entry.Spec),
            entry.Spec is TimerSpec timer ? timer.Mode.ToString() : string.Empty,
            entry.Spec is TimerSpec timed ? timed.Time : string.Empty,
            entry.Spec is TimerSpec interval
                ? interval.Interval.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is TimerSpec unit ? unit.Unit.ToString() : string.Empty,
            entry.Spec is FileSpec file ? file.Path : string.Empty,
            entry.Spec is FileSpec change ? change.Change.ToString() : string.Empty,
            entry.Spec is FileSpec filter ? filter.Filter : string.Empty,
            entry.Spec is FileSpec folders && folders.Subfolders,
            entry.Spec is ProcessSpec process ? process.Name : string.Empty,
            entry.Spec is ProcessSpec processChange ? processChange.Change.ToString() : string.Empty,
            entry.Spec is HotstringSpec hotstring ? hotstring.Abbreviation : string.Empty,
            entry.Spec is ColorSpec colour ? colour.Match.ToString() : string.Empty,
            entry.Spec is ColorSpec hex ? hex.Hex : string.Empty,
            entry.Spec is ColorSpec tolerance
                ? tolerance.Tolerance.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is ColorSpec point
                ? point.X.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is ColorSpec pointY
                ? pointY.Y.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is ColorSpec follow && follow.FollowCursor,
            entry.Spec is IdleSpec idle
                ? idle.Seconds.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is WindowSpec window ? window.Target.Title : string.Empty,
            entry.Spec is WindowSpec processWindow ? processWindow.Target.Process : string.Empty,
            entry.Spec is WindowSpec classWindow ? classWindow.Target.ClassName
                : entry.Spec is InterfaceSpec interfaceClass ? interfaceClass.Window.ClassName
                : string.Empty,
            entry.Spec is WindowSpec howWindow ? howWindow.Target.How.ToString() : string.Empty,
            entry.Spec is WindowSpec changeWindow ? changeWindow.Change.ToString() : string.Empty,
            entry.Spec is InterfaceSpec face ? face.Window.Title : string.Empty,
            entry.Spec is InterfaceSpec faceProcess ? faceProcess.Window.Process : string.Empty,
            entry.Spec is InterfaceSpec faceHow ? faceHow.Window.How.ToString() : string.Empty,
            entry.Spec is WindowSpec caseWindow && caseWindow.Target.CaseSensitive,
            entry.Spec is WindowSpec trimWindow && trimWindow.Target.Trim,
            entry.Spec is WindowSpec multiWindow && multiWindow.Target.Multiline,
            entry.Spec is HotstringSpec ends ? ends.EndChars : string.Empty,
            entry.Spec is HotstringSpec casing && casing.CaseSensitive,
            entry.Spec is HotstringSpec inside && inside.InsideWord,
            entry.Spec is HotstringSpec requiring && requiring.RequireEndChar,
            entry.Spec is HotstringSpec erasing && erasing.EraseTyped,
            entry.Spec is HotstringSpec keeping && keeping.KeepEndChar,
            HotstringCharacters.MaximumLength,
            _model.SelectedTrigger?.Id == entry.Id
                ? [.. _model.HotstringEndKeys.Select(option => new EditorHotstringEndKeySnapshot(
                    option.Value, option.Label, option.IsChecked, option.IsCustom))]
                : [],
            _model.SelectedTrigger?.Id == entry.Id ? _model.HotstringEndCaptureStatus : string.Empty,
            entry.Spec is InterfaceSpec && _model.SelectedTrigger?.Id == entry.Id
                ? _model.InterfaceToggleHotkey
                : string.Empty,
            _model.SelectedTrigger?.Id == entry.Id ? _model.InterfaceHotkeyProblem : string.Empty,
            _model.SelectedTrigger?.Id == entry.Id ? [.. _model.InterfaceGroupMembers] : []))];

    public EditorRecordingSnapshot Recording => new(
        _model.PositionCaptureIndex,
        _model.PositionCaptureOptions,
        _model.RecordMouseMovement,
        _model.RecordMouseButtons,
        _model.RecordKeyboardKeys,
        _model.RecordGamepadButtons,
        _model.RecordDelays,
        (int)(_model.IgnoreDelaysBelowMs ?? 0),
        _model.RecordIgnoreInjected,
        _model.DelayScale,
        _model.DelayScaleDisplay,
        _model.HasDelayScale);

    public void SetRecordOption(string field, string value)
    {
        switch (field)
        {
            case "positionCapture":
                _model.PositionCaptureIndex = int.TryParse(value, out var index) ? index : 0;
                break;
            case "mouseMovement":
                _model.RecordMouseMovement = Flag(value);
                break;
            case "mouseButtons":
                _model.RecordMouseButtons = Flag(value);
                break;
            case "keyboardKeys":
                _model.RecordKeyboardKeys = Flag(value);
                break;
            case "gamepadButtons":
                _model.RecordGamepadButtons = Flag(value);
                break;
            case "delays":
                _model.RecordDelays = Flag(value);
                break;
            case "ignoreDelaysBelowMs":
                _model.IgnoreDelaysBelowMs = decimal.TryParse(value,
                    System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var milliseconds) ? milliseconds : 0;
                break;
            case "ignoreInjected":
                _model.RecordIgnoreInjected = Flag(value);
                break;
            default:
                throw new InvalidOperationException("unknown recording option " + field);
        }

        _model.IsDirty = true;
    }

    public void SetDelayScale(double factor) => _model.SetDelayScale(factor);

    public bool RecordIgnoreInjected => _model.RecordIgnoreInjected;

    public bool RecordGamepadButtons => _model.RecordGamepadButtons;

    public RecordingOptions RecordingChoices() => new()
    {
        MouseMovement = _model.RecordMouseMovement,
        MouseButtons = _model.RecordMouseButtons,
        KeyboardKeys = _model.RecordKeyboardKeys,
        GamepadButtons = _model.RecordGamepadButtons,
        Delays = _model.RecordDelays,
        IgnoreDelaysBelowMs = (int)(_model.IgnoreDelaysBelowMs ?? 0),
        RelativeMovement = _model.PositionCapture is MousePositionMode.SavePositionDifferences,
    };

    private static bool Flag(string value)
        => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

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

    public IReadOnlyList<EditorValueChoiceSnapshot> ValueChoices
        => [.. _model.CollectVariables().Select(choice => new EditorValueChoiceSnapshot(
            choice.Name,
            choice.Type is { } type ? WhaleGenie.Models.VariableTypes.Name(type) : "text",
            choice.Origin.ToString(),
            choice.Source,
            choice.Description,
            choice.Group.ToString(),
            choice.Display,
            choice.IsPersistent))];

    public IReadOnlyList<EditorGlobalSnapshot> Globals
        => [.. (_globalDraft?.Select(entry => entry.Variable) ?? _workspace.Variables.Globals)
            .Select(variable => new EditorGlobalSnapshot(
                variable.Name,
                WhaleGenie.Models.VariableTypes.Name(variable.Type),
                Formula.Word(variable.Default),
                variable.Description,
                variable.PersistValue))];

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

    public void Undo() => _model.UndoCommand.Execute(null);

    public void Redo() => _model.RedoCommand.Execute(null);

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

    public void AddTrigger(string kind)
    {
        if (!Enum.TryParse<TriggerKind>(kind, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException("unknown trigger kind " + kind);
        }

        _model.AddTriggerCommand.Execute(parsed);
        _model.IsDirty = true;
    }

    public void AddKeyTrigger() => AddTrigger(nameof(TriggerKind.Key));

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
        var trigger = KeyTrigger(triggerId);
        _model.BindKey = binding.Trim();
        _model.IsDirty = true;
    }

    public void SetTriggerLoop(string triggerId, string loop)
    {
        var trigger = AnyTrigger(triggerId);
        if (trigger.Spec is not (KeySpec or ColorSpec))
        {
            throw new InvalidOperationException("trigger " + triggerId + " has no loop mode");
        }

        if (!Enum.TryParse<MacroLoop>(loop, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException("unknown trigger loop " + loop);
        }

        _model.SelectedTrigger = trigger;
        _model.LoopMode = parsed;
        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

    public void SetTriggerInterface(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not InterfaceSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not an interface trigger");
        }

        _model.SelectedTrigger = trigger;
        _model.WindowTarget = field switch
        {
            "title" => _model.WindowTarget with { Title = value },
            "process" => _model.WindowTarget with { Process = value },
            "class" => _model.WindowTarget with { ClassName = value },
            "how" => _model.WindowTarget with { How = value },
            _ => throw new InvalidOperationException("unknown interface field " + field),
        };
        _model.IsDirty = true;
    }

    public void SetTriggerWindow(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not WindowSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a window trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "title":
                _model.WindowTarget = _model.WindowTarget with { Title = value };
                break;
            case "process":
                _model.WindowTarget = _model.WindowTarget with { Process = value };
                break;
            case "class":
                _model.WindowTarget = _model.WindowTarget with { ClassName = value };
                break;
            case "how":
                _model.WindowTarget = _model.WindowTarget with { How = value };
                break;
            case "change":
                _model.WindowChange = Enum.Parse<WindowChangeKind>(value, ignoreCase: true);
                break;
            default:
                throw new InvalidOperationException("unknown window field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerColor(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not ColorSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a colour trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "match":
                _model.ColorMatch = Enum.Parse<ColorMatchCondition>(value, ignoreCase: true);
                break;
            case "hex":
                _model.HexColor = value.Trim();
                break;
            case "tolerance":
                _model.ColorTolerance = value.Trim();
                break;
            case "x":
                _model.ColorPositionX = value.Trim();
                break;
            case "y":
                _model.ColorPositionY = value.Trim();
                break;
            case "follow":
                _model.UseCursorPosition = bool.TryParse(value, out var follow) && follow;
                break;
            default:
                throw new InvalidOperationException("unknown colour field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerIdle(string triggerId, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not IdleSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not an idle trigger");
        }

        _model.SelectedTrigger = trigger;
        _model.IdleSeconds = int.TryParse(value, out var seconds) ? Math.Max(1, seconds) : 60;
        _model.IsDirty = true;
    }

    public void SetTriggerProcess(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not ProcessSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a process trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "name":
                _model.ProcessName = value.Trim();
                break;
            case "change":
                _model.ProcessChange = Enum.Parse<ProcessChangeKind>(value, ignoreCase: true);
                break;
            default:
                throw new InvalidOperationException("unknown process field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerHotstring(string triggerId, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not HotstringSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a hotstring trigger");
        }

        _model.SelectedTrigger = trigger;
        _model.HotstringText = value.Length > HotstringCharacters.MaximumLength
            ? value[..HotstringCharacters.MaximumLength]
            : value;
        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

    public void SetTriggerHotstringEndKey(string triggerId, string value, bool checkedState)
    {
        var trigger = AnyTrigger(triggerId);
        _model.SelectedTrigger = trigger;
        var option = _model.HotstringEndKeys.FirstOrDefault(candidate =>
            string.Equals(candidate.Value, value, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the hotstring has no end key " + value);
        option.IsChecked = checkedState;
        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

    public void ResetTriggerHotstringEndKeys(string triggerId)
    {
        var trigger = AnyTrigger(triggerId);
        _model.SelectedTrigger = trigger;
        _model.HotstringEndChars = HotstringCharacters.DefaultEndChars;
        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

    public void CaptureTriggerHotstringEnd(string triggerId, string character)
    {
        var trigger = AnyTrigger(triggerId);
        _model.SelectedTrigger = trigger;
        _model.IsCapturingHotstringEnd = true;
        _model.CaptureHotstringEnd(character);
        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

    public void SetTriggerInterfaceHotkey(string triggerId, string binding)
    {
        var trigger = AnyTrigger(triggerId);
        if (trigger.Spec is not InterfaceSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not an interface trigger");
        }

        _model.SelectedTrigger = trigger;
        if (_model.SetInterfaceToggleHotkey(binding))
        {
            _model.IsDirty = true;
        }
    }

    public void ClearTriggerInterfaceHotkey(string triggerId)
    {
        var trigger = AnyTrigger(triggerId);
        _model.SelectedTrigger = trigger;
        _model.ClearInterfaceToggleHotkey();
        _model.IsDirty = true;
    }

    public void SetTriggerFile(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not FileSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a file trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "path":
                _model.WatchPath = value;
                break;
            case "change":
                _model.WatchChange = Enum.Parse<FileChangeKind>(value, ignoreCase: true);
                break;
            case "filter":
                _model.WatchFilter = value;
                break;
            case "subfolders":
                _model.WatchSubfolders = bool.TryParse(value, out var subfolders) && subfolders;
                break;
            default:
                throw new InvalidOperationException("unknown file field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerTimer(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not TimerSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a timer trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "mode":
                _model.ScheduleMode = Enum.Parse<ScheduleMode>(value, ignoreCase: true);
                break;
            case "time":
                _model.ScheduleTime = value.Trim();
                break;
            case "interval":
                _model.ScheduleInterval = int.TryParse(value, out var interval) ? interval : 1;
                break;
            case "unit":
                _model.ScheduleUnit = Enum.Parse<ScheduleUnit>(value, ignoreCase: true);
                break;
            default:
                throw new InvalidOperationException("unknown timer field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerEnabled(string triggerId, bool enabled)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        trigger.IsEnabled = enabled;
        _model.IsDirty = true;
    }

    public void SetTriggerOnce(string triggerId, bool once)
    {
        var trigger = AnyTrigger(triggerId);
        _model.SelectedTrigger = trigger;
        _model.TriggerOnce = once;
        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

    public void SetTriggerHotstringOption(string triggerId, string field, string value)
    {
        var trigger = AnyTrigger(triggerId);
        if (trigger.Spec is not HotstringSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a hotstring trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "endChars":
                _model.HotstringEndChars = value;
                break;
            case "caseSensitive":
                _model.HotstringCaseSensitive = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case "insideWord":
                _model.HotstringInsideWord = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case "requireEnd":
                _model.HotstringRequireEnd = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case "eraseTyped":
                _model.HotstringEraseTyped = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case "keepEnd":
                _model.HotstringKeepEnd = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            default:
                throw new InvalidOperationException("unknown hotstring option " + field);
        }

        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

    private TriggerEntry AnyTrigger(string triggerId)
        => _model.TriggerEntries.FirstOrDefault(entry =>
               string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
           ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);

    private TriggerEntry KeyTrigger(string triggerId)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not KeySpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a key trigger");
        }

        // The drawer's fields are what BuildMacro commits; move the selection onto this entry and
        // write the value there rather than changing a spec the next commit would overwrite.
        _model.SelectedTrigger = trigger;
        return trigger;
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

    public void AddConditionRow(string stepId, string name, int[]? path = null)
    {
        if (_actionSession is { } action)
        {
            action.AddConditionRow(name, path ?? []);
            return;
        }

        ChangeCondition(stepId, name, root =>
            GroupAt(root, path ?? []).Children.Add(ConditionDraftNode.Row()));
    }

    public void AddConditionGroup(string stepId, string name, int[]? path = null)
    {
        if (_actionSession is { } action)
        {
            action.AddConditionGroup(name, path ?? []);
            return;
        }

        ChangeCondition(stepId, name, root =>
            GroupAt(root, path ?? []).Children.Add(ConditionDraftNode.Group(Join.All)));
    }

    public void RemoveConditionNode(string stepId, string name, int[] path)
    {
        if (_actionSession is { } action)
        {
            action.RemoveConditionNode(name, path);
            return;
        }

        ChangeCondition(stepId, name, root =>
        {
            if (path.Length == 0)
            {
                throw new InvalidOperationException("the root condition cannot be removed");
            }

            var parent = GroupAt(root, path[..^1]);
            if (path[^1] < 0 || path[^1] >= parent.Children.Count)
            {
                throw new InvalidOperationException("the condition has no node " + path[^1]);
            }

            parent.Children.RemoveAt(path[^1]);
        });
    }

    public void RemoveConditionRow(string stepId, string name, int index)
        => RemoveConditionNode(stepId, name, [index]);

    public void SetConditionRow(string stepId, string name, int[] path, string field, string value)
    {
        if (_actionSession is { } action)
        {
            action.SetConditionRow(name, path, field, value);
            return;
        }

        ChangeCondition(stepId, name, root =>
        {
            if (path.Length == 0)
            {
                throw new InvalidOperationException("the root condition is a group");
            }

            var parent = GroupAt(root, path[..^1]);
            if (path[^1] < 0 || path[^1] >= parent.Children.Count
                || parent.Children[path[^1]] is not { IsRow: true } row)
            {
                throw new InvalidOperationException("the condition has no row at " + string.Join('.', path));
            }

            switch (field)
            {
                case "left":
                    row.Left = ConditionValue(value);
                    break;
                case "right":
                    row.Right = ConditionValue(value);
                    break;
                case "compare":
                    row.Compare = Enum.Parse<ConditionCompare>(value, ignoreCase: true);
                    break;
                default:
                    throw new InvalidOperationException("unknown condition field " + field);
            }
        });
    }

    public void SetConditionRow(string stepId, string name, int index, string field, string value)
        => SetConditionRow(stepId, name, [index], field, value);

    public void SetConditionJoin(string stepId, string name, string value, int[]? path = null)
    {
        if (_actionSession is { } action)
        {
            action.SetConditionJoin(name, path ?? [], value);
            return;
        }

        ChangeCondition(stepId, name, root =>
        {
            var group = GroupAt(root, path ?? []);
            group.Join = Enum.Parse<Join>(value, ignoreCase: true);
        });
    }

    private static ExpressionNode ConditionValue(string value)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            return Formula.Nothing;
        }

        if (text[0] == '$')
        {
            return Formula.Name(text[1..]);
        }

        if (bool.TryParse(text, out var flag))
        {
            return Formula.Constant(flag ? "true" : "false");
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? Formula.Number(text, number)
            : Formula.Text(value);
    }

    private static ConditionDraftNode GroupAt(ConditionDraftNode root, int[] path)
    {
        var group = root;
        foreach (var index in path)
        {
            if (index < 0 || index >= group.Children.Count
                || !group.Children[index].IsGroup)
            {
                throw new InvalidOperationException("the condition has no group at " + string.Join('.', path));
            }

            group = group.Children[index];
        }

        return group;
    }

    private void ChangeCondition(string stepId, string name, Action<ConditionDraftNode> change)
    {
        var parameter = FindParameter(stepId, name);
        var draft = EnsureConditionDraft(parameter);
        change(draft.Root);
        _conditionDrafts[parameter] = draft;
        parameter.Written = Condition.ToNode(draft.Root.ToShape()) ?? Formula.Nothing;
        _model.IsDirty = true;
    }

    private ConditionDraft EnsureConditionDraft(StepParameter parameter)
    {
        if (_conditionDrafts.TryGetValue(parameter, out var draft))
        {
            return draft;
        }

        if (!Condition.TryRead(parameter.Written, out var shape))
        {
            throw new InvalidOperationException("the condition cannot be edited as rows");
        }

        draft = new ConditionDraft(ConditionDraftNode.Root(shape));
        _conditionDrafts[parameter] = draft;
        return draft;
    }

    private static ConditionDraftNode ConditionRowAt(ConditionDraftNode root, int[] path)
    {
        if (path.Length == 0)
        {
            throw new InvalidOperationException("the root condition is a group");
        }

        var parent = GroupAt(root, path[..^1]);
        if (path[^1] < 0 || path[^1] >= parent.Children.Count
            || parent.Children[path[^1]] is not { IsRow: true } row)
        {
            throw new InvalidOperationException("the condition has no row at " + string.Join('.', path));
        }

        return row;
    }

    private sealed class ConditionDraft(ConditionDraftNode root)
    {
        public ConditionDraftNode Root { get; } = root;
    }

    private sealed class ConditionDraftNode
    {
        public bool IsGroup { get; private init; }

        public bool IsRow => !IsGroup;

        public Join Join { get; set; }

        public List<ConditionDraftNode> Children { get; init; } = [];

        public ExpressionNode Left { get; set; } = Formula.Nothing;

        public ConditionCompare Compare { get; set; } = ConditionCompare.Equal;

        public ExpressionNode Right { get; set; } = Formula.Nothing;

        public static ConditionDraftNode Row() => new() { IsGroup = false };

        public static ConditionDraftNode Group(Join join) => new() { IsGroup = true, Join = join };

        public static ConditionDraftNode Root(ConditionShape shape)
        {
            if (shape is ConditionShape.Group group)
            {
                return FromShape(group);
            }

            var root = Group(Join.All);
            root.Children.Add(FromShape(shape));
            return root;
        }

        public static ConditionDraftNode FromShape(ConditionShape shape)
            => shape switch
            {
                ConditionShape.Row row => new ConditionDraftNode
                {
                    IsGroup = false,
                    Left = row.Left,
                    Compare = row.Compare,
                    Right = row.Right,
                },
                ConditionShape.Group group => new ConditionDraftNode
                {
                    IsGroup = true,
                    Join = group.Join,
                    Children = [.. group.Children.Select(FromShape)],
                },
                _ => Row(),
            };

        public ConditionShape ToShape()
            => IsGroup
                ? new ConditionShape.Group(Join, [.. Children.Select(child => child.ToShape())])
                : new ConditionShape.Row(Left, Compare, Right);
    }

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

        parameter.Rows[index].SetLine(column, value);
        _model.IsDirty = true;
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

    /// <summary>Writes the draft through the same rule the native editor and the MCP save use.</summary>
    /// <summary>
    /// Opens the restored token picker on one scalar parameter of a step. The field is rebuilt from
    /// the catalogue definition and the stored parameter, exactly as the old action editor did, and
    /// the confirmed shape replaces that one parameter on the step.
    /// </summary>
    public EditorActionFormSnapshot OpenActionDraft(string type, string parentId = "", string listName = "",
        string targetStepId = "", bool before = false)
    {
        var definition = ActionCatalog.Find(type)
            ?? throw new InvalidOperationException("unknown action " + type);
        CloseValueEditor();
        _actionTarget = new ActionTarget(parentId, listName, targetStepId, before);
        PrepareTarget();
        _actionSession = CreateActionSession(definition, null);
        return _actionSession.Snapshot();
    }

    public EditorActionFormSnapshot OpenStepDraft(string stepId)
    {
        var found = FindTreeStep(stepId)
            ?? throw new InvalidOperationException("the macro has no step " + stepId);
        CloseValueEditor();
        _actionTarget = null;
        var definition = ActionCatalog.Find(found.Type)
            ?? throw new InvalidOperationException("unknown action " + found.Type);
        _actionSession = CreateActionSession(definition, found);
        return _actionSession.Snapshot();
    }

    public EditorActionFormSnapshot ActionFormState()
        => _actionSession?.Snapshot() ?? EditorActionSnapshotFactory.Closed();

    public HeadlessActionSession ActionForm
        => _actionSession ?? throw new InvalidOperationException("no action is being edited");

    public EditorSnapshot ConfirmActionDraft()
    {
        var session = _actionSession
            ?? throw new InvalidOperationException("no action is being edited");
        if (!session.Form.CanSave)
        {
            return Snapshot();
        }

        var step = session.BuildStepForSave();
        if (session.IsEditing)
        {
            var original = FindTreeStep(session.Original!.Id)
                ?? throw new InvalidOperationException("the edited step is no longer in the macro");
            _model.ReplaceStep(original, step);
        }
        else
        {
            PrepareTarget();
            _model.AddStep(step);
        }

        _actionSession = null;
        _actionTarget = null;
        return Snapshot();
    }

    public EditorSnapshot CancelActionDraft()
    {
        _actionSession = null;
        _actionTarget = null;
        _valueSession = null;
        return Snapshot();
    }

    private HeadlessActionSession CreateActionSession(ActionDefinition definition, MacroStep? original)
    {
        var actions = ActionCatalog.PlaceableActions;
        if (actions.All(action => action.Key != definition.Key))
        {
            actions = [.. actions, definition];
        }

        var macros = new List<MacroCallable>(_workspace.Macros.Select(macro => macro.Callable));
        macros.AddRange(_model.SubMacros.Select(macro => macro.Callable));
        var above = WindowRuleChoices.NearestAbove(_model.Steps, _actionTarget?.TargetStepId);
        var form = new AddActionViewModel(actions, _model.CollectVariables(), macros,
            _model.StepChoices(), original is null ? StepIds.Next(CollectStepIds()) : null,
            WindowRuleChoices.Of(_model.Steps), above.Length > 0, above,
            _model.LoopNamesInScope())
        {
            Variables = _workspace.Variables,
            Persisted = _workspace.Persisted,
            Windows = _devices?.Windows,
            Ui = _devices?.Ui,
            Clipboard = _devices?.Clipboard,
            AssetFolder = _workspace.CurrentPath is { Length: > 0 } current
                ? MacroPackage.AssetFolderFor(current)
                : ImageAssets.DefaultFolder(),
            NextStepId = StepIds.Next,
        };

        if (original is null)
        {
            form.SelectAction(definition.Key);
        }
        else
        {
            form.LoadFrom(original);
        }

        return new HeadlessActionSession(form, original, new TrialHost());
    }

    private void PrepareTarget()
    {
        if (_actionTarget is not { } target)
        {
            _model.PrepareInsertionAt(_model.Steps, _model.Steps.Count);
            return;
        }

        if (target.TargetStepId is { Length: > 0 } targetId)
        {
            if (Find(targetId) is { } found)
            {
                _model.PrepareInsertionAt(found.List, found.Index + (target.Before ? 0 : 1));
                return;
            }

            throw new InvalidOperationException("the insertion target is no longer in the macro");
        }

        if (target.ParentId is { Length: > 0 } parentId)
        {
            var parent = FindTreeStep(parentId)
                ?? throw new InvalidOperationException("the parent step is no longer in the macro");
            var list = parent.Parameters.FirstOrDefault(parameter =>
                    parameter.Kind == ActionParameterKind.Steps
                    && (target.ListName is not { Length: > 0 }
                        || string.Equals(parameter.Name, target.ListName, StringComparison.Ordinal)))?.Steps
                ?? throw new InvalidOperationException("the parent has no child list " + target.ListName);
            _model.PrepareInsertionAt(list, list.Count);
            return;
        }

        _model.PrepareInsertionAt(_model.Steps, _model.Steps.Count);
    }

    private HashSet<string> CollectStepIds()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (list, index) in Walk(_model.Steps))
        {
            ids.Add(list[index].Id);
        }

        return ids;
    }

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

    /// <summary>Writes the page in front to a package of its own, the way the native export did.</summary>
    public static IReadOnlyList<EditorPublicMacroSnapshot> PublicMacros()
    {
        try
        {
            return [.. PublicMacroLibrary.Load(AppPaths.PublicMacroLibrary).Select(entry =>
                new EditorPublicMacroSnapshot(entry.Id, entry.Version,
                    entry.Macro.Name, entry.Macro.Description))];
        }
        catch (Exception)
        {
            return [];
        }
    }

    public EditorSnapshot AddPublicMacro(string id)
    {
        var entry = PublicMacroLibrary.Load(AppPaths.PublicMacroLibrary)
            .FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the public library has no macro " + id);
        var macro = MacroItem.FromJson(entry.Macro.ToSubMacroJson());
        var taken = _model.SubMacros.Select(candidate => candidate.Name)
            .Concat(_workspace.Macros.Select(candidate => candidate.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stem = macro.Name.Trim();
        var name = stem;
        for (var index = 2; taken.Contains(name); index++)
        {
            name = stem + " " + index.ToString(CultureInfo.InvariantCulture);
        }

        macro.Name = name;
        _model.SubMacros.Add(macro);

        var call = new MacroStep
        {
            Type = "control.runMacro",
            Meta = StepMeta.New,
            Parameters =
            [
                new StepParameter
                {
                    Name = "macro",
                    Kind = ActionParameterKind.Macro,
                    Written = Formula.Text(name),
                },
            ],
        };

        if (macro.Parameters.Count > 0)
        {
            call.Parameters.Add(new StepParameter
            {
                Name = FieldNames.Arguments,
                Kind = ActionParameterKind.Arguments,
                Rows =
                [
                    .. macro.Parameters.Select(parameter => new StepParameterRow
                    {
                        Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
                        {
                            [FieldNames.Name] = Formula.Text(parameter.Name),
                            [FieldNames.Value] = Formula.Text(string.Empty),
                        },
                    }),
                ],
            });
        }

        if (macro.Results.Count > 0)
        {
            call.Parameters.Add(new StepParameter
            {
                Name = "returns",
                Kind = ActionParameterKind.Returns,
                Rows =
                [
                    .. macro.Results.Select(result => new StepParameterRow
                    {
                        Cells = new Dictionary<string, ExpressionNode>(StringComparer.OrdinalIgnoreCase)
                        {
                            [FieldNames.Key] = Formula.Text(result.Name),
                            [FieldNames.Name] = Formula.Text(result.Name),
                        },
                    }),
                ],
            });
        }

        _model.Steps.Add(call);
        _model.SettleStepIds();
        _model.IsDirty = true;
        return Snapshot();
    }

    public bool Export(string path)
    {
        if (path.Trim().Length == 0)
        {
            throw new InvalidOperationException("an export needs a file name");
        }

        var draft = _model.IsSubMacro
            ? new MacroDraft(_model.BuildMacro(), [], [])
            : _model.Draft();
        var triggers = new ProjectTriggers();
        foreach (var entry in draft.Entries)
        {
            triggers.Entries.Add(entry.Copy());
        }

        foreach (var layout in draft.Layouts)
        {
            triggers.InterfaceLayouts.Add(layout);
        }

        var assets = _workspace.CurrentPath is { Length: > 0 } current
            ? MacroPackage.AssetFolderFor(current)
            : null;
        MacroPackage.Save(path, [draft.Macro],
            MacroBundle.Take(triggers, [draft.Macro]),
            MacroBundle.UsedGlobals([draft.Macro], _workspace.Variables.Globals),
            AppVersion.Text, assets);
        return true;
    }

    public bool Save()
    {
        if (!CanSave)
        {
            throw new InvalidOperationException(SaveProblem is { Length: > 0 } problem
                ? problem
                : Strings.Get("Editor.Problem.CannotSave"));
        }

        var draft = _model.Draft();
        if (_owner is not null)
        {
            _owner.ReplaceSubmacro(_submacroOriginal, draft.Macro);
            _model.IsDirty = false;
            _existing = draft.Macro;
            return true;
        }

        var commit = new ProjectMacroCommit(_workspace, _existing, MacroView.All,
            _ => { }, () => { }, () => { });
        commit.Apply(draft, []);
        CommitGlobals();
        _model.IsDirty = false;
        _existing = _workspace.Macros.FirstOrDefault(candidate => candidate.Name == draft.Macro.Name);
        return _existing is not null;
    }

    /// <summary>What the page will draw: the macro's fields and its step tree as the engine reads it.</summary>
    public EditorSnapshot Snapshot()
    {
        _model.SettleStepIds();
        var macro = _model.Draft().Macro;
        var problems = ProblemByStep();
        return new EditorSnapshot(
            Name,
            Description,
            Group,
            SubMacros,
            CallableParameters,
            CallableResults,
            FollowInputLimit,
            InputLimit,
            ToggleKey,
            IsDirty,
            Count(macro.Steps),
            [.. macro.Steps.Select(step => StepSnapshot(step, problems))],
            Triggers,
            Variables,
            Globals,
            ValueChoices,
            ValueFunctions,
            ConditionCompares,
            VariableTypes,
            TriggerKindOptions,
            CanUndo,
            CanRedo,
            CanSave,
            NameProblem,
            ProblemSummary,
            DeclarationProblemSummary,
            CallImpactSummary,
            Notice,
            TriggerProblemSummary,
            ToggleKeyProblem,
            Recording,
            IsSubMacro,
            Revision: ++_revision);
    }

    /// <summary>The design-time check grouped by the step it belongs to, both messages when one
    /// step has more than one.</summary>
    private Dictionary<MacroStep, string> ProblemByStep()
    {
        var byStep = new Dictionary<MacroStep, string>();
        foreach (var problem in _model.Problems)
        {
            byStep[problem.Step] = byStep.TryGetValue(problem.Step, out var seen)
                ? seen + "\n" + problem.Message
                : problem.Message;
        }

        return byStep;
    }

    /// <summary>The runnable actions, in catalogue order, with the grouping the palette draws.</summary>
    public static IReadOnlyList<EditorActionSnapshot> Actions()
        => [.. ActionCatalog.RunnableActions.Select(ActionSnapshot)];

    /// <summary>The category rail's order and labels, so the page never owns a second copy.</summary>
    public static IReadOnlyList<EditorActionCategorySnapshot> ActionCategories()
        => [.. ActionCategoryPresentation.BasicCategories().Select(choice =>
            new EditorActionCategorySnapshot(choice.Value?.ToString(), choice.Label, choice.Short))];

    private static EditorActionSnapshot ActionSnapshot(ActionDefinition action)
    {
        var category = ActionCategoryPresentation.PickerCategory(action.Category);
        var categoryLabel = Strings.Get($"Add.Category.{category}", category.ToString());
        var categoryShort = Strings.Get($"Add.Category.{category}.short", categoryLabel);
        string group;
        string groupLabel;
        int groupRank;
        if (category == ActionCategory.Control)
        {
            group = ActionCategoryPresentation.FlowGroup(action.Key);
            groupLabel = Strings.Get($"Palette.Flow.{group}", group);
            groupRank = ActionCategoryPresentation.FlowGroupRank(group);
        }
        else
        {
            group = action.Group;
            groupLabel = group.Length == 0
                ? Strings.Get("Palette.Group.Other")
                : Strings.Get($"Palette.Group.{group}", group);
            groupRank = int.MaxValue;
            var order = ActionCategoryPresentation.BasicGroups(category);
            for (var index = 0; index < order.Count; index++)
            {
                if (string.Equals(order[index], group, StringComparison.Ordinal))
                {
                    groupRank = index;
                    break;
                }
            }
        }

        return new EditorActionSnapshot(action.Key, action.LocalName, category.ToString(),
            categoryLabel, categoryShort, group, groupLabel, groupRank, action.LocalDescription,
            [.. action.Parameters
                .Where(parameter => parameter.Kind != ActionParameterKind.Steps)
                .Select(parameter => new EditorActionParameterSnapshot(
                    parameter.Name,
                    parameter.LocalLabel,
                    parameter.Kind.ToString(),
                    parameter.Required,
                    parameter.Placeholder,
                    [.. parameter.OptionChoices.Select(choice => new EditorChoiceSnapshot(
                        choice.Value, choice.Display))]))]);
    }

    private static int Count(IReadOnlyList<MacroStep> steps)
        => steps.Count + steps.Sum(step => step.Parameters
            .Where(parameter => parameter.Kind == ActionParameterKind.Steps)
            .Sum(parameter => Count(parameter.Steps)));

    private EditorConditionSnapshot ConditionSnapshot(StepParameter parameter)
    {
        ConditionDraftNode root;
        if (_conditionDrafts.TryGetValue(parameter, out var draft))
        {
            root = draft.Root;
        }
        else
        {
            if (!Condition.TryRead(parameter.Written, out var shape))
            {
                return new EditorConditionSnapshot("All", [], false);
            }

            root = ConditionDraftNode.Root(shape);
        }

        return new EditorConditionSnapshot(
            root.Join.ToString(),
            [.. root.Children.Select(NodeSnapshot)],
            true);
    }

    private static EditorConditionNodeSnapshot NodeSnapshot(ConditionDraftNode node)
        => node.IsGroup
            ? new EditorConditionNodeSnapshot("group", node.Join.ToString(), string.Empty,
                string.Empty, string.Empty, [.. node.Children.Select(NodeSnapshot)])
            : new EditorConditionNodeSnapshot("row", string.Empty,
                Expression.Render(node.Left), node.Compare.ToString(),
                Expression.Render(node.Right), []);

    private static MacroStep NewStep(string type)
    {
        var definition = ActionCatalog.Definitions.FirstOrDefault(action => action.Key == type)
            ?? throw new InvalidOperationException("unknown action " + type);
        var step = new MacroStep { Type = type };
        foreach (var field in definition.Parameters)
        {
            step.Parameters.Add(new StepParameter
            {
                Name = field.Name,
                Kind = field.Kind,
                Written = field.Kind == ActionParameterKind.Steps
                    ? Formula.Nothing
                    : WrittenFor(field.Kind, field.DefaultValue),
            });
        }

        return step;
    }

    private static ExpressionNode WrittenFor(ActionParameterKind kind, string value)
        => kind switch
        {
            ActionParameterKind.Bool or ActionParameterKind.Choice => Formula.Constant(value),
            ActionParameterKind.Number
                when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                    out var number) => Formula.Number(value, number),
            _ => Formula.Text(value),
        };

    private EditorStepSnapshot StepSnapshot(MacroStep step, IReadOnlyDictionary<MacroStep, string> problems)
    {
        var definition = ActionCatalog.Definitions.FirstOrDefault(action => action.Key == step.Type);
        var parameters = step.Parameters
            .Where(parameter => parameter.Kind != ActionParameterKind.Steps)
            .Select(parameter =>
            {
                var field = definition?.Parameters.FirstOrDefault(candidate =>
                    candidate.Name == parameter.Name);
                var columns = field?.Semantics.Columns ?? [];
                var parts = parameter.Written is ConcatNode run
                    ? run.Parts.Select(part => part switch
                    {
                        TextNode text => new EditorValuePartSnapshot("text", text.Text, string.Empty),
                        VariableNode name => new EditorValuePartSnapshot("name", string.Empty, name.Name),
                        _ => new EditorValuePartSnapshot("text", Expression.Render(part), string.Empty),
                    }).ToList()
                    : parameter.Written switch
                    {
                        TextNode text => [new EditorValuePartSnapshot("text", text.Text, string.Empty)],
                        VariableNode name => [new EditorValuePartSnapshot("name", string.Empty, name.Name)],
                        _ => new List<EditorValuePartSnapshot>(),
                    };
                var isNumber = field?.Kind is ActionParameterKind.Number;
                var isMultiline = field?.Kind is ActionParameterKind.MultilineText
                    or ActionParameterKind.Code;
                return new EditorParameterSnapshot(
                    parameter.Name,
                    field?.LocalLabel ?? parameter.Name,
                    parameter.Kind.ToString(),
                    parameter.Value,
                    [.. field?.OptionChoices.Select(choice => new EditorChoiceSnapshot(
                        choice.Value, choice.Display)) ?? []],
                    [.. columns.Select(column => new EditorColumnSnapshot(column.Name, column.Required))],
                    [.. parameter.Rows.Select(row => new EditorRowSnapshot(
                        [.. columns.Select(column => row.Text(column.Name))]))],
                    parameter.Kind == ActionParameterKind.Condition
                        ? ConditionSnapshot(parameter)
                        : null,
                    parameter.Value,
                    field?.ValueForm.ToString() ?? "Value",
                    field is not null && field.Kind is not (ActionParameterKind.Bool
                        or ActionParameterKind.Choice or ActionParameterKind.Step
                        or ActionParameterKind.Steps)
                        && field.Unit != ActionUnit.Percent,
                    field?.AcceptsFormula == true
                        || field?.Kind is ActionParameterKind.Expression or ActionParameterKind.Number,
                    field?.NamesVariable == true,
                    isNumber,
                    isNumber && parameter.Written is not NumberNode,
                    isMultiline,
                    field?.Picker is ActionPickerKind.Color,
                    field?.PathIntent is not PathIntent.None,
                    field?.Kind is ActionParameterKind.Key or ActionParameterKind.Keys,
                    field?.Placeholder ?? string.Empty,
                    field?.Required == true,
                    parts,
                    field?.Advanced == true,
                    field?.Group ?? string.Empty,
                    field is { Group.Length: > 0 } grouped
                        ? Strings.Get($"Palette.Group.{grouped.Group}")
                        : string.Empty,
                    field?.Kind == ActionParameterKind.Returns);
            })
            .ToList();
        var sections = step.StepLists
            .Select(list => new EditorStepSectionSnapshot(list.Name, step.ParameterLabel(list),
                [.. list.Steps.Select(child => StepSnapshot(child, problems))]))
            .ToList();
        var acceptsSteps = definition?.Parameters.Any(parameter =>
            parameter.Kind == ActionParameterKind.Steps) == true;
        return new EditorStepSnapshot(step.Id, step.Type,
            definition?.LocalName ?? step.Type, step.Meta.IsEnabled, step.Meta.Comment,
            step.Meta.TimeoutMs, step.Meta.RetryCount, step.Meta.RetryDelayMs,
            step.Meta.OnError.ToString(), acceptsSteps, step.Detail,
            problems.GetValueOrDefault(step) ?? string.Empty, step.InputMark, step.MetaSummary,
            parameters, sections);
    }

    private List<MacroStep> FindSteps(IReadOnlyList<string> stepIds)
    {
        var steps = new List<MacroStep>();
        foreach (var id in stepIds)
        {
            if (FindStep(id) is { } step && !steps.Contains(step))
            {
                steps.Add(step);
            }
        }

        return steps;
    }

    private static string? ClipboardText;

    private MacroStep? FindTreeStep(string stepId)
        => Find(stepId) is { } found ? found.List[found.Index] : null;

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
