using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Runs the macros waiting for a program to start or to finish. The processes with the wanted name
/// are counted afresh on every poll, so a difference from the count before is what happened since —
/// a program that was already running is not something that has just started.
/// </summary>
internal sealed class ProgramTrigger(IProcessDevice processes) : ITriggerPoll
{
    public TriggerKind Kind => TriggerKind.Process;

    private DateTime _checkedAt;
    private DateTime _dueAt;

    public void Poll(TriggerRound round)
    {
        _checkedAt = round.Now;
        _dueAt = round.Now + TriggerPace.Program;

        // Every program a macro asks about is asked about once, so two macros watching the same
        // program cost one look-up rather than two.
        var looked = new Dictionary<string, IReadOnlyList<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var trigger in round.Plan.OfKind(TriggerKind.Process))
        {
            if (trigger.Spec is not ProcessSpec spec)
            {
                continue;
            }

            var name = spec.Name.Trim();
            if (name.Length == 0)
            {
                continue;
            }

            IReadOnlyList<int> running;
            try
            {
                if (!looked.TryGetValue(name, out var found))
                {
                    found = processes.Find(name);
                    looked[name] = found;
                }

                running = found;
            }
            catch (Exception)
            {
                continue;   // another program's process list can be refused; the trigger waits
            }

            // Counting starts when the entry is armed, so arming it does not set off every macro
            // whose program happens to be open already.
            if (round.State.ProgramChanged(trigger.Id, running) is not { } change)
            {
                continue;
            }

            var wanted = spec.Change switch
            {
                ProcessChangeKind.Started => change.Started,
                ProcessChangeKind.Stopped => change.Stopped,
                _ => change.Started || change.Stopped,
            };

            if (wanted)
            {
                round.Start(trigger, repeating: false);
            }
        }
    }

    public IEnumerable<TriggerWatch> Report(TriggerRound round)
    {
        foreach (var trigger in round.Plan.OfKind(TriggerKind.Process))
        {
            yield return new TriggerWatch(trigger.Id, trigger.Macro.Name, trigger.Looking,
                round.State.Program(trigger.Id), _checkedAt, _dueAt, false);
        }
    }
}
