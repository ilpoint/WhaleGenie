using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// A typed abbreviation that expands into a macro. The recogniser keeps the last few characters;
/// this turns a hit into the input cleanup the author asked for and then starts the macro.
/// </summary>
internal sealed class HotstringTrigger(HotstringRecognizer recognizer, HeldKeys held, IInputDevice input)
{
    /// <summary>Modifiers that mean the press is a shortcut rather than typing.</summary>
    private static readonly string[] Modifiers =
        ["左Ctrl", "右Ctrl", "Ctrl", "左Alt", "右Alt", "Alt", "左Win", "右Win", "Win"];

    public bool Press(TriggerRound round, KeyHookEvent e)
    {
        var keys = held.All();
        if (keys.Any(key => Modifiers.Contains(key, StringComparer.OrdinalIgnoreCase)))
        {
            recognizer.Reset();
            return false;
        }

        var typed = HotstringText.Read(e.Key, keys);
        if (typed.Text.Length == 0)
        {
            return false;
        }

        var choices = new List<HotstringChoice>();
        var byId = new Dictionary<string, ArmedTrigger>(StringComparer.Ordinal);
        foreach (var trigger in round.Plan.OfKind(TriggerKind.Hotstring))
        {
            if (trigger.Spec is not HotstringSpec spec || spec.Abbreviation.Length == 0)
            {
                continue;
            }

            choices.Add(new HotstringChoice(trigger.Id, spec));
            byId[trigger.Id] = trigger;
        }

        if (recognizer.Press(typed.Text, typed.EndKey, choices) is not { } hit
            || !byId.TryGetValue(hit.Id, out var armed))
        {
            return false;
        }

        try
        {
            if (hit.Backspaces > 0)
            {
                for (var index = 0; index < hit.Backspaces; index++)
                {
                    input.KeyPress("Backspace", 0);
                }
            }

            if (hit.ReplayEndingKey)
            {
                if (hit.EndingText is "\n" or "\t")
                {
                    input.KeyPress(hit.EndingKey, 0);
                }
                else
                {
                    input.TypeText(hit.EndingText, 0);
                }
            }

            e.Suppress = hit.SuppressEndingKey;
        }
        catch (Exception error)
        {
            AppDiagnostics.Record("trigger.hotstring-input", error);
            return false;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Let the cleanup and the ending key reach the foreground control before the macro
                // starts sending its own input.
                await Task.Delay(1);
                round.Start(armed, repeating: false);
            }
            catch (Exception error)
            {
                AppDiagnostics.Record("trigger.hotstring-start", error);
            }
        });
        return true;
    }
}
