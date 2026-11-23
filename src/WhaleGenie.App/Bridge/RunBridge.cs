using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

/// <summary>The web run panel: one debugger session, the handler that reads the page's requests and the pushes beside it.</summary>
internal sealed partial class RunBridge : IBridgeHandler, IDisposable
{
    private readonly ProjectWorkspace _workspace;
    private readonly RunSession _session;
    private BridgeServer? _server;

    public RunBridge(ProjectWorkspace workspace, TriggerRuntime runtime, IUiPost? ui = null)
    {
        _workspace = workspace;
        _session = new RunSession(workspace, runtime, ui);
        Watch();
    }

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("run.state", _ => Task.FromResult<object?>(Snapshot()));
        server.Register("run.unload", Unload);
        server.Register("run.load", LoadAsync);
        server.Register("run.lookSelect", LookSelect);
        server.Register("run.command", CommandAsync);
        server.Register("run.breakpoint", Breakpoint);
        server.Register("run.breakpointRule", BreakpointRule);
        server.Register("run.argument", Argument);
        server.Register("run.answer", Answer);
    }

    public void Dispose() => _session.Dispose();

    private async Task<object?> LoadAsync(JsonElement parameters)
    {
        var name = parameters.TryGetProperty("name", out var given) ? given.GetString() : null;
        var macro = _workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (macro is null)
        {
            throw new BridgeFailureException(BridgeErrors.NoSuchMacro, Strings.Format("Bridge.NoSuchMacro", name ?? string.Empty));
        }

        await _session.LoadAsync(macro);
        StartClock();
        await PushAsync();
        return Snapshot();
    }

    private async Task<object?> CommandAsync(JsonElement parameters)
    {
        var command = parameters.TryGetProperty("command", out var named) ? named.GetString() : null;
        if (!_session.IsLoaded)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get("Run.NotLoaded"));
        }

        if (!await _session.CommandAsync(command ?? string.Empty))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get("Run.UnknownCommand"));
        }

        StartClock();
        await PushAsync();
        return Snapshot();
    }

    /// <summary>
    /// Shows a picture the session already kept. Nothing runs again and nothing is read again: what
    /// changes is which of the frames the run took is on show, so the delta pushed for it is the
    /// frame and the panel's own state, not the rows or the log.
    /// </summary>
    private async Task<object?> LookSelect(JsonElement parameters)
    {
        var id = parameters.TryGetProperty("stepId", out var given) ? given.GetString() : null;
        var run = _session.Run ?? throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get("Run.NotLoaded"));
        if (!run.Steps.Any(step => string.Equals(step.Id, id, StringComparison.Ordinal)))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get("Run.UnknownStep"));
        }

        if (!_session.SelectLook(id ?? string.Empty))
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get("Run.NotALookingStep"));
        }

        await PushLookAsync();
        return Snapshot();
    }

    private Task<object?> Unload(JsonElement parameters)
    {
        _session.Unload();
        return Task.FromResult<object?>(Snapshot());
    }

    private Task<object?> Breakpoint(JsonElement parameters)
    {
        var id = parameters.TryGetProperty("id", out var given) ? given.GetString() : null;
        var row = _session.Run?.Steps.FirstOrDefault(step =>
            string.Equals(step.Id, id, StringComparison.Ordinal));
        if (row is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get("Run.UnknownStep"));
        }

        _session.Run!.ToggleBreakpointCommand.Execute(row);
        return Task.FromResult<object?>(Snapshot());
    }

    private Task<object?> BreakpointRule(JsonElement parameters)
    {
        var id = parameters.TryGetProperty("id", out var named) ? named.GetString() : null;
        var row = _session.Run?.Steps.FirstOrDefault(step =>
            string.Equals(step.Id, id, StringComparison.Ordinal));
        if (row is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get("Run.UnknownStep"));
        }

        var hits = parameters.TryGetProperty("hitTimes", out var times) && times.TryGetInt32(out var parsed)
            ? parsed
            : 1;
        var expression = parameters.TryGetProperty("expression", out var written)
            ? written.GetString() ?? string.Empty
            : string.Empty;
        _session.Run!.SetBreakpointRule(row, new BreakpointRule
        {
            Expression = expression.Trim().Length == 0 ? Formula.Nothing : Formula.Text(expression),
            HitTimes = Math.Max(1, hits),
        });
        return Task.FromResult<object?>(Snapshot());
    }

    private Task<object?> Argument(JsonElement parameters)
    {
        var name = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        var row = _session.Run?.Arguments.FirstOrDefault(argument =>
            string.Equals(argument.Name, name, StringComparison.Ordinal));
        if (row is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get("Run.UnknownArgument"));
        }

        var value = parameters.TryGetProperty("value", out var written) ? written.GetString() ?? string.Empty : string.Empty;
        row.Value = value;
        row.Given = value.Length > 0;
        return Task.FromResult<object?>(Snapshot());
    }

    private Task<object?> Answer(JsonElement parameters)
    {
        var kind = parameters.TryGetProperty("kind", out var named) ? named.GetString() : null;
        var choice = parameters.TryGetProperty("choice", out var picked) ? picked.GetString() : null;
        var run = _session.Run ?? throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get("Run.NotLoaded"));

        if (kind == "user")
        {
            if (parameters.TryGetProperty("text", out var text) && text.GetString() is { } written)
            {
                run.AnswerText = written;
            }

            run.AnswerUserCommand.Execute(choice ?? "cancel");
        }
        else
        {
            run.AnswerCommand.Execute(choice ?? "stop");
        }

        return Task.FromResult<object?>(Snapshot());
    }
}
