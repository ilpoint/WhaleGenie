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
    private readonly string _submacroOriginal = string.Empty;
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

}
