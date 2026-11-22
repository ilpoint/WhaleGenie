using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// What each step looked at: keeping the pictures a run took, and letting go of them.
public partial class RunViewModel
{
    /// <summary>
    /// Takes what a step saw and puts it on that step's row. Called from the thread the run is on,
    /// so the picture is handed to the window's own thread before anything is done with it.
    /// </summary>
    public void Look(StepLook look) => _ui.Post(() => Keep(look));

    /// <summary>
    /// Takes what a step came to — how long it took, how it ended, why if it failed, and everything
    /// it worked out whether or not the macro keeps any of it — and puts it on that step's row.
    /// Called from the thread the run is on, so it is handed to the window's own thread first.
    /// </summary>
    public void Ran(StepRun run) => _ui.Post(() => KeepRun(run));

    /// <summary>Fills in the panel under a step's row with what the step just came to.</summary>
    private void KeepRun(StepRun run)
    {
        if (Steps.FirstOrDefault(step => step.Id == run.StepId) is { } row)
        {
            row.Took(run);
        }
    }

    public event Action<StepLook>? LookRequested;

    /// <summary>
    /// Raised when the run moves to another step, with that step's row, or null when it is not
    /// standing in front of one any more. The list follows the run rather than making the person
    /// hunt for the row: a long macro says nothing about where it is if the row is below the fold.
    /// </summary>
    public event Action<RunStepViewModel?>? CurrentChanged;

    /// <summary>
    /// Where the window the run is reading right now sits, or null when it is reading nothing: a
    /// step that does not look at the screen, the desktop, or a window that has gone. A step's own
    /// picture says what was found, and this says where the engine was looking for it — which is the
    /// answer when a step looked in the wrong window and found nothing.
    /// </summary>
    public event Action<FramePlace?>? WindowRead;

    /// <summary>
    /// Told by the engine which window it is about to read, and where that window is, before every
    /// picture it takes: the engine looked the window up for this look, so a frame put up from what
    /// is handed over here follows the window rather than staying where it was when the step started.
    /// The call comes from the thread the run is on, so the frame is drawn on the thread that owns
    /// windows.
    /// </summary>
    public void Reading(WindowInfo? window) => _ui.Post(() => ReadingAt(window));

    /// <summary>
    /// Moves the frame to the window being read. A reading that lands after the run has stopped
    /// takes the frame down rather than putting one up: a frame left over a window nothing is
    /// reading any more says something untrue.
    /// </summary>
    private void ReadingAt(WindowInfo? window)
    {
        if (!IsBusy || window is null)
        {
            WindowRead?.Invoke(null);
            return;
        }

        WindowRead?.Invoke(FramePlace.Of(window));
    }

    /// <summary>The whole of what a step last saw, or null when it has looked at nothing.</summary>
    public StepLook? LookOf(string stepId)
        => _looks.LastOrDefault(kept => kept.StepId == stepId).Look;

    private void Keep(StepLook look)
    {
        var bytes = (long)look.Frame.Width * look.Frame.Height * 4
            + (look.Needle is { } needle ? (long)needle.Width * needle.Height * 4 : 0);

        _looks.Add((look.StepId, look, bytes));

        // Newest first out of the door: the picture a person wants is the one the run has just
        // made, and a macro that loops over a search makes one every time round.
        while (_looks.Count > KeptLooks || _looks.Sum(kept => kept.Bytes) > KeptBytes)
        {
            var gone = _looks[0].StepId;
            _looks.RemoveAt(0);
            Forget(gone);
        }

        if (Steps.FirstOrDefault(step => step.Id == look.StepId) is { } row)
        {
            row.HasLook = true;
        }
    }

    /// <summary>
    /// Takes a step's picture off its row, once the run has let that picture go. Keeping the
    /// </summary>
    private void Forget(string stepId)
    {
        if (_looks.Any(kept => kept.StepId == stepId))
        {
            return;
        }

        if (Steps.FirstOrDefault(step => step.Id == stepId) is { } row)
        {
            row.HasLook = false;
        }
    }

    /// <summary>Lets go of the pictures the run kept, which the window does when it goes away.</summary>
    public void ReleaseLooks()
    {
        foreach (var step in Steps)
        {
            step.HasLook = false;
        }

        _looks.Clear();
    }

    /// <summary>Asks for the picture of one step to be opened.</summary>
    [RelayCommand]
    private void OpenLook(RunStepViewModel? step)
    {
        if (step is not null && LookOf(step.Id) is { } look)
        {
            LookRequested?.Invoke(look);
        }
    }

    private string? CurrentId
    {
        get => _currentId;
        set
        {
            if (_currentId == value)
            {
                return;
            }

            _currentId = value;
            RunStepViewModel? current = null;
            foreach (var row in Steps)
            {
                row.IsCurrent = string.Equals(row.Id, value, StringComparison.Ordinal);
                if (row.IsCurrent)
                {
                    current = row;
                    RevealAncestors(row);
                }
            }

            CurrentChanged?.Invoke(current);
        }
    }

    private void Release()
    {
        var gate = _gate;
        _gate = null;
        gate?.TrySetResult(true);
    }
}
