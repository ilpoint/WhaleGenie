using System;
using System.Collections.Generic;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Runs the macros waiting for the machine to be left alone. One run per quiet spell: once it has
/// run, the macro waits for input to arrive and the machine to fall quiet again, so a machine left
/// alone overnight does not set the same macro off all night.
/// </summary>
/// <remarks>
/// Input anywhere counts, including what a macro sends itself: a macro that moves the mouse counts
/// as having used the machine. An idle time that cannot be read is not a quiet spell.
/// </remarks>
internal sealed class IdleTrigger : ITriggerPoll
{
    public TriggerKind Kind => TriggerKind.Idle;

    public void Poll(TriggerRound round)
    {
        if (IdleWatch.Since() is not { } idle)
        {
            return;
        }

        foreach (var trigger in round.Plan.OfKind(TriggerKind.Idle))
        {
            if (trigger.Spec is not IdleSpec spec)
            {
                continue;
            }

            if (idle < TimeSpan.FromSeconds(Math.Max(1, spec.Seconds)))
            {
                round.State.ClearIdleDone(trigger.Id);
                continue;
            }

            if (round.State.MarkIdleDone(trigger.Id))
            {
                round.Start(trigger, repeating: false);
            }
        }
    }

    public IEnumerable<TriggerWatch> Report(TriggerRound round)
    {
        var since = IdleWatch.Since();
        var now = since is null ? DateTime.MinValue : DateTime.Now;
        foreach (var trigger in round.Plan.OfKind(TriggerKind.Idle))
        {
            var matched = trigger.Spec is IdleSpec spec && since is { } idle
                && idle >= TimeSpan.FromSeconds(Math.Max(1, spec.Seconds));
            yield return new TriggerWatch(trigger.Id, trigger.Macro.Name, trigger.Looking,
                matched, now, now == DateTime.MinValue ? now : now + TriggerPace.Poll, false);
        }
    }
}
