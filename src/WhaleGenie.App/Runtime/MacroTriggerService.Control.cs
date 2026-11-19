using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

// Starting and stopping: what is running, what is waiting, and one pass at a time.
public sealed partial class MacroTriggerService
{
    /// <summary>
    /// One pass of the watches that share a clock. Called by the timer, and by checks, which need a
    /// pass to happen when they say so rather than when a clock says so.
    /// </summary>
    internal void Look() => _hub.Look(DateTime.Now);

    /// <summary>The programs, on the clock of their own.</summary>
    internal void LookAtPrograms() => _hub.Programs(DateTime.Now);

    /// <summary>The controller, on the clock of its own.</summary>
    internal void LookAtGamepad() => _hub.Pad(DateTime.Now);

    public bool IsRunning(string macroName)
    {
        lock (_gate)
        {
            return _running.ContainsKey(macroName.Trim());
        }
    }

    /// <summary>Stops a macro the trigger started.</summary>
    public void Stop(string macroName)
    {
        CancellationTokenSource? cancellation;
        lock (_gate)
        {
            if (!_running.Remove(macroName.Trim(), out var slot))
            {
                return;
            }

            cancellation = slot.Cancellation;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run finished on its own between the two lines; there is nothing to stop.
        }

        StatusChanged?.Invoke();
    }

    /// <summary>Stops everything the trigger started, which is what switching the system off does.</summary>
    public void StopAll()
    {
        List<CancellationTokenSource> cancellations;
        lock (_gate)
        {
            cancellations = [.. _running.Values.Select(slot => slot.Cancellation)];
            _running.Clear();
        }

        _hub.Reset();

        foreach (var cancellation in cancellations)
        {
            try
            {
                cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already finished.
            }
        }

        StatusChanged?.Invoke();
    }

    /// <summary>
    /// Arms a macro again after it was disarmed and re-armed, so a "trigger once" macro can be
    /// used a second time without reloading the project.
    /// </summary>
    public void Rearm(string macroName)
    {
        var name = macroName.Trim();
        var ids = _hub.Plan.Entries
            .Where(trigger => string.Equals(trigger.Macro.Name, name, StringComparison.OrdinalIgnoreCase))
            .Select(trigger => trigger.Id)
            .ToList();

        _hub.State.Rearm(ids);
        _hub.RunEnded(name);
    }

    /// <summary>Runs one macro once for a floating button or menu selection.</summary>
    public void RunOnce(MacroItem macro)
    {
        if (macro.IsEnabled)
        {
            Start(MacroRef.Of(macro), repeating: false);
        }
    }

    /// <summary>
    /// Whether the menu launcher asked for the menu. Used by the checks that drive a key; a match
    /// asks for the menu exactly as a real press would, so the window hears it too.
    /// </summary>
    internal bool RequestMenuForCheck(string binding)
    {
        if (!_hub.Menu(binding))
        {
            return false;
        }

        MenuRequested?.Invoke();
        return true;
    }
}
