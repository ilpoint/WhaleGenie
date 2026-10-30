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
/// wrong thing in the middle of a job. So the name says what the note says and what the result is,
/// and a name that is already spoken for gets the step's own name after it, which is the one part
/// no other step shares. The action's name and the step's name are not in every name: they made the
/// common case — a note the user wrote, read back under the name they would have chosen — long
/// enough to be unreadable at a glance.
/// </remarks>
public static class VariableNames
{
    /// <summary>
    /// How much of the note is kept. Long enough for "上个月的销量" and short enough that the name
    /// still reads at a glance in a list.
    /// </summary>
    public const int NoteLength = 12;

    /// <summary>
    /// What to name one result of a step: the note and what the result is, and — when that name is
    /// already spoken for somewhere the macro can see — the step's own name after it. A step that
    /// hands over several results gets a name for each, so a find-image step that hands over a hit
    /// and the whole list can be read back as one or the other.
    /// </summary>
    public static string ForStep(string note, string result, string stepId,
        IEnumerable<string>? taken = null)
    {
        var wanted = Join(Part(note, NoteLength), Part(result, 0));
        if (wanted.Length == 0)
        {
            // A step with no note whose action hands over a result nobody named in the table still
            // has to leave that result somewhere a later step can read it.
            wanted = Part(stepId, 0);
        }

        if (wanted.Length == 0)
        {
            return string.Empty;
        }

        var used = new HashSet<string>(taken ?? [], StringComparer.OrdinalIgnoreCase);
        return used.Contains(wanted) ? Free(Join(wanted, Part(stepId, 0)), used) : wanted;
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

    /// <summary>The parts that are worth anything, joined the way a name is written.</summary>
    private static string Join(params string[] parts)
        => string.Join("_", parts.Where(part => part.Length > 0));
}
