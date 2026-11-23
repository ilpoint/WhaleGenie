using System;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Bridge;

// What the page sees of a run: the full state on a query and the differences pushed as it moves.
internal sealed partial class RunBridge
{
    private int _sentLog;
    private string _sentSteps = string.Empty;
    private RunLook? _sentLook;

    /// <summary>The full state for a query or command reply; it resets the page cursors.</summary>
    private RunState Snapshot()
    {
        var state = _session.Snapshot();
        _sentLog = state.Log.Count;
        _sentSteps = RunStateProjection.StepsSignature(state.Steps ?? []);
        _sentLook = state.Look;
        return state;
    }

    /// <summary>Starts the clock that pushes while a run is live; a second call keeps the same one.</summary>
    private void StartClock() => _session.StartClock();

    /// <summary>Pushes whenever the clock ticks or a step opens the picture it looked at.</summary>
    private void Watch()
    {
        _session.LookChanged += look => { _ = PushAsync(); };
        _session.Ticked += Tick;
    }

    private void Tick()
    {
        _ = PushAsync();
    }

    /// <summary>Pushes what changed since the last one: steps, then new log lines, then the look.</summary>
    private async Task PushAsync()
    {
        if (_server is null || _session.Run is not { } run)
        {
            return;
        }

        var full = _session.Snapshot();
        var stepsSignature = RunStateProjection.StepsSignature(full.Steps ?? []);
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

        if (!run.IsBusy && !run.IsPaused && !run.IsAsking && !run.IsAskingUser)
        {
            _session.StopClock();
        }
    }
}
