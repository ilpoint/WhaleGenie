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

// The pass that looks around: programs, the controller, and what one pass leaves behind.
public sealed partial class MacroTriggerService
{
    /// <summary>Looks at every watched pixel and treats a change of state as a press or a release.</summary>
    private void Poll()
    {
        if (!Listening)
        {
            return;
        }

        Look();
    }

    /// <summary>
    /// Everything one round of the clock does. Called by the timer, and by the checks, which need a
    /// round to happen when they say so rather than when a clock says so.
    /// </summary>
    internal void Look()
    {
        _polledAt = DateTime.Now;
        var triggers = TriggerSnapshot();
        foreach (var entry in triggers.Entries)
        {
            if (!entry.IsEnabled || entry.Kind != TriggerKind.Color
                || MacroFor(entry) is not { IsEnabled: true } macro)
            {
                continue;
            }

            bool observed;
            try
            {
                observed = ColourMatches(entry);
            }
            catch (Exception)
            {
                continue;   // reading the screen can fail outside a session
            }

            bool matched;
            lock (_gate)
            {
                matched = observed;
                if (matched == _colourTriggered.Contains(entry))
                {
                    continue;
                }

                if (matched)
                {
                    _colourTriggered.Add(entry);
                }
                else
                {
                    _colourTriggered.Remove(entry);
                }
            }

            if (matched)
            {
                // The macro's own mouse moves change the pixel under the cursor; they must not
                // look like the colour coming back and restart the macro over and over.
                if (IsRunning(macro))
                {
                    continue;
                }

                Pressed(entry, macro);

                lock (_gate)
                {
                    if (_running.ContainsKey(macro)
                        && entry.LoopMode is MacroLoop.Toggle or MacroLoop.Hold)
                    {
                        _colourHeld.Add(macro);
                    }
                }
            }
            else
            {
                bool held;
                lock (_gate)
                {
                    held = _colourHeld.Remove(macro);
                    if (held)
                    {
                        // Let the pass that is running finish before the macro stops, rather
                        // than cutting it off in the middle of its steps.
                        _colourStopping.Add(macro);
                    }
                }

                // A colour that stops matching stops the macro it started; anything else keeps
                // the loop mode's own release behaviour (a "once on release" macro, say).
                if (held)
                {
                    continue;
                }

                Released(entry, macro);
            }
        }

        PollTimers(triggers);
        PollFiles(triggers);
        PollWindows(triggers);
        PollIdle(triggers);
        PruneWatches(triggers);
    }

    /// <summary>The programs, on the clock of their own: this is the one reading whose interval decides
    /// what is noticed, so it is not tied to the round the pixels and the clock share.</summary>
    private void Programs()
    {
        if (!Listening)
        {
            return;
        }

        LookAtPrograms();
    }

    /// <summary>Counting the programs once, for the timer and for the checks.</summary>
    internal void LookAtPrograms()
    {
        try
        {
            PollProcesses(TriggerSnapshot());
        }
        catch (Exception)
        {
            // Reading another program's list can be refused; the next round tries again.
        }
    }

    /// <summary>The controller, on a clock of its own, for the same reason the programs have one.</summary>
    private void Pad()
    {
        if (!Listening)
        {
            return;
        }

        LookAtGamepad();
    }

    /// <summary>
    /// Takes up what the controller is holding without reporting it, so that switching the system on
    /// in the middle of a press does not set off the macro bound to that button. A controller that
    /// cannot be read counts as nothing held, which is what the next reading will find anyway.
    /// </summary>
    private void TakeUpGamepad()
    {
        try
        {
            _padHeld.Take(_pads.Held());
        }
        catch (Exception)
        {
            _padHeld.Take(GamepadButtons.None);
        }
    }

    /// <summary>
    /// Reads the controller once, for the timer and for the checks. A button going down and coming
    /// back up are answered the way a key is, so a binding of <c>Pad A</c> behaves like any other
    /// binding: the loop modes ("while holding", "until pressed again", "on release") say what
    /// happens, and a chord may hold a controller button and a key together.
    /// </summary>
    internal void LookAtGamepad()
    {
        GamepadButtons held;
        try
        {
            held = _pads.Held();
        }
        catch (Exception)
        {
            // A controller that cannot be read is a controller with nothing to say — the same
            // answer as a machine with none plugged in — so the next round tries again rather than
            // the timer dying.
            return;
        }

        foreach (var (button, down) in _padHeld.Changed(held))
        {
            var name = GamepadNames.Binding(button);
            if (name.Length == 0)
            {
                continue;
            }

            // Written down the way a key is, so a combination that holds a controller button down
            // answers the same as one that holds a key.
            lock (_keys)
            {
                if (down)
                {
                    _heldKeys.Add(name);
                }
                else
                {
                    _heldKeys.Remove(name);
                }
            }

            Fire(name, down);
        }
    }

    /// <summary>True when a running macro has been asked to stop once its current pass ends.</summary>
    private bool StoppingAfterPass(MacroItem macro)
    {
        lock (_gate)
        {
            return _colourStopping.Contains(macro);
        }
    }
}
