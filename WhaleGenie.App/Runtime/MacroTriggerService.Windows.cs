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

// Triggers that watch windows opening, changing or closing.
public sealed partial class MacroTriggerService
{
    /// <summary>
    /// Runs the macros waiting for a window to appear or to go away. A macro is answered by
    /// whether a window matching it is there at all, so a window whose title comes to match what
    /// the macro is waiting for counts as appearing, which is what "wait for the page to load"
    /// means in practice.
    /// </summary>
    /// <param name="straightAway">
    /// True on the way in from the machine saying a window changed. That is the one moment worth
    /// reading for, so the interval between rounds is not waited for; everything else about the
    /// reading is the same.
    /// </param>
    private void PollWindows(TriggerRuntimeSnapshot triggers, bool straightAway = false)
    {
        lock (_windowGate)
        {
            var now = DateTime.Now;
            if (!straightAway && now < _windowsDueAt)
            {
                return;
            }

            _windowsDueAt = now + WindowPollInterval;
            _windowsCheckedAt = now;
            ReadWindows(triggers);
        }
    }

    private void ReadWindows(TriggerRuntimeSnapshot triggers)
    {
        foreach (var entry in triggers.Entries)
        {
            if (!entry.IsEnabled || entry.Kind != TriggerKind.Window
                || MacroFor(entry) is not { IsEnabled: true } macro)
            {
                continue;
            }

            var rule = entry.WindowTarget;
            if (!rule.HasTarget)
            {
                continue;
            }

            bool there;
            try
            {
                // Recognised the way every window rule is, so a trigger waiting for a window whose
                // title moves with the document can name the program or a pattern instead.
                there = _devices.Windows.Find(rule.MatchValue, rule.Reading.Match,
                    rule.Reading.Compare, rule.Options) is not null;
            }
            catch (Exception)
            {
                continue;   // the window list can be refused; the trigger simply waits
            }

            bool changed;
            lock (_gate)
            {
                changed = _windows.Changed(entry, there);
            }

            if (!changed)
            {
                continue;
            }

            // A "once" macro waits to be re-armed rather than watching for ever.
            if (entry.TriggerOnce && IsSpent(entry))
            {
                continue;
            }

            var wanted = entry.WindowChange switch
            {
                WindowChangeKind.Appeared => there,
                WindowChangeKind.Disappeared => !there,
                _ => true,
            };

            if (wanted)
            {
                StartFrom(entry, macro, repeating: false);
            }
        }
    }
}
