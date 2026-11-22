using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Threading;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Bridge;

/// <summary>
/// The web run panel's model: one debugger session at a time, built on the same RunViewModel the
/// native window uses. Steps, log lines, arguments and the four run commands are snapshotted; the
/// page never owns a second run.
/// </summary>
internal sealed class RunBridge(ProjectWorkspace workspace, TriggerRuntime runtime)
    : IBridgeHandler, IDisposable
{
    private BridgeServer? _server;
    private RunViewModel? _run;
    private DispatcherTimer? _timer;
    private RunLook? _look;

    public void Register(BridgeServer server)
    {
        _server = server;
        server.Register("run.state", _ => Task.FromResult<object?>(Snapshot()));
        server.Register("run.unload", Unload);
        server.Register("run.load", LoadAsync);
        server.Register("run.command", CommandAsync);
        server.Register("run.breakpoint", Breakpoint);
        server.Register("run.breakpointRule", BreakpointRule);
        server.Register("run.argument", Argument);
        server.Register("run.answer", Answer);
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

        _run ??= NewRun();
        await _run.DebugAsync(macro);
        StartClock();
        await PushAsync();
        return Snapshot();
    }

    private RunViewModel NewRun()
    {
        var run = new RunViewModel([], runtime.Devices, listed: [.. workspace.Macros],
            triggers: runtime.Triggers, variables: workspace.Variables);
        run.LookRequested += look =>
        {
            _look = new RunLook(look.StepId, look.StepType, look.Looking, ToDataUrl(look.Frame));
            _ = PushAsync();
        };
        return run;
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

        StartClock();
        await PushAsync();
        return Snapshot();
    }

    /// <summary>Leaving the panel ends the debugger session: stop the clock and release the run.</summary>
    private Task<object?> Unload(JsonElement parameters)
    {
        _timer?.Stop();
        _timer = null;
        if (_run?.IsBusy == true)
        {
            _run.StopCommand.Execute(null);
        }

        _run = null;
        _look = null;
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

    private Task<object?> Argument(JsonElement parameters)
    {
        var name = parameters.TryGetProperty("name", out var named) ? named.GetString() : null;
        var row = _run?.Arguments.FirstOrDefault(argument =>
            string.Equals(argument.Name, name, StringComparison.Ordinal));
        if (row is null)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Run.UnknownArgument"));
        }

        var value = parameters.TryGetProperty("value", out var written)
            ? written.GetString() ?? string.Empty
            : string.Empty;
        row.Value = value;
        row.Given = value.Length > 0;
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
        if (!_run.IsBusy && !_run.IsPaused && !_run.IsAsking && !_run.IsAskingUser)
        {
            _timer?.Stop();
            _timer = null;
        }
    }

    private RunState Snapshot() => new(
        _run?.Debugged ?? string.Empty,
        _run?.Status ?? string.Empty,
        _run?.StatusKindValue.ToString() ?? string.Empty,
        _run?.IsBusy ?? false,
        _run?.IsPaused ?? false,
        _run?.RunCommand.CanExecute(null) ?? false,
        _run?.StepCommand.CanExecute(null) ?? false,
        _run?.StopCommand.CanExecute(null) ?? false,
        _run?.Question ?? string.Empty,
        _run?.WantsTypedAnswer ?? false,
        _run?.AnswerText ?? string.Empty,
        _run?.IsAskingUser ?? false,
        _run?.IsAsking ?? false,
        _run?.Arguments.Select(argument => new RunArgument(
            argument.Name,
            argument.Label,
            argument.Hint,
            argument.Required,
            argument.Value,
            argument.Given,
            argument.IsMissing)).ToArray() ?? Array.Empty<RunArgument>(),
        _look,
        _run?.Globals.Select(global => new RunGlobal(
            global.Name,
            global.LocalType,
            global.CurrentValue,
            global.DefaultValue)).ToArray() ?? Array.Empty<RunGlobal>(),
        _run?.LocalVariables.SelectMany(group => group.Variables.Select(variable => new RunLocal(
            variable.Name,
            group.MacroName,
            variable.Type.ToString()))).ToArray() ?? Array.Empty<RunLocal>(),
        _run?.Steps.Select(step => new RunStep(
            step.Id,
            step.Label,
            step.Detail,
            step.Depth,
            step.IsShown,
            step.IsContainer,
            step.IsCurrent,
            step.HasBreakpoint,
            step.CanBreak,
            step.TimeNote,
            step.FailureNote,
            step.ConditionNote,
            _run.BreakpointRuleOf(step).HitTimes,
            _run.BreakpointRuleOf(step).WaitsForExpression
                ? ExpressionPhrase.Display(_run.BreakpointRuleOf(step).Expression)
                : string.Empty)).ToArray() ?? Array.Empty<RunStep>(),
        _run?.Lines.Select(line => new RunLine(
            line.Time,
            line.Level.ToString(),
            line.Text)).ToArray() ?? Array.Empty<RunLine>());

    /// <summary>A BMP data URL: no image library on the bridge thread, and browsers decode it.</summary>
    private static string ToDataUrl(ImageFrame frame)
    {
        var width = frame.Width;
        var height = frame.Height;
        var pixels = frame.Bgra;
        var stride = width * 4;
        var size = 54 + stride * height;
        var bytes = new byte[size];
        bytes[0] = (byte)'B';
        bytes[1] = (byte)'M';
        WriteInt32(bytes, 2, size);
        WriteInt32(bytes, 10, 54);
        WriteInt32(bytes, 14, 40);
        WriteInt32(bytes, 18, width);
        WriteInt32(bytes, 22, height);
        WriteInt16(bytes, 26, 1);
        WriteInt16(bytes, 28, 32);
        WriteInt32(bytes, 34, stride * height);
        for (var y = 0; y < height; y++)
        {
            Buffer.BlockCopy(pixels, y * stride, bytes, 54 + (height - 1 - y) * stride, stride);
        }

        return "data:image/bmp;base64," + Convert.ToBase64String(bytes);
    }

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
        bytes[offset + 2] = (byte)(value >> 16);
        bytes[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteInt16(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)value;
        bytes[offset + 1] = (byte)(value >> 8);
    }
}
