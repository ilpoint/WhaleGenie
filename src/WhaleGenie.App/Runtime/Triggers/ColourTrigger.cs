using System;
using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Watches a pixel for a colour. What counts is the edge, not the colour: a macro runs when the
/// pixel comes to match what it waits for, and one that keeps matching is not started again on
/// every pass.
/// </summary>
/// <remarks>
/// A colour that stops matching stops the macro it started, but only after the pass that is running
/// has finished: cutting a macro off in the middle of its steps would leave it half done. That is
/// why a stop is remembered here and asked for by the run itself.
/// </remarks>
internal sealed class ColourTrigger(IScreenDevice screen, IInputDevice input) : ITriggerPoll
{
    private readonly object _gate = new();
    private readonly HashSet<string> _held = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _stopping = new(StringComparer.OrdinalIgnoreCase);

    public TriggerKind Kind => TriggerKind.Color;

    /// <summary>True when the macro was started by a colour and that colour is gone now.</summary>
    public bool Stopping(string macroName)
    {
        lock (_gate)
        {
            return _stopping.Contains(macroName);
        }
    }

    /// <summary>Forgets the colour bookkeeping of a run that has ended.</summary>
    public void Forget(string macroName)
    {
        lock (_gate)
        {
            _held.Remove(macroName);
            _stopping.Remove(macroName);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _held.Clear();
            _stopping.Clear();
        }
    }

    public void Poll(TriggerRound round)
    {
        foreach (var trigger in round.Plan.OfKind(TriggerKind.Color))
        {
            if (trigger.Spec is not ColorSpec colour)
            {
                continue;
            }

            bool observed;
            try
            {
                observed = Matches(colour);
            }
            catch (Exception)
            {
                continue;   // reading the screen can fail outside a session
            }

            if (!round.State.ColourChanged(trigger.Id, observed))
            {
                continue;
            }

            var name = trigger.Macro.Name;
            if (observed)
            {
                // The macro's own mouse moves change the pixel under the cursor; they must not
                // look like the colour coming back and restart the macro over and over.
                if (round.IsRunning(trigger))
                {
                    continue;
                }

                TriggerLoop.Pressed(round, trigger, colour.Loop);
                lock (_gate)
                {
                    if (round.IsRunning(trigger) && colour.Loop is MacroLoop.Toggle or MacroLoop.Hold)
                    {
                        _held.Add(name);
                    }
                }
            }
            else
            {
                bool held;
                lock (_gate)
                {
                    held = _held.Remove(name);
                    if (held)
                    {
                        _stopping.Add(name);
                    }
                }

                // A colour that stops matching stops the macro it started; anything else keeps the
                // loop mode's own release behaviour (a "once on release" macro, say).
                if (held)
                {
                    continue;
                }

                TriggerLoop.Released(round, trigger, colour.Loop);
            }
        }
    }

    public IEnumerable<TriggerWatch> Report(TriggerRound round)
    {
        foreach (var trigger in round.Plan.OfKind(TriggerKind.Color))
        {
            yield return new TriggerWatch(trigger.Id, trigger.Macro.Name, trigger.Looking,
                round.State.Colour(trigger.Id), round.LastPolledAt, round.LastPolledAt + TriggerPace.Poll,
                false);
        }
    }

    private bool Matches(ColorSpec colour)
    {
        var x = colour.X;
        var y = colour.Y;

        if (colour.FollowCursor)
        {
            var cursor = input.Cursor;
            x = cursor.X;
            y = cursor.Y;
        }

        var pixel = screen.PixelAt(x, y);
        var wanted = PixelColor.Parse(colour.Hex);
        var close = pixel.Matches(wanted, colour.Tolerance);

        return colour.Match == ColorMatchCondition.ColorMatches ? close : !close;
    }
}
