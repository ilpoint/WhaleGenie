using System;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Runtime;

/// <summary>
/// One debugger session as the run panel drives it: the run model, the clock that keeps the panel
/// following it, and the last frame a step opened. The bridge above parses what the page asked for
/// and forwards it here, so the session itself can be read without a page attached.
/// </summary>
internal sealed class RunSession(ProjectWorkspace workspace, TriggerRuntime runtime, IUiPost? ui = null)
    : IDisposable
{
    private readonly IUiPost _ui = ui ?? InlineUiPost.Instance;
    private RunViewModel? _run;
    private Timer? _timer;
    private RunLook? _look;

    /// <summary>The run model this session drives, or null before a macro is loaded and after unload.</summary>
    public RunViewModel? Run => _run;

    /// <summary>The frame the run last asked to open, as the page draws it, or null.</summary>
    public RunLook? Look => _look;

    /// <summary>The frame a step asked to open, raised on the interface thread once it is encoded.</summary>
    public event Action<RunLook>? LookChanged;

    /// <summary>One tick of the clock a live run keeps, raised on the interface thread.</summary>
    public event Action? Ticked;

    /// <summary>True once a macro has been loaded, until the page leaves the panel.</summary>
    public bool IsLoaded => _run is not null;

    /// <summary>Points the session at a macro without running it; the bridge says how to start it.</summary>
    public async Task LoadAsync(MacroItem macro)
    {
        _run ??= NewRun();
        await _run.DebugAsync(macro);
    }

    /// <summary>
    /// Carries out one of the panel's words — run, step, stop, continue or skip — and says whether it
    /// was one of them. Refusing in the page's own error codes belongs to the bridge, so an unknown
    /// word is a plain false here rather than an exception.
    /// </summary>
    public Task<bool> CommandAsync(string command)
    {
        if (_run is null)
        {
            return Task.FromResult(false);
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
                return Task.FromResult(false);
        }

        return Task.FromResult(true);
    }

    /// <summary>Leaving the panel ends the debugger session: stop the clock and release the run.</summary>
    public void Unload()
    {
        StopClock();
        if (_run?.IsBusy == true)
        {
            _run.StopCommand.Execute(null);
        }

        _run = null;
        _look = null;
    }

    /// <summary>The full state for a query or command reply; an unloaded session reads as the empty panel.</summary>
    public RunState Snapshot() => RunStateProjection.Project(_run, _look);

    /// <summary>Starts the clock that makes the panel follow a live run; a second call keeps the same clock.</summary>
    public void StartClock()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = new Timer(_ => _ui.Post(() => Ticked?.Invoke()), null,
            TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    /// <summary>Stops the clock until something starts it again.</summary>
    public void StopClock()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose() => _timer?.Dispose();

    private RunViewModel NewRun()
    {
        var run = new RunViewModel([], runtime.Devices, listed: [.. workspace.Macros],
            triggers: runtime.Triggers, variables: workspace.Variables, ui: _ui);
        run.LookRequested += look =>
        {
            var built = new RunLook(look.StepId, look.StepType, look.Looking,
                FrameDataUrl.ToDataUrl(look.Frame));
            _look = built;
            LookChanged?.Invoke(built);
        };
        return run;
    }
}
