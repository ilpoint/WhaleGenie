using System.Collections.Generic;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Keys, mouse buttons, wheel notches and controller buttons, answered from the hook thread. What
/// a press means depends on the loop mode, and that is decided in one place for keys and colours
/// alike.
/// </summary>
internal sealed class KeyTrigger(HeldKeys held) : ITriggerInput
{
    public void Pressed(TriggerRound round, string key) => Fire(round, key, pressed: true);

    public void Released(TriggerRound round, string key) => Fire(round, key, pressed: false);

    public IEnumerable<TriggerWatch> Report(TriggerRound round)
    {
        foreach (var trigger in round.Plan.OfKind(TriggerKind.Key))
        {
            // A key is not "there" or "due": the hook says when it happened, and nothing is on a
            // clock, so the run window shows the row without a state.
            yield return new TriggerWatch(trigger.Id, trigger.Macro.Name, trigger.Looking, null,
                round.LastPolledAt, System.DateTime.MinValue, true);
        }
    }

    private void Fire(TriggerRound round, string key, bool pressed)
    {
        foreach (var trigger in round.Plan.OfKind(TriggerKind.Key))
        {
            if (trigger.Spec is not KeySpec spec || !held.Binds(spec.Binding, key))
            {
                continue;
            }

            if (pressed)
            {
                TriggerLoop.Pressed(round, trigger, spec.Loop);
            }
            else
            {
                TriggerLoop.Released(round, trigger, spec.Loop);
            }
        }
    }
}
