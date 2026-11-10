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

// Triggers that come due on a clock, and the files a pass notices.
public sealed partial class MacroTriggerService
{
    /// <summary>Runs the macros a timer trigger is waiting for.</summary>
    private void PollTimers(TriggerRuntimeSnapshot triggers)
    {
        var now = DateTime.Now;

        foreach (var entry in triggers.Entries)
        {
            if (!entry.IsEnabled || entry.Kind != TriggerKind.Timer
                || MacroFor(entry) is not { IsEnabled: true } macro)
            {
                continue;
            }

            // A schedule that has had its one turn waits until the macro is re-armed.
            if (entry.TriggerOnce && IsSpent(entry))
            {
                continue;
            }

            var fire = false;
            lock (_gate)
            {
                if (!_scheduleDue.TryGetValue(entry, out var due))
                {
                    // Counting starts when the system is switched on, so turning it on does not
                    // set off every interval macro in the list at once.
                    if (NextDue(entry, now) is { } first)
                    {
                        _scheduleDue[entry] = first;
                    }
                }
                else if (now >= due)
                {
                    fire = true;

                    // The next run counts from the one that has just come round, so the schedule
                    // does not drift. When the machine was away past several of them, the wait
                    // starts again from now rather than firing them off in a burst.
                    var next = NextDue(entry, due);
                    if (next is not { } armed || armed <= now)
                    {
                        next = NextDue(entry, now);
                    }

                    if (next is { } moment)
                    {
                        _scheduleDue[entry] = moment;
                    }
                    else
                    {
                        _scheduleDue.Remove(entry);
                    }
                }
            }

            if (fire)
            {
                // One pass for each moment the schedule comes round. A timer says how often to
                // start, so "while holding" and "until pressed again" have nothing to hold on to;
                // a macro that wants to keep going writes its own loop.
                StartFrom(entry, macro, repeating: false);
            }
        }
    }

    /// <summary>When this macro's schedule next wants to run, or null when it is not filled in.</summary>
    private static DateTime? NextDue(TriggerEntry entry, DateTime from)
        => MacroSchedule.TryNextDue(entry, from, out var due) ? due : null;

    /// <summary>Runs the macros whose watched file or folder has changed.</summary>
    private void PollFiles(TriggerRuntimeSnapshot triggers)
    {
        var now = DateTime.Now;

        foreach (var entry in triggers.Entries)
        {
            if (!entry.IsEnabled || entry.Kind != TriggerKind.File
                || MacroFor(entry) is not { IsEnabled: true } macro)
            {
                continue;
            }

            if (Watch(entry) is not { } watch)
            {
                continue;
            }

            watch.Ensure(now);

            // A watch that has had its one turn waits until the macro is re-armed.
            if (entry.TriggerOnce && IsSpent(entry))
            {
                continue;
            }

            if (watch.Take(now))
            {
                // One pass for each change, the way a timer does it: there is nothing to hold on
                // to, so a macro that wants to keep going writes its own loop.
                StartFrom(entry, macro, repeating: false);
            }
        }
    }

    /// <summary>
    /// The watch belonging to a macro: built the first time the macro is seen, and built again
    /// when its path or its settings change.
    /// </summary>
    private FileWatch? Watch(TriggerEntry entry)
    {
        string path;
        try
        {
            path = _devices.Files.Resolve(entry.WatchPath.Trim());
        }
        catch (Exception)
        {
            return null;    // no files on this machine, so there is nothing to watch
        }

        if (path.Length == 0)
        {
            return null;
        }

        var signature = FileWatch.SignatureOf(entry, path);
        lock (_gate)
        {
            if (_watches.TryGetValue(entry, out var existing))
            {
                if (existing.Signature == signature)
                {
                    return existing;
                }

                existing.Dispose();
                _watches.Remove(entry);
            }

            var watch = FileWatch.Create(entry, path);
            _watches[entry] = watch;
            return watch;
        }
    }
}
