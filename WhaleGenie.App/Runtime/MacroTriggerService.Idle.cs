using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;


namespace WhaleGenie.Runtime;

// Triggers that watch the machine being left alone, and a watched colour.
public sealed partial class MacroTriggerService
{
    /// <summary>
    /// Runs the macros waiting for the machine to be left alone. One run per quiet spell: once it
    /// has run, the macro waits for input to arrive and the machine to fall quiet again, so a
    /// machine left alone overnight does not set the same macro off all night.
    /// </summary>
    /// <remarks>
    /// Input anywhere counts, including what a macro sends itself: a macro that moves the mouse
    /// counts as having used the machine.
    /// </remarks>
    private void PollIdle(TriggerRuntimeSnapshot triggers)
    {
        var idle = IdleWatch.Since();

        foreach (var entry in triggers.Entries)
        {
            if (!entry.IsEnabled || entry.Kind != TriggerKind.Idle
                || MacroFor(entry) is not { IsEnabled: true } macro)
            {
                continue;
            }

            if (idle < TimeSpan.FromSeconds(Math.Max(1, entry.IdleSeconds)))
            {
                lock (_gate)
                {
                    _idleDone.Remove(entry);
                }

                continue;
            }

            bool first;
            lock (_gate)
            {
                first = _idleDone.Add(entry);
            }

            if (first && !(entry.TriggerOnce && IsSpent(entry)))
            {
                StartFrom(entry, macro, repeating: false);
            }
        }
    }

    /// <summary>True when a "trigger once" macro has already had its turn.</summary>
    private bool IsSpent(TriggerEntry entry)
    {
        lock (_gate)
        {
            return _spent.Contains(entry);
        }
    }

    /// <summary>Forgets what was watched for macros that are gone, switched off or changed.</summary>
    private void PruneWatches(TriggerRuntimeSnapshot triggers)
    {
        lock (_gate)
        {
            if (_colourTriggered.Count == 0 && _colourHeld.Count == 0 && _colourStopping.Count == 0
                && _scheduleDue.Count == 0 && _watches.Count == 0 && _programsRunning.Count == 0
                && _windows.Count == 0 && _idleDone.Count == 0)
            {
                return;
            }

            var tracked = new HashSet<TriggerEntry>(_colourTriggered);
            tracked.UnionWith(_scheduleDue.Keys);
            tracked.UnionWith(_programsRunning.Macros.OfType<TriggerEntry>());
            tracked.UnionWith(_windows.Macros.OfType<TriggerEntry>());
            tracked.UnionWith(_idleDone);

            var current = new HashSet<TriggerEntry>(triggers.Entries);
            foreach (var item in tracked)
            {
                if (!current.Contains(item) || !item.IsEnabled || MacroFor(item) is not { IsEnabled: true })
                {
                    _colourTriggered.Remove(item);
                    if (MacroFor(item) is { } gone)
                    {
                        _colourHeld.Remove(gone);
                        _colourStopping.Remove(gone);
                    }
                    _scheduleDue.Remove(item);
                    _programsRunning.Forget(item);
                    _windows.Forget(item);
                    _idleDone.Remove(item);
                    continue;
                }

                // A macro moved to another kind of trigger stands down like one that was removed,
                // so moving it back starts its watch over.
                if (item.Kind != TriggerKind.Color)
                {
                    _colourTriggered.Remove(item);
                    if (MacroFor(item) is { } macro)
                    {
                        _colourHeld.Remove(macro);
                        _colourStopping.Remove(macro);
                    }
                }

                if (item.Kind != TriggerKind.Timer)
                {
                    _scheduleDue.Remove(item);
                }

                if (item.Kind != TriggerKind.Process)
                {
                    _programsRunning.Forget(item);
                }

                if (item.Kind != TriggerKind.Window)
                {
                    _windows.Forget(item);
                }

                if (item.Kind != TriggerKind.Idle)
                {
                    _idleDone.Remove(item);
                }
            }

            // A watch holds a listener of its own, so one that is no longer wanted has to be let
            // go of rather than only forgotten.
            foreach (var (item, watch) in _watches.ToList())
            {
                if (!current.Contains(item) || !item.IsEnabled
                    || item.Kind != TriggerKind.File)
                {
                    watch.Dispose();
                    _watches.Remove(item);
                }
            }
        }
    }

    private bool ColourMatches(TriggerEntry entry)
    {
        var x = entry.ColorPositionX;
        var y = entry.ColorPositionY;

        if (entry.UseCursorPosition)
        {
            var cursor = _devices.Input.Cursor;
            x = cursor.X;
            y = cursor.Y;
        }

        var pixel = _devices.Screen.PixelAt(x, y);
        var wanted = PixelColor.Parse(entry.HexColor);
        var close = pixel.Matches(wanted, entry.ColorTolerance);

        return entry.ColorMatch == ColorMatchCondition.ColorMatches ? close : !close;
    }
}
