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
    private HeadlessEditorSession? _session;

    public void Register(BridgeServer server)
    {
        server.Register("editor.sessionOpen", Open);
        server.Register("editor.sessionState", _ => Task.FromResult<object?>(Snapshot()));
        server.Register("editor.sessionPatch", Patch);
        server.Register("editor.sessionStep", AddStep);
        server.Register("editor.sessionParameter", PatchParameter);
        server.Register("editor.sessionMove", MoveStep);
        server.Register("editor.sessionRemove", RemoveStep);
        server.Register("editor.sessionSave", _ => Task.FromResult<object?>(Save()));
        server.Register("editor.sessionClose", _ => Task.FromResult<object?>(Close()));
        server.Register("editor.sessionActions", _ => Task.FromResult<object?>(Actions()));
    }

    private Task<object?> Open(JsonElement parameters)
    {
        var name = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        try
        {
            _session = HeadlessEditorSession.Open(workspace, name);
            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException)
        {
            throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                Strings.Format("Bridge.NoSuchMacro", name ?? string.Empty));
        }
    }

    private Task<object?> Patch(JsonElement parameters)
    {
        var session = Required();
        session.Patch(
            parameters.TryGetProperty("name", out var name) ? name.GetString() : null,
            parameters.TryGetProperty("description", out var description) ? description.GetString() : null);
        return Task.FromResult<object?>(Snapshot());
    }

    private Task<object?> AddStep(JsonElement parameters)
    {
        if (!parameters.TryGetProperty("type", out var type) || type.GetString() is not { Length: > 0 } step)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        try
        {
            Required().AddStep(step);
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

    private object? Save()
    {
        var session = Required();
        if (!session.Save())
        {
            throw new BridgeFailureException(BridgeErrors.Internal,
                Strings.Get("Bridge.EditorSaveFailed"));
        }

        return Snapshot();
    }

    private object? Close()
    {
        _session = null;
        return null;
    }

    private object? Snapshot()
        => _session?.Snapshot() is { } snapshot
            ? new
            {
                name = snapshot.Name,
                description = snapshot.Description,
                dirty = snapshot.Dirty,
                stepCount = snapshot.StepCount,
                steps = snapshot.Steps,
            }
            : null;

    private static object Actions()
        => new
        {
            count = ActionCatalog.Definitions.Count,
            actions = HeadlessEditorSession.Actions(),
        };

    private HeadlessEditorSession Required()
        => _session ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
            Strings.Get("Bridge.EditorNoSession"));
}
