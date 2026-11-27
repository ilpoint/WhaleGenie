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
            await _server.PushAsync("run.steps", full.Steps, BridgePushMode.Latest, BridgeSurface.Workbench);
            _sentSteps = stepsSignature;
        }

        var resetLog = _sentLog > full.Log.Count;
        var from = resetLog ? 0 : _sentLog;
        RunLine[] log = [.. full.Log.Skip(from)];
        if (log.Length > 0 || resetLog)
        {
            await _server.PushAsync("run.log", new RunLogBatch(from, log), BridgePushMode.All, BridgeSurface.Workbench);
            _sentLog = from + log.Length;
        }

        if (!Equals(full.Look, _sentLook))
        {
            await _server.PushAsync("run.look", full.Look, BridgePushMode.Latest, BridgeSurface.Workbench);
            _sentLook = full.Look;
        }

        await PushChangedAsync(full);

        if (!run.IsBusy && !run.IsPaused && !run.IsAsking && !run.IsAskingUser)
        {
            _session.StopClock();
        }
    }

    /// <summary>
    /// Pushes only the frame the panel shows, which is the whole of what switching from one kept
    /// picture to another changes. The rows and the log are untouched by it, so they are neither
    /// looked at again nor sent.
    /// </summary>
    private async Task PushLookAsync()
    {
        if (_server is null)
        {
            return;
        }

        var full = _session.Snapshot();
        await _server.PushAsync("run.look", full.Look, BridgePushMode.Latest, BridgeSurface.Workbench);
        _sentLook = full.Look;
        await PushChangedAsync(full);
    }

    /// <summary>
    /// The parts of the state a push does not carry: the rows, the log and the frames come as their
    /// own deltas, and a full state is only ever a reply. The pictures are the largest thing the
    /// state holds, so a clock tick that resent them would put a run's every frame on the pipe
    /// several times a second; the page keeps the list it was last handed instead.
    /// </summary>
    private Task PushChangedAsync(RunState full) => _server!.PushAsync("run.changed", full with
    {
        Steps = null,
        Log = [],
        LogFrom = _sentLog,
        Look = null,
        Looks = null,
    }, BridgePushMode.Latest, BridgeSurface.Workbench);
}
