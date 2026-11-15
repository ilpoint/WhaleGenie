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

// Triggers that watch whether a program is running.
public sealed partial class MacroTriggerService
{
    /// <summary>
    /// Runs the macros waiting for a program to start or to finish. The processes with the wanted
    /// name are counted afresh on every poll, so a difference from the count before is what
    /// happened since — a program that was already running is not something that has just started.
    /// </summary>
    private void PollProcesses(TriggerRuntimeSnapshot triggers)
    {
        _programsCheckedAt = DateTime.Now;
        _programsDueAt = _programsCheckedAt + ProgramPollInterval;

        // Every program a macro asks about is asked about once, so two macros watching the same
        // program cost one look-up rather than two.
        var looked = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in triggers.Entries)
        {
            if (!entry.IsEnabled || entry.Kind != TriggerKind.Process
                || MacroFor(entry) is not { IsEnabled: true } macro)
            {
                continue;
            }

            var name = entry.ProcessName.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            IReadOnlyList<int> running;
            try
            {
                if (!looked.TryGetValue(name, out var found))
                {
                    found = _devices.Processes.Find(name);
                    looked[name] = found;
                }

                running = found;
            }
            catch (Exception)
            {
                continue;   // another program's process list can be refused; the trigger waits
            }

            (bool Started, bool Stopped)? moved;
            lock (_gate)
            {
                moved = _programsRunning.Changed(entry, running);
            }

            // Counting starts when the system is switched on, so switching it on does not set off
            // every macro whose program happens to be open already.
            if (moved is not { } change)
            {
                continue;
            }

            // A "once" macro waits to be re-armed rather than watching for ever.
            if (entry.TriggerOnce && IsSpent(entry))
            {
                continue;
            }

            var wanted = entry.ProcessChange switch
            {
                ProcessChangeKind.Started => change.Started,
                ProcessChangeKind.Stopped => change.Stopped,
                _ => change.Started || change.Stopped,
            };

            if (wanted)
            {
                StartFrom(entry, macro, repeating: false);
            }
        }
    }
}
