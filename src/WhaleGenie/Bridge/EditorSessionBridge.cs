using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
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
internal sealed class EditorSessionBridge(ProjectWorkspace workspace, IDeviceLayer? devices = null)
    : IBridgeHandler
{
    private readonly HeadlessEditorWorkspace _documents = new(workspace);
    private readonly IDeviceLayer? _devices = devices;
    private RecordingSession? _recording;
    private volatile bool _stopRecordingRequested;

    public void Register(BridgeServer server)
    {
        server.Register("editor.sessionOpen", Open);
        server.Register("editor.sessionTabs", _ => Task.FromResult<object?>(Tabs()));
        server.Register("editor.sessionNew", _ => Task.FromResult<object?>(NewDocument()));
        server.Register("editor.sessionSubOpen", OpenSubmacro);
        server.Register("editor.sessionMacroSetting", SetMacroSetting);
        server.Register("editor.sessionParameterDeclAdd", AddCallableParameter);
        server.Register("editor.sessionParameterDeclPatch", PatchCallableParameter);
        server.Register("editor.sessionParameterDeclRemove", RemoveCallableParameter);
        server.Register("editor.sessionParameterDeclMove", MoveCallableParameter);
        server.Register("editor.sessionResultDeclAdd", AddCallableResult);
        server.Register("editor.sessionResultDeclPatch", PatchCallableResult);
        server.Register("editor.sessionResultDeclRemove", RemoveCallableResult);
        server.Register("editor.sessionResultDeclMove", MoveCallableResult);
        server.Register("editor.sessionParameterFieldAdd", AddCallableParameterField);
        server.Register("editor.sessionParameterFieldPatch", PatchCallableParameterField);
        server.Register("editor.sessionParameterFieldRemove", RemoveCallableParameterField);
        server.Register("editor.sessionResultFieldAdd", AddCallableResultField);
        server.Register("editor.sessionResultFieldPatch", PatchCallableResultField);
        server.Register("editor.sessionResultFieldRemove", RemoveCallableResultField);
        server.Register("editor.sessionActivate", Activate);
        server.Register("editor.sessionState", _ => Task.FromResult<object?>(Snapshot()));
        server.Register("editor.sessionPatch", Patch);
        server.Register("editor.sessionStep", AddStep);
        server.Register("editor.sessionStepEnabled", SetStepEnabled);
        server.Register("editor.sessionStepMeta", SetStepMeta);
        server.Register("editor.sessionConditionRowAdd", AddConditionRow);
        server.Register("editor.sessionConditionGroupAdd", AddConditionGroup);
        server.Register("editor.sessionConditionRowRemove", RemoveConditionRow);
        server.Register("editor.sessionConditionRow", SetConditionRow);
        server.Register("editor.sessionConditionJoin", SetConditionJoin);
        server.Register("editor.sessionParameter", PatchParameter);
        server.Register("editor.sessionParameterRowAdd", AddParameterRow);
        server.Register("editor.sessionParameterRowRemove", RemoveParameterRow);
        server.Register("editor.sessionParameterRowCell", PatchParameterRow);
        server.Register("editor.sessionUndo", _ => Task.FromResult<object?>(Undo()));
        server.Register("editor.sessionRedo", _ => Task.FromResult<object?>(Redo()));
        server.Register("editor.sessionMove", MoveStep);
        server.Register("editor.sessionRemove", RemoveStep);
        server.Register("editor.sessionStepDuplicate", DuplicateStep);
        server.Register("editor.sessionStepsEnabled", SetStepsEnabled);
        server.Register("editor.sessionStepsRemove", RemoveSteps);
        server.Register("editor.sessionStepsDuplicate", DuplicateSteps);
        server.Register("editor.sessionStepsGroup", GroupSteps);
        server.Register("editor.sessionStepsMove", MoveSteps);
        server.Register("editor.sessionStepsMoveBy", MoveStepsBy);
        server.Register("editor.sessionStepsLift", LiftSteps);
        server.Register("editor.sessionStepsCopy", CopySteps);
        server.Register("editor.sessionStepsCut", CutSteps);
        server.Register("editor.sessionStepsPaste", PasteSteps);

        server.Register("editor.sessionTriggerAdd", AddTrigger);
        server.Register("editor.sessionTriggerRemove", RemoveTrigger);
        server.Register("editor.sessionTriggerBinding", SetTriggerBinding);
        server.Register("editor.sessionTriggerLoop", SetTriggerLoop);
        server.Register("editor.sessionTriggerOnce", SetTriggerOnce);
        server.Register("editor.sessionTriggerEnabled", SetTriggerEnabled);
        server.Register("editor.sessionTriggerTimer", SetTriggerTimer);
        server.Register("editor.sessionTriggerFile", SetTriggerFile);
        server.Register("editor.sessionTriggerProcess", SetTriggerProcess);
        server.Register("editor.sessionTriggerHotstring", SetTriggerHotstring);
        server.Register("editor.sessionTriggerColor", SetTriggerColor);
        server.Register("editor.sessionTriggerWindow", SetTriggerWindow);
        server.Register("editor.sessionTriggerInterface", SetTriggerInterface);
        server.Register("editor.sessionTriggerIdle", SetTriggerIdle);
        server.Register("editor.sessionVariableAdd", _ => Task.FromResult<object?>(AddVariable()));
        server.Register("editor.sessionVariablePatch", PatchVariable);
        server.Register("editor.sessionVariableRemove", RemoveVariable);
        server.Register("editor.sessionGlobalAdd", _ => Task.FromResult<object?>(AddGlobal()));
        server.Register("editor.sessionGlobalPatch", PatchGlobal);
        server.Register("editor.sessionGlobalRemove", RemoveGlobal);
        server.Register("editor.sessionRecordStart", _ => Task.FromResult<object?>(StartRecording()));
        server.Register("editor.sessionRecordStop", _ => Task.FromResult<object?>(StopRecording()));
        server.Register("editor.sessionRecordState", _ => Task.FromResult<object?>(RecordingState()));
        server.Register("editor.sessionSave", _ => Task.FromResult<object?>(Save()));
        server.Register("editor.sessionClose", Close);
        server.Register("editor.sessionActions", _ => Task.FromResult<object?>(Actions()));
        server.Register("editor.sessionPublicMacroAdd", AddPublicMacro);
        server.Register("editor.actionOpen", ActionOpen);
        server.Register("editor.actionState", _ => Task.FromResult<object?>(Required().ActionFormState()));
        server.Register("editor.actionConfirm", _ => Task.FromResult<object?>(Required().ConfirmActionDraft()));
        server.Register("editor.actionCancel", _ => Task.FromResult<object?>(Required().CancelActionDraft()));
        server.Register("editor.valueOpen", ValueOpen);
        server.Register("editor.valueState", _ => Task.FromResult<object?>(Required().ValueState()));
        server.Register("editor.valueCommand", ValueCommand);
        server.Register("editor.valueConfirm", _ => Task.FromResult<object?>(Required().ConfirmValue()));
        server.Register("editor.valueCancel", _ => Task.FromResult<object?>(Required().CancelValue()));
        server.Register("editor.sessionExport", Export);
    }

    private Task<object?> Open(JsonElement parameters)
    {
        var name = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        try
        {
            _documents.Active?.CloseValueEditor();
            _documents.Active?.CancelActionDraft();
            _documents.Open(name);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException)
        {
            throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                Strings.Format("Bridge.NoSuchMacro", name ?? string.Empty));
        }
    }

    private object? NewDocument()
    {
        _documents.Active?.CloseValueEditor();
            _documents.Active?.CancelActionDraft();
        _documents.New();
        return Snapshot();
    }

    private object Tabs() => new
    {
        activeKey = _documents.ActiveKey,
        tabs = _documents.Tabs,
    };

    private Task<object?> Activate(JsonElement parameters)
    {
        var key = parameters.TryGetProperty("key", out var named) ? named.GetString() : null;
        if (key is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsDocument"));
        }

        try
        {
            _documents.Active?.CloseValueEditor();
            _documents.Active?.CancelActionDraft();
            _documents.Activate(key);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> Patch(JsonElement parameters)
    {
        var session = Required();
        session.Patch(
            parameters.TryGetProperty("name", out var name) ? name.GetString() : null,
            parameters.TryGetProperty("description", out var description) ? description.GetString() : null,
            parameters.TryGetProperty("group", out var group) ? group.GetString() : null);
        return Task.FromResult<object?>(Snapshot());
    }

    private Task<object?> AddStep(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("type", out var type) || type.GetString() is not { Length: > 0 } step)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        var parentId = parameters.TryGetProperty("parentId", out var parent) ? parent.GetString() : null;
        var list = parameters.TryGetProperty("list", out var namedList) ? namedList.GetString() : null;
        Dictionary<string, string>? presets = null;
        if (parameters.TryGetProperty("parameters", out var values)
            && values.ValueKind == JsonValueKind.Object)
        {
            presets = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var field in values.EnumerateObject())
            {
                presets[field.Name] = field.Value.ValueKind == JsonValueKind.String
                    ? field.Value.GetString() ?? string.Empty
                    : field.Value.GetRawText();
            }
        }

        try
        {
            Required().AddStep(step, parentId, list, presets);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> PatchParameter(JsonElement parameters)
    {
        var stepId = parameters.TryGetProperty("stepId", out var id) ? id.GetString() : null;
        var name = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        var value = parameters.TryGetProperty("value", out var given) ? given.GetString() : null;
        if (stepId is not { Length: > 0 } || name is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        try
        {
            Required().PatchParameter(stepId, name, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> MoveStep(JsonElement parameters)
    {
        var stepId = parameters.TryGetProperty("stepId", out var id) ? id.GetString() : null;
        var delta = parameters.TryGetProperty("delta", out var given) && given.TryGetInt32(out var value)
            ? value
            : 0;
        if (stepId is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        try
        {
            Required().MoveStep(stepId, delta);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> DuplicateStep(JsonElement parameters)
    {
        var stepId = parameters.TryGetProperty("stepId", out var id) ? id.GetString() : null;
        if (stepId is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        try
        {
            Required().DuplicateStep(stepId);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> RemoveStep(JsonElement parameters)
    {
        var stepId = parameters.TryGetProperty("stepId", out var id) ? id.GetString() : null;
        if (stepId is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        try
        {
            Required().RemoveStep(stepId);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetStepsEnabled(JsonElement parameters)
    {
        var steps = StepIds(parameters);
        var enabled = parameters.TryGetProperty("enabled", out var value) && value.GetBoolean();
        try
        {
            Required().SetStepsEnabled(steps, enabled);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> RemoveSteps(JsonElement parameters)
    {
        var steps = StepIds(parameters);
        try
        {
            Required().RemoveSteps(steps);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> DuplicateSteps(JsonElement parameters)
    {
        var steps = StepIds(parameters);
        try
        {
            Required().DuplicateSteps(steps);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> GroupSteps(JsonElement parameters)
    {
        var steps = StepIds(parameters);
        try
        {
            Required().GroupSteps(steps);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> MoveSteps(JsonElement parameters)
    {
        var steps = StepIds(parameters);
        var target = parameters.TryGetProperty("target", out var named) ? named.GetString() : null;
        var before = parameters.TryGetProperty("before", out var value) && value.GetBoolean();
        if (target is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        try
        {
            Required().MoveStepsTo(steps, target, before);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> MoveStepsBy(JsonElement parameters)
    {
        var steps = StepIds(parameters);
        var delta = parameters.TryGetProperty("delta", out var value) && value.TryGetInt32(out var number)
            ? number
            : 0;
        try
        {
            Required().MoveStepsBy(steps, delta);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> LiftSteps(JsonElement parameters)
    {
        var steps = StepIds(parameters);
        try
        {
            Required().LiftSteps(steps);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> CopySteps(JsonElement parameters)
    {
        var steps = StepIds(parameters);
        return Task.FromResult<object?>(Required().CopySteps(steps));
    }

    private Task<object?> CutSteps(JsonElement parameters)
    {
        var steps = StepIds(parameters);
        return Task.FromResult<object?>(Required().CutSteps(steps));
    }

    private Task<object?> PasteSteps(JsonElement parameters)
    {
        var text = OptionalString(parameters, "text");
        var after = OptionalString(parameters, "after");
        try
        {
            Required().PasteSteps(text, after);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> AddConditionRow(JsonElement parameters)
        => ConditionCommand(parameters, (session, step, name) =>
            session.AddConditionRow(step, name, ConditionPath(parameters)));

    private Task<object?> AddConditionGroup(JsonElement parameters)
        => ConditionCommand(parameters, (session, step, name) =>
            session.AddConditionGroup(step, name, ConditionPath(parameters)));

    private Task<object?> RemoveConditionRow(JsonElement parameters)
        => ConditionCommand(parameters, (session, step, name) =>
            session.RemoveConditionNode(step, name, ConditionPath(parameters)));

    private Task<object?> SetConditionRow(JsonElement parameters)
        => ConditionCommand(parameters, (session, step, name) =>
        {
            var field = parameters.TryGetProperty("field", out var named) ? named.GetString() : null;
            var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
            if (field is not { Length: > 0 } || value is null)
            {
                throw new InvalidOperationException("a condition cell needs a field and a value");
            }

            session.SetConditionRow(step, name, ConditionPath(parameters), field, value);
        });

    private static IReadOnlyList<string> StepIds(JsonElement parameters)
    {
        if (parameters.TryGetProperty("steps", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            var ids = value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(item => item is { Length: > 0 })
                .Select(item => item!)
                .ToArray();
            if (ids.Length > 0)
            {
                return ids;
            }
        }

        throw new BridgeFailureException(BridgeErrors.BadArgument,
            Strings.Get("Bridge.EditorNeedsStep"));
    }

    private static int[] ConditionPath(JsonElement parameters)
    {
        if (parameters.TryGetProperty("path", out var path) && path.ValueKind == JsonValueKind.Array)
        {
            return [.. path.EnumerateArray().Select(item => item.GetInt32())];
        }

        return parameters.TryGetProperty("index", out var row) && row.TryGetInt32(out var index)
            ? [index]
            : [];
    }

    private Task<object?> SetConditionJoin(JsonElement parameters)
        => ConditionCommand(parameters, (session, step, name) =>
        {
            var join = parameters.TryGetProperty("join", out var value) ? value.GetString() : null;
            if (join is not { Length: > 0 })
            {
                throw new InvalidOperationException("a condition join needs a value");
            }

            session.SetConditionJoin(step, name, join, ConditionPath(parameters));
        });

    private Task<object?> ConditionCommand(JsonElement parameters,
        Action<HeadlessEditorSession, string, string> apply)
    {
        var stepId = parameters.TryGetProperty("stepId", out var id) ? id.GetString() : null;
        var name = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        if (stepId is not { Length: > 0 } || name is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        try
        {
            apply(Required(), stepId, name);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object StartRecording()
    {
        if (_devices is null)
        {
            throw new BridgeFailureException(BridgeErrors.Internal,
                Strings.Get("Editor.RecordFailed"));
        }

        _recording ??= new RecordingSession();
        _stopRecordingRequested = false;
        if (!_recording.Start(_devices, ignoreInjected: true, recordController: false,
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
        var outcome = _recording?.Stop(new RecordingOptions()) ?? new RecordingSession.Outcome([], 0);
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

    private Task<object?> AddCallableParameterField(JsonElement parameters)
        => DeclarationCommand(session => session.AddCallableParameterField(DeclarationIndex(parameters)));

    private Task<object?> PatchCallableParameterField(JsonElement parameters)
        => DeclarationCommand(session =>
        {
            var (index, fieldIndex, field, value) = FieldFields(parameters);
            session.PatchCallableParameterField(index, fieldIndex, field, value);
        });

    private Task<object?> RemoveCallableParameterField(JsonElement parameters)
        => DeclarationCommand(session => session.RemoveCallableParameterField(
            DeclarationIndex(parameters), FieldIndex(parameters)));

    private Task<object?> AddCallableResultField(JsonElement parameters)
        => DeclarationCommand(session => session.AddCallableResultField(DeclarationIndex(parameters)));

    private Task<object?> PatchCallableResultField(JsonElement parameters)
        => DeclarationCommand(session =>
        {
            var (index, fieldIndex, field, value) = FieldFields(parameters);
            session.PatchCallableResultField(index, fieldIndex, field, value);
        });

    private Task<object?> RemoveCallableResultField(JsonElement parameters)
        => DeclarationCommand(session => session.RemoveCallableResultField(
            DeclarationIndex(parameters), FieldIndex(parameters)));

    private static int FieldIndex(JsonElement parameters)
        => parameters.TryGetProperty("fieldIndex", out var index) && index.TryGetInt32(out var number)
            ? number
            : -1;

    private static (int Index, int FieldIndex, string Field, string Value) FieldFields(
        JsonElement parameters)
    {
        var (index, field, value) = DeclarationFields(parameters);
        return (index, FieldIndex(parameters), field, value);
    }

    private Task<object?> AddCallableParameter(JsonElement parameters)
        => DeclarationCommand(session => session.AddCallableParameter());

    private Task<object?> PatchCallableParameter(JsonElement parameters)
        => DeclarationCommand(session =>
        {
            var (index, field, value) = DeclarationFields(parameters);
            session.PatchCallableParameter(index, field, value);
        });

    private Task<object?> MoveCallableParameter(JsonElement parameters)
        => DeclarationCommand(session => session.MoveCallableParameter(
            DeclarationIndex(parameters), DeclarationOffset(parameters)));

    private Task<object?> MoveCallableResult(JsonElement parameters)
        => DeclarationCommand(session => session.MoveCallableResult(
            DeclarationIndex(parameters), DeclarationOffset(parameters)));

    private static int DeclarationOffset(JsonElement parameters)
        => parameters.TryGetProperty("offset", out var offset) && offset.TryGetInt32(out var number)
            ? number
            : 0;

    private Task<object?> RemoveCallableParameter(JsonElement parameters)
        => DeclarationCommand(session => session.RemoveCallableParameter(DeclarationIndex(parameters)));

    private Task<object?> AddCallableResult(JsonElement parameters)
        => DeclarationCommand(session => session.AddCallableResult());

    private Task<object?> PatchCallableResult(JsonElement parameters)
        => DeclarationCommand(session =>
        {
            var (index, field, value) = DeclarationFields(parameters);
            session.PatchCallableResult(index, field, value);
        });

    private Task<object?> RemoveCallableResult(JsonElement parameters)
        => DeclarationCommand(session => session.RemoveCallableResult(DeclarationIndex(parameters)));

    private Task<object?> DeclarationCommand(Action<HeadlessEditorSession> apply)
    {
        try
        {
            apply(Required());
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private static int DeclarationIndex(JsonElement parameters)
        => parameters.TryGetProperty("index", out var index) && index.TryGetInt32(out var number)
            ? number
            : -1;

    private static (int Index, string Field, string Value) DeclarationFields(JsonElement parameters)
    {
        var field = parameters.TryGetProperty("field", out var named) ? named.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (field is not { Length: > 0 } || value is null)
        {
            throw new InvalidOperationException("a declaration field needs a field and a value");
        }

        return (DeclarationIndex(parameters), field, value);
    }

    private Task<object?> SetMacroSetting(JsonElement parameters)
    {
        var field = parameters.TryGetProperty("field", out var named) ? named.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (field is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        try
        {
            Required().SetMacroSetting(field, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> OpenSubmacro(JsonElement parameters)
    {
        var name = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        var parentKey = _documents.ActiveKey;
        if (name is not { Length: > 0 } || parentKey is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsMacro"));
        }

        try
        {
            _documents.Active?.CloseValueEditor();
            _documents.Active?.CancelActionDraft();
            _documents.OpenSubmacro(parentKey, name);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetStepMeta(JsonElement parameters)
    {
        var stepId = parameters.TryGetProperty("stepId", out var id) ? id.GetString() : null;
        var field = parameters.TryGetProperty("field", out var named) ? named.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (stepId is not { Length: > 0 } || field is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        try
        {
            Required().SetStepMeta(stepId, field, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetStepEnabled(JsonElement parameters)
    {
        var stepId = parameters.TryGetProperty("stepId", out var id) ? id.GetString() : null;
        var enabled = parameters.TryGetProperty("enabled", out var value) && value.GetBoolean();
        if (stepId is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        try
        {
            Required().SetStepEnabled(stepId, enabled);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> AddParameterRow(JsonElement parameters)
    {
        var stepId = parameters.TryGetProperty("stepId", out var id) ? id.GetString() : null;
        var name = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        if (stepId is not { Length: > 0 } || name is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        try
        {
            Required().AddParameterRow(stepId, name);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> RemoveParameterRow(JsonElement parameters)
    {
        var stepId = parameters.TryGetProperty("stepId", out var id) ? id.GetString() : null;
        var name = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        var index = parameters.TryGetProperty("index", out var row) && row.TryGetInt32(out var number)
            ? number
            : -1;
        if (stepId is not { Length: > 0 } || name is not { Length: > 0 } || index < 0)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsRow"));
        }

        try
        {
            Required().RemoveParameterRow(stepId, name, index);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> PatchParameterRow(JsonElement parameters)
    {
        var stepId = parameters.TryGetProperty("stepId", out var id) ? id.GetString() : null;
        var name = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        var column = parameters.TryGetProperty("column", out var cell) ? cell.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        var index = parameters.TryGetProperty("index", out var row) && row.TryGetInt32(out var number)
            ? number
            : -1;
        if (stepId is not { Length: > 0 } || name is not { Length: > 0 }
            || column is not { Length: > 0 } || value is null || index < 0)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsRow"));
        }

        try
        {
            Required().PatchParameterRow(stepId, name, index, column, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? Undo()
    {
        Required().Undo();
        return Snapshot();
    }

    private object? Redo()
    {
        Required().Redo();
        return Snapshot();
    }

    private Task<object?> SetTriggerLoop(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        var loop = parameters.TryGetProperty("loop", out var named) ? named.GetString() : null;
        if (triggerId is not { Length: > 0 } || loop is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().SetTriggerLoop(triggerId, loop);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetTriggerInterface(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        var field = parameters.TryGetProperty("field", out var named) ? named.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (triggerId is not { Length: > 0 } || field is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().SetTriggerInterface(triggerId, field, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetTriggerWindow(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        var field = parameters.TryGetProperty("field", out var named) ? named.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (triggerId is not { Length: > 0 } || field is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().SetTriggerWindow(triggerId, field, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetTriggerColor(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        var field = parameters.TryGetProperty("field", out var named) ? named.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (triggerId is not { Length: > 0 } || field is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().SetTriggerColor(triggerId, field, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetTriggerIdle(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (triggerId is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().SetTriggerIdle(triggerId, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetTriggerProcess(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        var field = parameters.TryGetProperty("field", out var named) ? named.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (triggerId is not { Length: > 0 } || field is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().SetTriggerProcess(triggerId, field, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetTriggerHotstring(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (triggerId is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().SetTriggerHotstring(triggerId, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetTriggerFile(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        var field = parameters.TryGetProperty("field", out var named) ? named.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (triggerId is not { Length: > 0 } || field is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().SetTriggerFile(triggerId, field, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetTriggerTimer(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        var field = parameters.TryGetProperty("field", out var named) ? named.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (triggerId is not { Length: > 0 } || field is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().SetTriggerTimer(triggerId, field, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetTriggerEnabled(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        var enabled = parameters.TryGetProperty("enabled", out var value) && value.GetBoolean();
        if (triggerId is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().SetTriggerEnabled(triggerId, enabled);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetTriggerOnce(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        var once = parameters.TryGetProperty("once", out var value) && value.GetBoolean();
        if (triggerId is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().SetTriggerOnce(triggerId, once);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? AddGlobal()
    {
        Required().AddGlobal();
        return Snapshot();
    }

    private Task<object?> PatchGlobal(JsonElement parameters)
    {
        var original = parameters.TryGetProperty("originalName", out var name) ? name.GetString() : null;
        var field = parameters.TryGetProperty("field", out var property) ? property.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (original is null || field is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsVariable"));
        }

        try
        {
            Required().PatchGlobal(original, field, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> RemoveGlobal(JsonElement parameters)
    {
        var name = parameters.TryGetProperty("name", out var written) ? written.GetString() : null;
        if (name is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsVariable"));
        }

        try
        {
            Required().RemoveGlobal(name);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? AddVariable()
    {
        Required().AddVariable();
        return Snapshot();
    }

    private Task<object?> PatchVariable(JsonElement parameters)
    {
        var original = parameters.TryGetProperty("originalName", out var name) ? name.GetString() : null;
        var field = parameters.TryGetProperty("field", out var property) ? property.GetString() : null;
        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
        if (original is null || field is not { Length: > 0 } || value is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsVariable"));
        }

        try
        {
            Required().PatchVariable(original, field, value);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> RemoveVariable(JsonElement parameters)
    {
        var name = parameters.TryGetProperty("name", out var written) ? written.GetString() : null;
        if (name is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsVariable"));
        }

        try
        {
            Required().RemoveVariable(name);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> AddTrigger(JsonElement parameters)
    {
        var kind = parameters.TryGetProperty("kind", out var named) ? named.GetString() : null;
        try
        {
            Required().AddTrigger(kind is { Length: > 0 } ? kind : nameof(TriggerKind.Key));
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> RemoveTrigger(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        if (triggerId is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().RemoveTrigger(triggerId);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> SetTriggerBinding(JsonElement parameters)
    {
        var triggerId = parameters.TryGetProperty("triggerId", out var id) ? id.GetString() : null;
        var binding = parameters.TryGetProperty("binding", out var text) ? text.GetString() : null;
        if (triggerId is not { Length: > 0 } || binding is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            Required().SetTriggerBinding(triggerId, binding);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? Save()
    {
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
    }

    private Task<object?> Close(JsonElement parameters)
    {
        try
        {
            var key = parameters.TryGetProperty("key", out var named) ? named.GetString() : null;
            _documents.Active?.CloseValueEditor();
            _documents.Active?.CancelActionDraft();
            if (key is { Length: > 0 })
            {
                _documents.Close(key);
            }
            else
            {
                _documents.CloseActive();
            }

            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? Snapshot()
        => _documents.Active?.Snapshot() is { } snapshot
            ? new
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
                isSubMacro = snapshot.IsSubMacro,
            }
            : null;

    private Task<object?> ValueOpen(JsonElement parameters)
    {
        var step = parameters.TryGetProperty("step", out var stepName) ? stepName.GetString() : null;
        var parameter = parameters.TryGetProperty("parameter", out var field) ? field.GetString() : null;
        if (step is not { Length: > 0 } || parameter is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        try
        {
            var conditionField = OptionalString(parameters, "conditionField");
            var path = OptionalPath(parameters, "conditionPath");
            var row = OptionalInt(parameters, "rowIndex");
            var column = OptionalString(parameters, "column");
            if (row is >= 0 && column is { Length: > 0 })
            {
                return Task.FromResult<object?>(Required().OpenRowValue(step, parameter, row.Value, column));
            }

            if (path is not null && conditionField is { Length: > 0 })
            {
                return Task.FromResult<object?>(Required().OpenConditionValue(step, parameter, path, conditionField));
            }

            return Task.FromResult<object?>(Required().OpenValue(step, parameter));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
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

    private Task<object?> ValueCommand(JsonElement parameters)
    {
        try
        {
            var command = new ValueEditorCommand(
                parameters.TryGetProperty("action", out var action) ? action.GetString() ?? "" : "",
                Token: OptionalString(parameters, "token"),
                Name: OptionalString(parameters, "name"),
                Sign: OptionalString(parameters, "sign"),
                Group: OptionalString(parameters, "group"),
                Text: OptionalString(parameters, "text"),
                Path: OptionalPath(parameters, "path"),
                Target: OptionalPath(parameters, "target"),
                After: OptionalBool(parameters, "after"),
                Number: OptionalBool(parameters, "number"),
                Direction: OptionalInt(parameters, "direction"),
                At: OptionalInt(parameters, "at"),
                Width: OptionalDouble(parameters, "width"),
                Append: OptionalBool(parameters, "append"));
            return Task.FromResult<object?>(Required().ValueCommand(command));
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

    private Task<object?> Export(JsonElement parameters)
    {
        var path = parameters.TryGetProperty("path", out var named) ? named.GetString() : null;
        if (path is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsFile"));
        }

        try
        {
            return Task.FromResult<object?>(Required().Export(path));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> AddPublicMacro(JsonElement parameters)
    {
        var id = parameters.TryGetProperty("id", out var named) ? named.GetString() : null;
        if (id is not { Length: > 0 })
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        try
        {
            return Task.FromResult<object?>(Required().AddPublicMacro(id));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private static object Actions()
        => new
        {
            count = ActionCatalog.RunnableActions.Count,
            actions = HeadlessEditorSession.Actions(),
            categories = HeadlessEditorSession.ActionCategories(),
            publicMacros = HeadlessEditorSession.PublicMacros(),
        };

    private HeadlessEditorSession Required()
        => _documents.Active ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
            Strings.Get("Bridge.EditorNoSession"));
}
