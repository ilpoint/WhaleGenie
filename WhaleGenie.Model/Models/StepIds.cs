using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;

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
///
/// Two steps may not share an id in one document chain, so a duplicate is given a fresh one. That
/// rename is not the whole repair: conditions and expressions name steps as
/// <c>step.&lt;id&gt;.outcome</c>, and leaving those pointing at an id nothing answers to would turn
/// a repaired file into a file that fails at run time. So the same pass rewrites those references
/// and says which ids moved, and the editor shows that on its notice line.
/// </remarks>
public static class StepIds
{
    /// <summary>One id that had to move because another step was already using it.</summary>
    public sealed record Rename(string Old, string New);

    /// <summary>The characters an id is built from: no i, l, o, u, 0 or 1.</summary>
    private const string Alphabet = "23456789abcdefghjkmnpqrstvwxyz";

    private const int Length = 4;

    /// <summary>The variable a run keeps a step's outcome in, without the id.</summary>
    private const string StepPrefix = "step.";

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
    /// whose id another step is already using gets a new one too — with everything that named the
    /// old id rewritten to the new one. Old macro files have no ids at all, and a file edited by
    /// hand can hold the same id twice; either way the run has to be able to tell two steps apart.
    /// </summary>
    public static IReadOnlyList<Rename> Settle(IEnumerable<MacroStep> steps)
        => Settle([steps.ToList()]);

    /// <summary>
    /// Settles several callable documents against one set of names. A parent and its submacros run
    /// under the same parent identity, so steps in different documents still need distinct ids.
    /// </summary>
    public static IReadOnlyList<Rename> SettleAcross(IEnumerable<IEnumerable<MacroStep>> documents)
        => Settle([.. documents.Select(steps => steps.ToList())]);

    private static IReadOnlyList<Rename> Settle(List<List<MacroStep>> documents)
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A rename is followed inside the step that was renamed and nowhere else. That is the step
        // the reference travelled with: pasting a block copies ids and the conditions that name
        // them together, so the copies move as one, and the block that was already there keeps the
        // names it owns. Rewriting the whole document instead would hand the original's conditions
        // to the copy — a repair that changes what the macro does.
        var renamed = new List<(MacroStep Step, Rename Change)>();

        foreach (var step in documents.SelectMany(steps => steps))
        {
            Settle(step, taken, renamed);
        }

        foreach (var (step, change) in renamed)
        {
            Rewrite(step, [change]);
        }

        return [.. renamed.Select(entry => entry.Change)];
    }

    private static void Settle(MacroStep step, ISet<string> taken,
        List<(MacroStep Step, Rename Change)> renamed)
    {
        if (step.Id.Length == 0)
        {
            Set(step, Next(taken));
            taken.Add(step.Id);
        }
        else if (!taken.Add(step.Id))
        {
            // Two steps were carrying one name. The later one moves, and everything that pointed at
            // the name is rewritten below: a rename nobody follows is a broken reference.
            var old = step.Id;
            Set(step, Next(taken));
            taken.Add(step.Id);
            renamed.Add((step, new Rename(old, step.Id)));
        }

        foreach (var child in Children(step))
        {
            Settle(child, taken, renamed);
        }
    }

    /// <summary>
    /// Rewrites every reference to a moved id inside one step: conditions and expressions that name
    /// a step, and nothing else. A field the engine reads as text is left alone — a macro that types
    /// the words <c>step.abcd.outcome</c> into a window means those words, not a reference.
    /// </summary>
    private static void Rewrite(MacroStep step, IReadOnlyList<Rename> renamed)
    {
        foreach (var parameter in step.Parameters)
        {
            if (ReadsReferences(step.Type, parameter.Name) && parameter.Value.Length > 0)
            {
                parameter.Value = Rewrite(parameter.Value, renamed);
            }

            foreach (var child in parameter.Steps)
            {
                Rewrite(child, renamed);
            }
        }
    }

    /// <summary>
    /// Whether a field is read as a reference to something — an expression, a variable, a value with
    /// names in it — rather than as the words themselves.
    /// </summary>
    private static bool ReadsReferences(string type, string parameter)
        => ActionSemanticsCatalog.Find(type)?.Parameter(parameter) is
        { AcceptsVariables: true, ReadMode: ActionReadMode.WholeValue };

    /// <summary>
    /// The text with every <c>step.&lt;old&gt;</c> that names a moved step replaced by the new id.
    /// The id has to be a name of its own: <c>step.abcdx</c> is not <c>step.abcd</c>.
    /// </summary>
    private static string Rewrite(string text, IReadOnlyList<Rename> renamed)
    {
        var result = text;
        foreach (var rename in renamed)
        {
            result = Replace(result, StepPrefix + rename.Old, StepPrefix + rename.New);
        }

        return result;
    }

    private static string Replace(string text, string old, string replacement)
    {
        var at = text.IndexOf(old, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var from = 0;
        while (at >= 0)
        {
            var after = at + old.Length;
            var endsName = after == text.Length || !char.IsLetterOrDigit(text[after]);

            if (!endsName)
            {
                // Part of a longer name that happens to start the same way.
                from = after;
                at = text.IndexOf(old, from, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            builder.Append(text, from, at - from).Append(replacement);
            from = after;
            at = text.IndexOf(old, from, StringComparison.OrdinalIgnoreCase);
        }

        return builder.Append(text, from, text.Length - from).ToString();
    }

    /// <summary>
    /// Writes one id onto a step. The field is init-only so that nothing can quietly change the
    /// name a macro already refers to; this is the one place that hands out a new one.
    /// </summary>
    private static void Set(MacroStep step, string id) => step.Id = id;

    /// <summary>Everything written inside a step's child lists.</summary>
    private static IEnumerable<MacroStep> Children(MacroStep step)
        => step.Parameters.SelectMany(parameter => parameter.Steps);

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
