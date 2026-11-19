using System;
using System.Collections.Generic;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Runs the macros a timer trigger is waiting for. One pass for each moment the schedule comes
/// round: a timer says how often to start, so "while holding" and "until pressed again" have
/// nothing to hold on to, and a macro that wants to keep going writes its own loop.
/// </summary>
internal sealed class TimerTrigger : ITriggerPoll
{
    public TriggerKind Kind => TriggerKind.Timer;

    public void Poll(TriggerRound round)
    {
        foreach (var trigger in round.Plan.OfKind(TriggerKind.Timer))
        {
            if (trigger.Spec is not TimerSpec timer)
            {
                continue;
            }

            var due = round.State.Due(trigger.Id);
            if (due is null)
            {
                // Counting starts when the entry is armed, so arming it does not set off every
                // interval macro in the list at once.
                if (NextDue(timer, round.Now) is { } first)
                {
                    round.State.SetDue(trigger.Id, first);
                }

                continue;
            }

            if (round.Now < due)
            {
                continue;
            }

            // The next run counts from the one that has just come round, so the schedule does not
            // drift. When the machine was away past several of them, the wait starts again from
            // now rather than firing them off in a burst.
            var next = NextDue(timer, due.Value);
            if (next is not { } armed || armed <= round.Now)
            {
                next = NextDue(timer, round.Now);
            }

            if (next is { } moment)
            {
                round.State.SetDue(trigger.Id, moment);
            }
            else
            {
                round.State.ClearDue(trigger.Id);
            }

            round.Start(trigger, repeating: false);
        }
    }

    public IEnumerable<TriggerWatch> Report(TriggerRound round)
    {
        foreach (var trigger in round.Plan.OfKind(TriggerKind.Timer))
        {
            yield return new TriggerWatch(trigger.Id, trigger.Macro.Name, trigger.Looking, null,
                round.LastPolledAt, round.State.Due(trigger.Id) ?? DateTime.MinValue, false);
        }
    }

    /// <summary>When this schedule next wants to run, or null when it is not filled in.</summary>
    private static DateTime? NextDue(TimerSpec timer, DateTime from)
        => MacroSchedule.TryNextDue(timer, from, out var due) ? due : null;
}
