using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Recording;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Bridge;

/// <summary>
/// The C0 editor session over the bridge: open a macro, read the snapshot, change fields, add a
/// step, save, close. The native editor is still the one a person uses; this is the path the page
/// will take in C1, and the gate that proves it can be built without moving the model out of C#.
/// </summary>
internal sealed partial class EditorSessionBridge(ProjectWorkspace workspace,
    IDeviceLayer? devices = null, IEditorSettings? settings = null,
    IEditorPickerHost? pickers = null, IEditorWindowAside? aside = null,
    IEditorBrowserTestHost? browserTests = null, IEditorElementTestHost? elementTests = null)
    : IBridgeHandler
{
    private readonly HeadlessEditorWorkspace _documents = new(workspace, devices,
        settings ?? DefaultEditorSettings.Instance);
    private readonly IDeviceLayer? _devices = devices;
    private readonly IEditorSettings _settings = settings ?? DefaultEditorSettings.Instance;
    private readonly IEditorPickerHost? _pickers = pickers;
    private readonly IEditorWindowAside? _aside = aside;
    private readonly IEditorBrowserTestHost? _browserTests = browserTests;
    private readonly IEditorElementTestHost? _elementTests = elementTests;
    private RecordingSession? _recording;
    private volatile bool _stopRecordingRequested;

    public void Register(BridgeServer server)
    {
        server.Register("editor.session", SessionAction);
        server.Register("editor.sessionTabs", _ => Task.FromResult<object?>(Tabs()));
        server.Register("editor.declaration", DeclarationAction);
        server.Register("editor.sessionState", _ => Task.FromResult<object?>(Snapshot()));
        server.Register("editor.step", StepAction);
        server.Register("editor.condition", ConditionAction);
        server.Register("editor.steps", StepsAction);

        server.Register("editor.trigger", TriggerAction);
        server.Register("editor.variable", VariableAction);
        server.Register("editor.record", RecordAction);
        server.Register("editor.sessionActions", _ => Task.FromResult<object?>(Actions()));
        server.Register("editor.action", ActionCommand);
        server.Register("editor.actionOpen", ActionOpen);
        server.Register("editor.actionState", _ => Task.FromResult<object?>(Required().ActionFormState()));
        server.Register("editor.actionConfirm", _ => Task.FromResult<object?>(Required().ConfirmActionDraft()));
        server.Register("editor.actionCancel", _ => Task.FromResult<object?>(Required().CancelActionDraft()));
        server.Register("editor.actionWindowPreview", ActionWindowPreview);
        server.Register("editor.actionPick", ActionPick);
        server.Register("editor.actionLook", ActionLook);
        server.Register("editor.actionTestBrowser", ActionTestBrowser);
        server.Register("editor.actionTestElement", ActionTestElement);
        server.Register("editor.actionTestNarrow", ActionTestNarrow);
        server.Register("editor.actionWindowPick", ActionWindowPick);
        server.Register("editor.actionRun", ActionRun);
        server.Register("editor.actionKeyPad", ActionKeyPad);
        server.Register("editor.hotstringKeyPad", HotstringKeyPad);
        server.Register("editor.value", ValueAction);
        server.Register("editor.valueState", _ => Task.FromResult<object?>(Required().ValueState()));
    }



    private Task<object?> SessionAction(JsonElement parameters)
    {
        EditorSessionCommand command;
        try
        {
            command = parameters.Deserialize<EditorSessionCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsMacro"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsMacro"));
        }

        try
        {
            return Task.FromResult<object?>(ApplySession(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplySession(EditorSessionCommand command)
    {
        switch (command)
        {
            case EditorSessionOpenCommand open:
                _documents.Active?.CloseValueEditor();
                _documents.Active?.CancelActionDraft();
                try
                {
                    _documents.Open(open.Name);
                }
                catch (InvalidOperationException)
                {
                    throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                        Strings.Format("Bridge.NoSuchMacro", open.Name ?? string.Empty));
                }

                return Snapshot();
            case EditorSessionNewCommand:
                _documents.Active?.CloseValueEditor();
                _documents.Active?.CancelActionDraft();
                _documents.New();
                return Snapshot();
            case EditorSessionSubOpenCommand sub:
                var subName = SessionText(sub.Name, "Bridge.EditorNeedsMacro");
                var parentKey = _documents.ActiveKey;
                if (parentKey is null)
                {
                    throw new BridgeFailureException(BridgeErrors.BadArgument,
                        Strings.Get("Bridge.EditorNeedsMacro"));
                }

                _documents.Active?.CloseValueEditor();
                _documents.Active?.CancelActionDraft();
                _documents.OpenSubmacro(parentKey, subName);
                return Snapshot();
            case EditorSessionMacroSettingCommand setting:
                Required().SetMacroSetting(
                    SessionText(setting.Field, "Bridge.EditorNeedsField"), setting.Value);
                return Snapshot();
            case EditorSessionActivateCommand activate:
                var activeKey = SessionText(activate.Key, "Bridge.EditorNeedsDocument");
                _documents.Active?.CloseValueEditor();
                _documents.Active?.CancelActionDraft();
                _documents.Activate(activeKey);
                return Snapshot();
            case EditorSessionPatchCommand patch:
                Required().Patch(patch.Name, patch.Description, patch.Group);
                return Snapshot();
            case EditorSessionUndoCommand:
                Required().Undo();
                return Snapshot();
            case EditorSessionRedoCommand:
                Required().Redo();
                return Snapshot();
            case EditorSessionSaveCommand:
                var session = Required();
                try
                {
                    if (!session.Save())
                    {
                        throw new BridgeFailureException(BridgeErrors.Internal,
                            Strings.Get("Bridge.EditorSaveFailed"));
                    }
                }
                catch (InvalidOperationException error)
                {
                    throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
                }

                return Snapshot();
            case EditorSessionCloseCommand close:
                _documents.Active?.CloseValueEditor();
                _documents.Active?.CancelActionDraft();
                if (close.Key is { Length: > 0 } closeKey)
                {
                    _documents.Close(closeKey);
                }
                else
                {
                    _documents.CloseActive();
                }

                return Snapshot();
            case EditorSessionPublicMacroAddCommand add:
                return Required().AddPublicMacro(SessionText(add.Id, "Bridge.EditorNeedsStep"));
            case EditorSessionExportCommand export:
                return Required().Export(SessionText(export.Path, "Bridge.EditorNeedsFile"));
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsMacro"));
        }
    }

    private static string SessionText(string? value, string key)
        => value is { Length: > 0 }
            ? value
            : throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get(key));
    private object Tabs() => new
    {
        activeKey = _documents.ActiveKey,
        tabs = _documents.Tabs,
    };



    private Task<object?> StepAction(JsonElement parameters)
    {
        EditorStepCommand command;
        try
        {
            command = parameters.Deserialize<EditorStepCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsStep"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        try
        {
            return Task.FromResult<object?>(ApplyStep(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplyStep(EditorStepCommand command)
    {
        var session = Required();
        switch (command.Kind)
        {
            case "add":
                var type = StepText(command.Type, "Bridge.EditorNeedsStep");
                Dictionary<string, string>? presets = command.Parameters is null
                    ? null
                    : new Dictionary<string, string>(command.Parameters, StringComparer.Ordinal);
                try
                {
                    session.AddStep(type, command.ParentId, command.List, presets);
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException)
                {
                    throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
                }

                return Snapshot();
            case "meta":
                session.SetStepMeta(StepId(command.StepId),
                    StepText(command.Field, "Bridge.EditorNeedsStep"), command.Value ?? string.Empty);
                return Snapshot();
            case "parameter":
                session.PatchParameter(StepId(command.StepId), StepName(command.Name),
                    command.Value ?? string.Empty);
                return Snapshot();
            case "rowAdd":
                session.AddParameterRow(StepId(command.StepId), StepName(command.Name));
                return Snapshot();
            case "rowRemove":
                session.RemoveParameterRow(StepId(command.StepId), StepName(command.Name),
                    StepIndex(command.Index));
                return Snapshot();
            case "rowCell":
                session.PatchParameterRow(StepId(command.StepId), StepName(command.Name),
                    StepIndex(command.Index), StepText(command.Column, "Bridge.EditorNeedsRow"),
                    command.Value ?? string.Empty);
                return Snapshot();
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsStep"));
        }
    }

    private static string StepId(string? value)
        => StepText(value, "Bridge.EditorNeedsStep");

    private static string StepName(string? value)
        => StepText(value, "Bridge.EditorNeedsField");

    private static int StepIndex(int? value)
        => value is >= 0
            ? value.Value
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsRow"));

    private static string StepText(string? value, string key)
        => value is { Length: > 0 }
            ? value
            : throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get(key));

    private Task<object?> StepsAction(JsonElement parameters)
    {
        EditorStepsCommand command;
        try
        {
            command = parameters.Deserialize<EditorStepsCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsStep"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        try
        {
            return Task.FromResult<object?>(ApplySteps(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplySteps(EditorStepsCommand command)
    {
        var session = Required();
        var steps = command.Steps ?? [];
        if (command.Kind != "paste" && steps.Length == 0)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        switch (command.Kind)
        {
            case "enabled":
                session.SetStepsEnabled(steps, command.Enabled ?? false);
                return Snapshot();
            case "remove":
                session.RemoveSteps(steps);
                return Snapshot();
            case "duplicate":
                session.DuplicateSteps(steps);
                return Snapshot();
            case "group":
                session.GroupSteps(steps);
                return Snapshot();
            case "move":
                session.MoveStepsTo(steps,
                    StepsText(command.Target, "Bridge.EditorNeedsStep"), command.Before ?? false);
                return Snapshot();
            case "moveBy":
                session.MoveStepsBy(steps, command.Delta ?? 0);
                return Snapshot();
            case "lift":
                session.LiftSteps(steps);
                return Snapshot();
            case "copy":
                return session.CopySteps(steps);
            case "cut":
                return session.CutSteps(steps);
            case "paste":
                session.PasteSteps(command.Text, command.After);
                return Snapshot();
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsStep"));
        }
    }

    private static string StepsText(string? value, string key)
        => value is { Length: > 0 }
            ? value
            : throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get(key));

    private Task<object?> ConditionAction(JsonElement parameters)
    {
        EditorConditionCommand command;
        try
        {
            command = parameters.Deserialize<EditorConditionCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        try
        {
            var session = Required();
            var step = command.StepId;
            var name = command.Name;
            if (step.Length == 0 || name.Length == 0)
            {
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField"));
            }

            var path = command.Path ?? (command.Index is { } index ? [index] : []);
            switch (command.Kind)
            {
                case "rowAdd":
                    session.AddConditionRow(step, name, path);
                    break;
                case "groupAdd":
                    session.AddConditionGroup(step, name, path);
                    break;
                case "rowRemove":
                    session.RemoveConditionNode(step, name, path);
                    break;
                case "row":
                    session.SetConditionRow(step, name, path, ConditionText(command.Field),
                        command.Value ?? string.Empty);
                    break;
                case "join":
                    session.SetConditionJoin(step, name, ConditionText(command.Join), path);
                    break;
                default:
                    throw new BridgeFailureException(BridgeErrors.BadArgument,
                        Strings.Get("Bridge.EditorNeedsField"));
            }

            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private static string ConditionText(string? value)
        => value is { Length: > 0 }
            ? value
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));


    private Task<object?> RecordAction(JsonElement parameters)
    {
        EditorRecordCommand command;
        try
        {
            command = parameters.Deserialize<EditorRecordCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Editor.RecordFailed"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Editor.RecordFailed"));
        }

        try
        {
            return Task.FromResult<object?>(ApplyRecord(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplyRecord(EditorRecordCommand command)
    {
        switch (command.Kind)
        {
            case "start":
                return StartRecording();
            case "stop":
                return StopRecording();
            case "state":
                return RecordingState();
            case "option":
                if (command.Field is not { Length: > 0 } || command.Value is null)
                {
                    throw new BridgeFailureException(BridgeErrors.BadArgument,
                        Strings.Get("Editor.RecordFailed"));
                }

                Required().SetRecordOption(command.Field, command.Value);
                return Snapshot();
            case "delayScale":
                Required().SetDelayScale(command.Factor ?? throw new BridgeFailureException(
                    BridgeErrors.BadArgument, Strings.Get("Editor.Problem.CannotSave")));
                return Snapshot();
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Editor.RecordFailed"));
        }
    }

    private object StartRecording()
    {
        if (_devices is null)
        {
            throw new BridgeFailureException(BridgeErrors.Internal,
                Strings.Get("Editor.RecordFailed"));
        }

        _recording ??= new RecordingSession(_settings);
        _stopRecordingRequested = false;
        var model = Required();
        if (!_recording.Start(_devices, model.RecordIgnoreInjected, model.RecordGamepadButtons,
            () => _stopRecordingRequested = true))
        {
            _recording = null;
            throw new BridgeFailureException(BridgeErrors.Internal,
                Strings.Get("Editor.RecordFailed"));
        }

        return RecordingState();
    }

    private object StopRecording()
    {
        var outcome = _recording?.Stop(Required().RecordingChoices()) ?? new RecordingSession.Outcome([], 0);
        _recording = null;
        if (outcome.Steps.Count > 0)
        {
            Required().AppendSteps(outcome.Steps);
        }

        return new
        {
            captured = outcome.Captured,
            steps = outcome.Steps.Count,
            snapshot = Snapshot(),
        };
    }


    private object RecordingState()
    {
        if (_stopRecordingRequested)
        {
            _stopRecordingRequested = false;
            return StopRecording();
        }

        return new
        {
            recording = _recording?.IsRecording == true,
            captured = _recording?.Captured ?? 0,
        };
    }

    private Task<object?> DeclarationAction(JsonElement parameters)
    {
        EditorDeclarationCommand command;
        try
        {
            command = parameters.Deserialize<EditorDeclarationCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        try
        {
            return Task.FromResult<object?>(ApplyDeclaration(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplyDeclaration(EditorDeclarationCommand command)
    {
        var session = Required();
        var index = command.Index ?? -1;
        var fieldIndex = command.FieldIndex ?? -1;
        var offset = command.Offset ?? 0;
        var value = command.Value ?? string.Empty;
        switch (command.Family, command.Kind)
        {
            case ("parameter", "add"):
                session.AddCallableParameter();
                break;
            case ("parameter", "patch"):
                session.PatchCallableParameter(index, DeclarationField(command), value);
                break;
            case ("parameter", "remove"):
                session.RemoveCallableParameter(index);
                break;
            case ("parameter", "move"):
                session.MoveCallableParameter(index, offset);
                break;
            case ("parameter", "fieldAdd"):
                session.AddCallableParameterField(index);
                break;
            case ("parameter", "fieldPatch"):
                session.PatchCallableParameterField(index, fieldIndex, DeclarationField(command),
                    value);
                break;
            case ("parameter", "fieldRemove"):
                session.RemoveCallableParameterField(index, fieldIndex);
                break;
            case ("result", "add"):
                session.AddCallableResult();
                break;
            case ("result", "patch"):
                session.PatchCallableResult(index, DeclarationField(command), value);
                break;
            case ("result", "remove"):
                session.RemoveCallableResult(index);
                break;
            case ("result", "move"):
                session.MoveCallableResult(index, offset);
                break;
            case ("result", "fieldAdd"):
                session.AddCallableResultField(index);
                break;
            case ("result", "fieldPatch"):
                session.PatchCallableResultField(index, fieldIndex, DeclarationField(command),
                    value);
                break;
            case ("result", "fieldRemove"):
                session.RemoveCallableResultField(index, fieldIndex);
                break;
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField"));
        }

        return Snapshot();
    }

    private static string DeclarationField(EditorDeclarationCommand command)
        => command.Field is { Length: > 0 } field
            ? field
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));






    private Task<object?> TriggerAction(JsonElement parameters)
    {
        EditorTriggerCommand command;
        try
        {
            command = parameters.Deserialize<EditorTriggerCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsTrigger"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            return Task.FromResult<object?>(TriggerCommand(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? TriggerCommand(EditorTriggerCommand command)
    {
        var session = Required();
        switch (command)
        {
            case EditorTriggerAddCommand add:
                session.AddTrigger(add.TriggerKind.Length > 0
                    ? add.TriggerKind
                    : nameof(TriggerKind.Key));
                break;
            case EditorTriggerRemoveCommand remove:
                session.RemoveTrigger(Trigger(remove.TriggerId));
                break;
            case EditorTriggerBindingCommand binding:
                session.SetTriggerBinding(Trigger(binding.TriggerId), binding.Binding);
                break;
            case EditorTriggerLoopCommand loop:
                session.SetTriggerLoop(Trigger(loop.TriggerId), loop.Loop);
                break;
            case EditorTriggerOnceCommand once:
                session.SetTriggerOnce(Trigger(once.TriggerId), once.Once);
                break;
            case EditorTriggerEnabledCommand enabled:
                session.SetTriggerEnabled(Trigger(enabled.TriggerId), enabled.Enabled);
                break;
            case EditorTriggerFieldCommand field:
                SetTriggerField(session, field);
                break;
            case EditorTriggerHotstringCommand hotstring:
                session.SetTriggerHotstring(Trigger(hotstring.TriggerId), hotstring.Value);
                break;
            case EditorTriggerHotstringEndKeyCommand endKey:
                session.SetTriggerHotstringEndKey(Trigger(endKey.TriggerId), endKey.Value,
                    endKey.Checked);
                break;
            case EditorTriggerHotstringEndResetCommand endReset:
                session.ResetTriggerHotstringEndKeys(Trigger(endReset.TriggerId));
                break;
            case EditorTriggerHotstringEndCaptureCommand endCapture:
                session.CaptureTriggerHotstringEnd(Trigger(endCapture.TriggerId),
                    endCapture.Character);
                break;
            case EditorTriggerInterfaceHotkeyCommand hotkey:
                session.SetTriggerInterfaceHotkey(Trigger(hotkey.TriggerId), hotkey.Binding);
                break;
            case EditorTriggerInterfaceHotkeyClearCommand clearHotkey:
                session.ClearTriggerInterfaceHotkey(Trigger(clearHotkey.TriggerId));
                break;
            case EditorTriggerIdleCommand idle:
                session.SetTriggerIdle(Trigger(idle.TriggerId), idle.Value);
                break;
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        return Snapshot();
    }

    private static void SetTriggerField(HeadlessEditorSession session,
        EditorTriggerFieldCommand field)
    {
        var id = Trigger(field.TriggerId);
        if (field.Field.Length == 0)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        switch (field.Family)
        {
            case "timer":
                session.SetTriggerTimer(id, field.Field, field.Value);
                break;
            case "file":
                session.SetTriggerFile(id, field.Field, field.Value);
                break;
            case "process":
                session.SetTriggerProcess(id, field.Field, field.Value);
                break;
            case "hotstringOption":
                session.SetTriggerHotstringOption(id, field.Field, field.Value);
                break;
            case "color":
                session.SetTriggerColor(id, field.Field, field.Value);
                break;
            case "window":
                session.SetTriggerWindow(id, field.Field, field.Value);
                break;
            case "interface":
                session.SetTriggerInterface(id, field.Field, field.Value);
                break;
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsTrigger"));
        }
    }

    private static string Trigger(string triggerId)
        => triggerId is { Length: > 0 }
            ? triggerId
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));

    private Task<object?> VariableAction(JsonElement parameters)
    {
        EditorVariableCommand command;
        try
        {
            command = parameters.Deserialize<EditorVariableCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsVariable"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsVariable"));
        }

        try
        {
            return Task.FromResult<object?>(ApplyVariable(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplyVariable(EditorVariableCommand command)
    {
        var session = Required();
        switch (command.Family, command.Kind)
        {
            case ("local", "add"):
                session.AddVariable();
                break;
            case ("local", "patch"):
                session.PatchVariable(VariableName(command.OriginalName),
                    VariableField(command.Field), command.Value ?? string.Empty);
                break;
            case ("local", "remove"):
                session.RemoveVariable(VariableName(command.Name));
                break;
            case ("global", "add"):
                session.AddGlobal();
                break;
            case ("global", "patch"):
                session.PatchGlobal(VariableName(command.OriginalName),
                    VariableField(command.Field), command.Value ?? string.Empty);
                break;
            case ("global", "remove"):
                session.RemoveGlobal(VariableName(command.Name));
                break;
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsVariable"));
        }

        return Snapshot();
    }

    private static string VariableName(string? name)
        => name is { Length: > 0 }
            ? name
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsVariable"));

    private static string VariableField(string? field)
        => field is { Length: > 0 }
            ? field
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsVariable"));




    private object? Snapshot()
    {
        var active = _documents.Active;
        if (active?.Snapshot() is not { } snapshot)
        {
            return null;
        }

        return new
        {
                name = snapshot.Name,
                description = snapshot.Description,
                group = snapshot.Group,
                subMacros = snapshot.SubMacros,
                callableParameters = snapshot.CallableParameters,
                callableResults = snapshot.CallableResults,
                followInputLimit = snapshot.FollowInputLimit,
                inputLimit = snapshot.InputLimit,
                toggleKey = snapshot.ToggleKey,
                dirty = snapshot.Dirty,
                stepCount = snapshot.StepCount,
                steps = snapshot.Steps,
                triggers = snapshot.Triggers,
                variables = snapshot.Variables,
                globals = snapshot.Globals,
                valueChoices = snapshot.ValueChoices,
                valueFunctions = snapshot.ValueFunctions,
                conditionCompares = snapshot.ConditionCompares,
                variableTypes = snapshot.VariableTypes,
                triggerKinds = snapshot.TriggerKinds,
                canUndo = snapshot.CanUndo,
                canRedo = snapshot.CanRedo,
                canSave = snapshot.CanSave,
                nameProblem = snapshot.NameProblem,
                problemSummary = snapshot.ProblemSummary,
                declarationProblemSummary = snapshot.DeclarationProblemSummary,
                callImpactSummary = snapshot.CallImpactSummary,
                notice = snapshot.Notice,
                triggerProblemSummary = snapshot.TriggerProblemSummary,
                toggleKeyProblem = snapshot.ToggleKeyProblem,
                recording = snapshot.Recording,
                isSubMacro = snapshot.IsSubMacro,
                tabs = _documents.Tabs,
                activeKey = _documents.ActiveKey,
                actionForm = active.ActionFormState(),
                revision = snapshot.Revision,
            };
    }

    private static readonly JsonSerializerOptions CommandOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private Task<object?> ValueAction(JsonElement parameters)
    {
        EditorValueCommand command;
        try
        {
            command = parameters.Deserialize<EditorValueCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        try
        {
            return Task.FromResult<object?>(command switch
            {
                EditorValueOpenCommand open => OpenValue(open),
                EditorValueKeyCommand key => Required().ValueCommand(new ValueEditorCommand(
                    key.Action,
                    Token: key.Token,
                    Name: key.Name,
                    Sign: key.Sign,
                    Group: key.Group,
                    Text: key.Text,
                    Path: key.Path,
                    Target: key.Target,
                    After: key.After,
                    Number: key.Number,
                    Direction: key.Direction,
                    At: key.At,
                    Width: key.Width,
                    Append: key.Append)),
                EditorValueConfirmCommand => Required().ConfirmValue(),
                EditorValueCancelCommand => Required().CancelValue(),
                _ => throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField")),
            });
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private EditorValueSnapshot OpenValue(EditorValueOpenCommand open)
    {
        if (open.Step.Length == 0 || open.Parameter.Length == 0)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        if (open.RowIndex is >= 0 && open.Column is { Length: > 0 })
        {
            return Required().OpenRowValue(open.Step, open.Parameter, open.RowIndex.Value,
                open.Column);
        }

        if (open.ConditionPath is not null && open.ConditionField is { Length: > 0 })
        {
            return Required().OpenConditionValue(open.Step, open.Parameter,
                open.ConditionPath, open.ConditionField);
        }

        return Required().OpenValue(open.Step, open.Parameter);
    }

    private Task<object?> ActionOpen(JsonElement parameters)
    {
        var type = OptionalString(parameters, "type");
        var stepId = OptionalString(parameters, "stepId");
        var parentId = OptionalString(parameters, "parentId") ?? string.Empty;
        var list = OptionalString(parameters, "list") ?? string.Empty;
        var target = OptionalString(parameters, "targetStepId") ?? string.Empty;
        var before = parameters.TryGetProperty("before", out var direction) && direction.GetBoolean();
        try
        {
            return Task.FromResult<object?>(stepId is { Length: > 0 }
                ? Required().OpenStepDraft(stepId)
                : type is { Length: > 0 }
                    ? Required().OpenActionDraft(type, parentId, list, target, before)
                    : throw new BridgeFailureException(BridgeErrors.BadArgument,
                        Strings.Get("Bridge.EditorNeedsStep")));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private static string? OptionalString(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? OptionalBool(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;

    private static int? OptionalInt(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.TryGetInt32(out var number)
            ? number
            : null;

    private static double? OptionalDouble(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.TryGetDouble(out var number)
            ? number
            : null;

    private static int[]? OptionalPath(JsonElement parameters, string name)
        => parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray().Select(item => item.GetInt32())]
            : null;



    private static EditorCatalogue Actions()
        => new(
            ActionCatalog.RunnableActions.Count,
            HeadlessEditorSession.Actions(),
            HeadlessEditorSession.ActionCategories(),
            HeadlessEditorSession.PublicMacros());

    private HeadlessEditorSession Required()
        => _documents.Active ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
            Strings.Get("Bridge.EditorNoSession"));
}
