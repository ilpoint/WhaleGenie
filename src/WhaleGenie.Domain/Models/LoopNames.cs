using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace WhaleGenie.Models;

/// <summary>
/// The variables a loop writes for its own rounds, and the name a loop nested inside another one
/// takes so it does not borrow one of them.
/// </summary>
/// <remarks>
/// This is the one place that says which field of which action is a loop's own name: the
/// design-time check reads a macro's steps with it, and the dialog that writes a new step asks it
/// before that step's counter field is put on screen. A second answer in the interface would name
/// a counter by a rule the check does not know about.
/// </remarks>
public static class LoopNames
{
    /// <summary>
    /// The names a loop writes for its own rounds: the item and the round number of a "for each",
    /// and the counter of a counting loop. A repeat or a while counts the rounds in the runner's
    /// head, so there is no name of the macro's to keep.
    /// </summary>
    /// <remarks>
    /// A loop that names nothing writes the name the catalogue starts it with — <c>item</c> for a
    /// "for each", <c>i</c> for a counting loop — so an empty field is that default rather than
    /// nothing at all. Leaving it out would let an inner loop take a name the outer one really
    /// writes, which is exactly the shadowing the check is about.
    /// </remarks>
    public static IReadOnlyList<string> Of(MacroStep step)
    {
        if (string.Equals(step.Type, "control.forEach", StringComparison.Ordinal))
        {
            var item = step.Text("itemVariable").Trim();
            var index = step.Text("indexVariable").Trim();
            return
            [
                .. new[] { item.Length == 0 ? "item" : item, index }
                    .Where(name => name.Length > 0),
            ];
        }

        if (string.Equals(step.Type, "control.for", StringComparison.Ordinal))
        {
            var counter = step.Text("name").Trim();
            return [counter.Length == 0 ? "i" : counter];
        }

        return [];
    }

    /// <summary>True when this field of this action is the counter a counting loop writes.</summary>
    public static bool WritesCounter(string? type, string parameterName)
        => parameterName == "name" && string.Equals(type, "control.for", StringComparison.Ordinal);

    /// <summary>
    /// The first of <paramref name="preferred"/>, preferred2, preferred3 … that none of
    /// <paramref name="taken"/> is using.
    /// </summary>
    /// <remarks>
    /// Comparison is the variable table's: case does not make two names different. The number
    /// starts at two, so the second loop of a macro reads <c>i2</c> rather than <c>i1</c>.
    /// <paramref name="preferred"/> is the field's own starting name, which the caller only passes
    /// when it has one.
    /// </remarks>
    public static string Free(IReadOnlyCollection<string> taken, string preferred)
    {
        if (!taken.Any(name => Same(name, preferred)))
        {
            return preferred;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = preferred + suffix.ToString(CultureInfo.InvariantCulture);
            if (!taken.Any(name => Same(name, candidate)))
            {
                return candidate;
            }
        }

        static bool Same(string left, string right)
            => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }
}
