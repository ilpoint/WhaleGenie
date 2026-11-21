using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Bridge;

/// <summary>
/// The web run panel's model: one debugger session at a time, built on the same RunViewModel the
/// native window uses. Steps, log lines and the four run commands are snapshotted; the page never
/// owns a second run.
/// </summary>
internal sealed class RunBridge(ProjectWorkspace workspace, TriggerRuntime runtime)
    : IBridgeHandler, IDisposable
{
    private BridgeServer? _server;
    private RunViewModel? _run;
    private DispatcherTimer? _timer;

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("run.state", _ => Task.FromResult<object?>(Snapshot()));
        server.Register("run.load", LoadAsync);
        server.Register("run.command", CommandAsync);
        server.Register("run.breakpoint", Breakpoint);
        server.Register("run.answer", Answer);
        server.Register("run.breakpointRule", BreakpointRule);
    }

    public void Dispose() => _timer?.Stop();

    private async Task<object?> LoadAsync(JsonElement parameters)
    {
        var name = parameters.TryGetProperty("name", out var given) ? given.GetString() : null;
        var macro = workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (macro is null)
        {
            throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                Strings.Format("Bridge.NoSuchMacro", name ?? string.Empty));
        }

        _run ??= new RunViewModel([], runtime.Devices, listed: [.. workspace.Macros],
            triggers: runtime.Triggers, variables: workspace.Variables);
        await _run.DebugAsync(macro);
        StartClock();
        await PushAsync();
        return Snapshot();
    }

    private async Task<object?> CommandAsync(JsonElement parameters)
    {
        var command = parameters.TryGetProperty("command", out var named) ? named.GetString() : null;
        if (_run is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Run.NotLoaded"));
        }

        switch (command)
        {
            case "run":
                _run.RunCommand.Execute(null);
                break;
            case "step":
                _run.StepCommand.Execute(null);
                break;
            case "stop":
                _run.StopCommand.Execute(null);
                break;
            case "continue":
                _run.ContinueCommand.Execute(null);
                break;
            case "skip":
                _run.SkipStepCommand.Execute(null);
                break;
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Run.UnknownCommand"));
        }

        await PushAsync();
        return Snapshot();
    }

    private Task<object?> BreakpointRule(JsonElement parameters)
    {
        var id = parameters.TryGetProperty("id", out var named) ? named.GetString() : null;
        var row = _run?.Steps.FirstOrDefault(step =>
            string.Equals(step.Id, id, StringComparison.Ordinal));
        if (row is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Run.UnknownStep"));
        }

        var hits = parameters.TryGetProperty("hitTimes", out var times) && times.TryGetInt32(out var parsed)
            ? parsed
            : 1;
        var expression = parameters.TryGetProperty("expression", out var written)
            ? written.GetString() ?? string.Empty
            : string.Empty;
        _run!.SetBreakpointRule(row, new BreakpointRule
        {
            Expression = expression.Trim().Length == 0 ? Formula.Nothing : Formula.Text(expression),
            HitTimes = Math.Max(1, hits),
        });
        return Task.FromResult<object?>(Snapshot());
    }

    private Task<object?> Answer(JsonElement parameters)
    {
        var kind = parameters.TryGetProperty("kind", out var named) ? named.GetString() : null;
        var choice = parameters.TryGetProperty("choice", out var picked) ? picked.GetString() : null;
        if (_run is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Run.NotLoaded"));
        }

        if (kind == "user")
        {
            if (parameters.TryGetProperty("text", out var text) && text.GetString() is { } written)
            {
                _run.AnswerText = written;
            }

            _run.AnswerUserCommand.Execute(choice ?? "cancel");
        }
        else
        {
            _run.AnswerCommand.Execute(choice ?? "stop");
        }

        return Task.FromResult<object?>(Snapshot());
    }

    private Task<object?> Breakpoint(JsonElement parameters)
    {
        var id = parameters.TryGetProperty("id", out var given) ? given.GetString() : null;
        var row = _run?.Steps.FirstOrDefault(step =>
            string.Equals(step.Id, id, StringComparison.Ordinal));
        if (row is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Run.UnknownStep"));
        }

        _run!.ToggleBreakpointCommand.Execute(row);
        return Task.FromResult<object?>(Snapshot());
    }

    private void StartClock()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _timer.Tick += (_, _) => _ = PushAsync();
        _timer.Start();
    }

    private async Task PushAsync()
    {
        if (_server is null || _run is null)
        {
            return;
        }

        await _server.PushAsync("run.changed", Snapshot());
    }

    private object Snapshot()
        => new
        {
            loaded = _run?.Debugged ?? string.Empty,
            status = _run?.Status ?? string.Empty,
            statusKind = _run?.StatusKindValue.ToString() ?? string.Empty,
            busy = _run?.IsBusy ?? false,
            paused = _run?.IsPaused ?? false,
            canRun = _run?.RunCommand.CanExecute(null) ?? false,
            canStep = _run?.StepCommand.CanExecute(null) ?? false,
            canStop = _run?.StopCommand.CanExecute(null) ?? false,
            steps = _run?.Steps.Select(step => new
            {
                id = step.Id,
                label = step.Label,
                detail = step.Detail,
                depth = step.Depth,
                shown = step.IsShown,
                container = step.IsContainer,
                current = step.IsCurrent,
                breakpoint = step.HasBreakpoint,
                canBreak = step.CanBreak,
                time = step.TimeNote,
                failure = step.FailureNote,
                condition = step.ConditionNote,
                hitTimes = _run.BreakpointRuleOf(step).HitTimes,
                expression = _run.BreakpointRuleOf(step).WaitsForExpression
                    ? ExpressionPhrase.Display(_run.BreakpointRuleOf(step).Expression)
                    : string.Empty,
            }),
            question = _run?.Question ?? string.Empty,
            wantsTypedAnswer = _run?.WantsTypedAnswer ?? false,
            answerText = _run?.AnswerText ?? string.Empty,
            isAskingUser = _run?.IsAskingUser ?? false,
            isAsking = _run?.IsAsking ?? false,
            globals = _run?.Globals.Select(global => new
            {
                name = global.Name,
                type = global.LocalType,
                value = global.CurrentValue,
                defaultValue = global.DefaultValue,
            }),
            locals = _run?.LocalVariables.SelectMany(group => group.Variables.Select(variable => new
            {
                name = variable.Name,
                macro = group.MacroName,
                type = variable.Type.ToString(),
            })),
            log = _run?.Lines.Select(line => new
            {
                time = line.Time,
                level = line.Level.ToString(),
                text = line.Text,
            }),
        };
}
