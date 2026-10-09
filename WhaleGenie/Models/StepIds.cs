using System;
using System.Collections.Generic;
using System.Linq;

namespace WhaleGenie.Models;

/// <summary>
/// The short name a step is known by for as long as it exists. It is written into the macro file,
/// which is what makes it useful for anything the user has to name: a result variable can carry it
/// (two "read the sheet" steps must not both end up writing <c>rows</c>), a condition can point at
/// the step it is asking about, and the run keeps reporting the same step under the same name.
/// </summary>
/// <remarks>
/// Four characters out of an alphabet that leaves out the pairs people read as each other, because
/// this is a name a person has to compare by eye — "was it k3f9 or k3fg?" decides which step a
/// condition watches.
/// </remarks>
public static class StepIds
{
    /// <summary>The characters an id is built from: no i, l, o, u, 0 or 1.</summary>
    private const string Alphabet = "23456789abcdefghjkmnpqrstvwxyz";

    private const int Length = 4;

    /// <summary>An id no step in this macro is using yet.</summary>
    public static string Next(ISet<string> taken)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            var id = Random.Shared.GetItems<char>(Alphabet, Length);
            var written = new string(id);
            if (!taken.Contains(written))
            {
                return written;
            }
        }

        // Sixty-four draws out of a million-shaped space all colliding means the set is not what it
        // looks like; walking up from the alphabet is a dull answer that always terminates.
        for (var value = 0; ; value++)
        {
            var id = Written(value);
            if (!taken.Contains(id))
            {
                return id;
            }
        }
    }

    /// <summary>
    /// Gives every step in the tree an id of its own: one that has none gets a new one, and one
    /// whose id another step is already using gets a new one too. Old macro files have no ids at
    /// all, and a file edited by hand can hold the same id twice — either way the run has to be
    /// able to tell two steps apart, so the tree is settled before the editor shows it.
    /// </summary>
    public static void Settle(IEnumerable<MacroStep> steps)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            Settle(step, taken);
        }
    }

    private static void Settle(MacroStep step, ISet<string> taken)
    {
        if (step.Id.Length == 0 || !taken.Add(step.Id))
        {
            Set(step, Next(taken));
            taken.Add(step.Id);
        }

        foreach (var child in Children(step))
        {
            Settle(child, taken);
        }
    }

    /// <summary>
    /// Writes one id onto a step. The field is init-only so that nothing can quietly change the
    /// name a macro already refers to; this is the one place that hands out a new one.
    /// </summary>
    private static void Set(MacroStep step, string id) => step.Id = id;

    /// <summary>Everything written inside a step, lists and conditions alike.</summary>
    private static IEnumerable<MacroStep> Children(MacroStep step)
        => step.Parameters.SelectMany(parameter => parameter.Steps)
            .Concat(step.Parameters
                .Where(parameter => parameter.Condition is not null)
                .Select(parameter => parameter.Condition!));

    /// <summary>The same id written out of a number, for the exhausted case above.</summary>
    private static string Written(int value)
    {
        var text = string.Empty;
        for (var at = 0; at < Length; at++)
        {
            text += Alphabet[value % Alphabet.Length];
            value /= Alphabet.Length;
        }

        return text;
    }
}
