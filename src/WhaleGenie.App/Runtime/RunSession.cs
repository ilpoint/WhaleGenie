using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Runtime;

/// <summary>
/// One debugger session as the run panel drives it: the run model, the clock that keeps the panel
/// following it, and the frames a run looked at. The bridge above parses what the page asked for and
/// forwards it here, so the session itself can be read without a page attached.
/// </summary>
internal sealed class RunSession(ProjectWorkspace workspace, TriggerRuntime runtime, IUiPost? ui = null)
    : IDisposable
{
    private readonly IUiPost _ui = ui ?? InlineUiPost.Instance;
    private readonly LookHistory _looks = new();
    private RunViewModel? _run;
    private Timer? _timer;

    /// <summary>The run model this session drives, or null before a macro is loaded and after unload.</summary>
    public RunViewModel? Run => _run;

    /// <summary>The frame the panel is showing, as the page draws it, or null while it shows none.</summary>
    public RunLook? Look => _looks.Active;

    /// <summary>Every frame the session kept, newest last, which is what the page's history lists.</summary>
    public IReadOnlyList<RunLook> Looks => _looks.Frames;

    /// <summary>The frame a step asked to open, raised on the interface thread once it is encoded.</summary>
    public event Action<RunLook>? LookChanged;

    /// <summary>One tick of the clock a live run keeps, raised on the interface thread.</summary>
    public event Action? Ticked;

    /// <summary>True once a macro has been loaded, until the page leaves the panel.</summary>
    public bool IsLoaded => _run is not null;

    /// <summary>Points the session at a macro without running it; the bridge says how to start it.</summary>
    public async Task LoadAsync(MacroItem macro)
    {
        if (_run is null)
        {
            _run = NewRun();
        }

        RunViewModel run = _run;
        await run.DebugAsync(macro);

        // What the armed triggers are waiting for is read once when the macro is loaded and again on
        // every tick: a macro that was expected to run and did not is answered by that list, and a
        // list read once at load time would be about a moment that has passed.
        run.RefreshTriggers();
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

    /// <summary>
    /// Shows a frame the session already kept, which is what picking one out of the history asks
    /// for. Nothing is run and nothing is read again: the picture is one the run had already taken,
    /// and what changes is which of them the panel draws.
    /// </summary>
    public bool SelectLook(string stepId) => _looks.Select(stepId);

    /// <summary>Leaving the panel ends the debugger session: stop the clock and release the run.</summary>
    public void Unload()
    {
        StopClock();
        if (_run?.IsBusy == true)
        {
            _run.StopCommand.Execute(null);
        }

        _run = null;
        _looks.Clear();
    }

    /// <summary>The full state for a query or command reply; an unloaded session reads as the empty panel.</summary>
    public RunState Snapshot() => RunStateProjection.Project(_run, _looks.Active, _looks.Frames);

    /// <summary>Starts the clock that makes the panel follow a live run; a second call keeps the same clock.</summary>
    public void StartClock()
    {
        if (_timer is not null)
        {
            return;
        }

        _timer = new Timer(_ => _ui.Post(() =>
        {
            _run?.RefreshTriggers();
            Ticked?.Invoke();
        }), null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
    }

    /// <summary>Stops the clock until something starts it again.</summary>
    public void StopClock()
    {
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// Ends the session for good. The frames go with it: a picture is the largest thing a session
    /// holds, and a deleted session has nobody left to show it to.
    /// </summary>
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
        _looks.Clear();
    }

    private RunViewModel NewRun()
    {
        var run = new RunViewModel([], runtime.Devices, listed: [.. workspace.Macros],
            triggers: runtime.Triggers, variables: workspace.Variables, ui: _ui);
        run.LookRequested += look =>
        {
            var built = new RunLook(look.StepId, look.StepType, look.Looking,
                FrameDataUrl.ToDataUrl(look.Frame));
            _looks.Keep(built);
            LookChanged?.Invoke(built);
        };
        return run;
    }
}

/// <summary>
/// The frames one session kept, newest last, and the one the panel is showing. A run that loops over
/// a search makes a picture every time round, so only the newest few are held; a step that looks
/// again replaces its own earlier picture rather than adding a second one, because the thumbnail
/// stands for that step rather than for one try at it.
/// </summary>
internal sealed class LookHistory
{
    /// <summary>
    /// How many frames the session keeps. The page grows its own list by the same amount while a
    /// run adds pictures between two replies, so the two lists agree until the next full state.
    /// </summary>
    public const int Kept = 20;

    private readonly List<RunLook> _looks = [];
    private RunLook? _active;

    /// <summary>The frames still held, newest last.</summary>
    public IReadOnlyList<RunLook> Frames => _looks;

    /// <summary>The frame the panel is showing, or null while it shows none.</summary>
    public RunLook? Active => _active;

    /// <summary>Puts a frame the run just took on the list and shows it.</summary>
    public void Keep(RunLook look)
    {
        _looks.RemoveAll(kept => string.Equals(kept.StepId, look.StepId, StringComparison.Ordinal));
        _looks.Add(look);
        if (_looks.Count > Kept)
        {
            _looks.RemoveRange(0, _looks.Count - Kept);
        }

        _active = look;
    }

    /// <summary>Shows a frame already kept; false when this step left none behind.</summary>
    public bool Select(string stepId)
    {
        var kept = _looks.FirstOrDefault(look => string.Equals(look.StepId, stepId, StringComparison.Ordinal));
        if (kept is null)
        {
            return false;
        }

        _active = kept;
        return true;
    }

    /// <summary>Lets go of every frame, which the session does when it ends.</summary>
    public void Clear()
    {
        _looks.Clear();
        _active = null;
    }
}
