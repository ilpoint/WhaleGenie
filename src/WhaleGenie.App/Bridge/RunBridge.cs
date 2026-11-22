using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
internal sealed class RunBridge(ProjectWorkspace workspace, TriggerRuntime runtime,
    IUiPost? ui = null) : IBridgeHandler, IDisposable
{
    private readonly IUiPost _ui = ui ?? InlineUiPost.Instance;
    private BridgeServer? _server;
    private RunViewModel? _run;
    private Timer? _timer;
    private RunLook? _look;
    private int _sentLog;
    private string _sentSteps = string.Empty;
    private RunLook? _sentLook;

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

    public void Dispose() => _timer?.Dispose();

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
            triggers: runtime.Triggers, variables: workspace.Variables, ui: _ui);
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
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
        if (_run?.IsBusy == true)
        {
            _run.StopCommand.Execute(null);
        }

        _run = null;
        _look = null;
        _sentLog = 0;
        _sentSteps = string.Empty;
        _sentLook = null;
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

        _timer = new Timer(_ => _ui.Post(() => _ = PushAsync()), null,
            TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    private async Task PushAsync()
    {
        if (_server is null || _run is null)
        {
            return;
        }

        var full = SnapshotCore();
        var stepsSignature = StepsSignature(full.Steps ?? []);
        if (!string.Equals(stepsSignature, _sentSteps, StringComparison.Ordinal))
        {
            await _server.PushAsync("run.steps", full.Steps);
            _sentSteps = stepsSignature;
        }

        var resetLog = _sentLog > full.Log.Count;
        var from = resetLog ? 0 : _sentLog;
        RunLine[] log = [.. full.Log.Skip(from)];
        if (log.Length > 0 || resetLog)
        {
            await _server.PushAsync("run.log", new RunLogBatch(from, log), BridgePushMode.All);
            _sentLog = from + log.Length;
        }

        if (!Equals(full.Look, _sentLook))
        {
            await _server.PushAsync("run.look", full.Look);
            _sentLook = full.Look;
        }

        await _server.PushAsync("run.changed", full with
        {
            Steps = null,
            Log = [],
            LogFrom = _sentLog,
            Look = null,
        });

        if (!_run.IsBusy && !_run.IsPaused && !_run.IsAsking && !_run.IsAskingUser)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>The full state for a query or command reply; it resets the page cursors.</summary>
    private RunState Snapshot()
    {
        var state = SnapshotCore();
        _sentLog = state.Log.Count;
        _sentSteps = StepsSignature(state.Steps ?? []);
        _sentLook = state.Look;
        return state;
    }

    /// <summary>Changes that the page has to redraw; an unchanged tree is left off the push.</summary>
    private static string StepsSignature(IReadOnlyList<RunStep> steps)
    {
        var text = new System.Text.StringBuilder();
        foreach (var step in steps)
        {
            text.Append(step.Id).Append('|')
                .Append(step.Label).Append('|')
                .Append(step.Detail).Append('|')
                .Append(step.Depth).Append('|')
                .Append(step.Shown ? '1' : '0')
                .Append(step.Container ? '1' : '0')
                .Append(step.Current ? '1' : '0')
                .Append(step.Breakpoint ? '1' : '0')
                .Append(step.CanBreak ? '1' : '0')
                .Append(step.Time).Append('|')
                .Append(step.Failure).Append('|')
                .Append(step.Condition).Append('|')
                .Append(step.HitTimes).Append('|')
                .Append(step.Expression).Append(';');
        }

        return text.ToString();
    }

    private RunState SnapshotCore() => new(
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

    /// <summary>The longest side a look frame keeps; debugger frames do not need native detail.</summary>
    private const int LookMaxSide = 320;

    /// <summary>
    /// A BMP data URL scaled to fit <see cref="LookMaxSide"/>. No image library on the bridge
    /// thread, and browsers decode BMP; nearest-neighbour is enough for a debugger thumbnail.
    /// </summary>
    internal static string ToDataUrl(ImageFrame frame)
    {
        var sourceWidth = frame.Width;
        var sourceHeight = frame.Height;
        var scale = Math.Min(1.0, LookMaxSide / (double)Math.Max(sourceWidth, sourceHeight));
        var width = Math.Max(1, (int)Math.Round(sourceWidth * scale));
        var height = Math.Max(1, (int)Math.Round(sourceHeight * scale));
        var pixels = frame.Bgra;
        if (width != sourceWidth || height != sourceHeight)
        {
            var scaled = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                var sourceY = Math.Min(sourceHeight - 1, y * sourceHeight / height);
                for (var x = 0; x < width; x++)
                {
                    var sourceX = Math.Min(sourceWidth - 1, x * sourceWidth / width);
                    Buffer.BlockCopy(pixels, (sourceY * sourceWidth + sourceX) * 4,
                        scaled, (y * width + x) * 4, 4);
                }
            }

            pixels = scaled;
        }

        return EncodeBmp(pixels, width, height);
    }

    private static string EncodeBmp(byte[] pixels, int width, int height)
    {
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
