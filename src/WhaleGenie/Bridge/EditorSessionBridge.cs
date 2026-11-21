using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Bridge;

/// <summary>
/// The C0 editor session over the bridge: open a macro, read the snapshot, change fields, add a
/// step, save, close. The native editor is still the one a person uses; this is the path the page
/// will take in C1, and the gate that proves it can be built without moving the model out of C#.
/// </summary>
internal sealed class EditorSessionBridge(ProjectWorkspace workspace) : IBridgeHandler
{
    private readonly HeadlessEditorWorkspace _documents = new(workspace);

    public void Register(BridgeServer server)
    {
        server.Register("editor.sessionOpen", Open);
        server.Register("editor.sessionTabs", _ => Task.FromResult<object?>(Tabs()));
        server.Register("editor.sessionNew", _ => Task.FromResult<object?>(NewDocument()));
        server.Register("editor.sessionActivate", Activate);
        server.Register("editor.sessionState", _ => Task.FromResult<object?>(Snapshot()));
        server.Register("editor.sessionPatch", Patch);
        server.Register("editor.sessionStep", AddStep);
        server.Register("editor.sessionStepEnabled", SetStepEnabled);
        server.Register("editor.sessionStepMeta", SetStepMeta);
        server.Register("editor.sessionConditionRowAdd", AddConditionRow);
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
        server.Register("editor.sessionSave", _ => Task.FromResult<object?>(Save()));
        server.Register("editor.sessionClose", Close);
        server.Register("editor.sessionActions", _ => Task.FromResult<object?>(Actions()));
    }

    private Task<object?> Open(JsonElement parameters)
    {
        var name = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        try
        {
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
        try
        {
            Required().AddStep(step, parentId);
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

    private Task<object?> AddConditionRow(JsonElement parameters)
        => ConditionCommand(parameters, (session, step, name) => session.AddConditionRow(step, name));

    private Task<object?> RemoveConditionRow(JsonElement parameters)
        => ConditionCommand(parameters, (session, step, name) =>
        {
            var index = parameters.TryGetProperty("index", out var row) && row.TryGetInt32(out var number)
                ? number
                : -1;
            session.RemoveConditionRow(step, name, index);
        });

    private Task<object?> SetConditionRow(JsonElement parameters)
        => ConditionCommand(parameters, (session, step, name) =>
        {
            var index = parameters.TryGetProperty("index", out var row) && row.TryGetInt32(out var number)
                ? number
                : -1;
            var field = parameters.TryGetProperty("field", out var named) ? named.GetString() : null;
            var value = parameters.TryGetProperty("value", out var written) ? written.GetString() : null;
            if (field is not { Length: > 0 } || value is null)
            {
                throw new InvalidOperationException("a condition cell needs a field and a value");
            }

            session.SetConditionRow(step, name, index, field, value);
        });

    private Task<object?> SetConditionJoin(JsonElement parameters)
        => ConditionCommand(parameters, (session, step, name) =>
        {
            var join = parameters.TryGetProperty("join", out var value) ? value.GetString() : null;
            if (join is not { Length: > 0 })
            {
                throw new InvalidOperationException("a condition join needs a value");
            }

            session.SetConditionJoin(step, name, join);
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
                dirty = snapshot.Dirty,
                stepCount = snapshot.StepCount,
                steps = snapshot.Steps,
                triggers = snapshot.Triggers,
                variables = snapshot.Variables,
                globals = snapshot.Globals,
                valueChoices = snapshot.ValueChoices,
                conditionCompares = snapshot.ConditionCompares,
                variableTypes = snapshot.VariableTypes,
                triggerKinds = snapshot.TriggerKinds,
                canUndo = snapshot.CanUndo,
                canRedo = snapshot.CanRedo,
            }
            : null;

    private static object Actions()
        => new
        {
            count = ActionCatalog.Definitions.Count,
            actions = HeadlessEditorSession.Actions(),
        };

    private HeadlessEditorSession Required()
        => _documents.Active ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
            Strings.Get("Bridge.EditorNoSession"));
}
