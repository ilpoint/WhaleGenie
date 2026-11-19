using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Runs the macros waiting for a window to appear or to go away. A macro is answered by whether a
/// window matching it is there at all, so a window whose title comes to match what the macro is
/// waiting for counts as appearing, which is what "wait for the page to load" means in practice.
/// </summary>
internal sealed class WindowTrigger : ITriggerPoll
{
    private readonly object _gate = new();
    private readonly IWindowDevice _windows;
    private readonly IWindowEvents _events;
    private DateTime _dueAt;
    private DateTime _checkedAt;

    public WindowTrigger(IWindowDevice windows, IWindowEvents events)
    {
        _windows = windows;
        _events = events;
    }

    public TriggerKind Kind => TriggerKind.Window;

    /// <summary>Whether the machine says when a window changes, rather than it being looked for.</summary>
    public bool Told => _events.Available;

    public void Poll(TriggerRound round) => Read(round, straightAway: false);

    /// <summary>
    /// Reads the window list because the machine said something changed, rather than on the clock.
    /// A window worth catching is one that is only up for a moment; by the next round it is gone.
    /// </summary>
    public void PollStraightAway(TriggerRound round) => Read(round, straightAway: true);

    public IEnumerable<TriggerWatch> Report(TriggerRound round)
    {
        DateTime checkedAt;
        DateTime dueAt;
        lock (_gate)
        {
            checkedAt = _checkedAt;
            dueAt = _dueAt;
        }

        foreach (var trigger in round.Plan.OfKind(TriggerKind.Window))
        {
            yield return new TriggerWatch(trigger.Id, trigger.Macro.Name, trigger.Looking,
                round.State.Window(trigger.Id), checkedAt, dueAt, Told);
        }
    }

    private void Read(TriggerRound round, bool straightAway)
    {
        lock (_gate)
        {
            if (!straightAway && round.Now < _dueAt)
            {
                return;
            }

            _dueAt = round.Now + TriggerPace.Window;
            _checkedAt = round.Now;

            foreach (var trigger in round.Plan.OfKind(TriggerKind.Window))
            {
                if (trigger.Spec is not WindowSpec spec || !spec.Target.HasTarget)
                {
                    continue;
                }

                bool there;
                try
                {
                    // Recognised the way every window rule is, so a trigger waiting for a window
                    // whose title moves with the document can name the program instead.
                    there = _windows.Find(spec.Target.MatchValue, spec.Target.Reading.Match,
                        spec.Target.Reading.Compare, spec.Target.Options) is not null;
                }
                catch (Exception)
                {
                    continue;   // the window list can be refused; the trigger simply waits
                }

                if (!round.State.WindowChanged(trigger.Id, there))
                {
                    continue;
                }

                var wanted = spec.Change switch
                {
                    WindowChangeKind.Appeared => there,
                    WindowChangeKind.Disappeared => !there,
                    _ => true,
                };

                if (wanted)
                {
                    round.Start(trigger, repeating: false);
                }
            }
        }
    }
}
