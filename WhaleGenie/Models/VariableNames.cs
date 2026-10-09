using System;
using System.Collections.Generic;
using System.Linq;

namespace WhaleGenie.Models;

/// <summary>
/// The name a step gives the variable it writes, before the user has said anything about it.
/// </summary>
/// <remarks>
/// Two "read the sheet" steps both want to call it <c>rows</c>, and the second one quietly
/// overwrites the first — which is the kind of mistake nobody sees until the macro is doing the
/// wrong thing in the middle of a job. So the name says which step it came from: the note the user
/// wrote, the action, and the name the step goes by. Any one of the three alone is not enough —
/// the note is what the user recognises, the action says what kind of thing it is, and the step's
/// name is the only part that is unique for certain.
/// </remarks>
public static class VariableNames
{
    /// <summary>
    /// How much of the note is kept. Long enough for "上个月的销量" and short enough that the name
    /// still reads at a glance in a list.
    /// </summary>
    public const int NoteLength = 12;

    /// <summary>What to write into a result variable of a step with this note, action and name.</summary>
    public static string ForStep(string note, string action, string stepId)
    {
        var parts = new List<string>();
        foreach (var (text, limit) in new[] { (note, NoteLength), (action, 0), (stepId, 0) })
        {
            var part = Part(text, limit);
            if (part.Length > 0)
            {
                parts.Add(part);
            }
        }

        return string.Join("_", parts);
    }

    /// <summary>
    /// The same name, made free: one that is already spoken for gets a number after it, because a
    /// name that is already in use is either overwritten or shadowed, and neither is what the user
    /// meant by "read the sheet into a variable".
    /// </summary>
    public static string Free(string wanted, IEnumerable<string> taken)
    {
        var used = new HashSet<string>(taken ?? [], StringComparer.OrdinalIgnoreCase);
        if (wanted.Length == 0 || !used.Contains(wanted))
        {
            return wanted;
        }

        for (var number = 2; number < 1000; number++)
        {
            var candidate = wanted + "_" + number;
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }

        return wanted;
    }

    /// <summary>
    /// One part of a name: letters and digits are kept — Chinese is letters, so a Chinese note
    /// works — and everything else becomes an underscore, because a space or a colon in a name is
    /// a name the macro language cannot read back.
    /// </summary>
    private static string Part(string text, int limit)
    {
        var kept = (text ?? string.Empty).Trim();
        if (limit > 0 && kept.Length > limit)
        {
            kept = kept[..limit];
        }

        var cleaned = new string([.. kept.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_')]);

        // Runs of them are folded into one, so "a: b" reads as "a_b" rather than "a__b".
        while (cleaned.Contains("__", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        }

        return cleaned.Trim('_');
    }
}
