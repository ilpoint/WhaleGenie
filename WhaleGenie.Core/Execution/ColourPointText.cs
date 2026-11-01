using System.Collections.Generic;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// One point of a pattern — an offset from the base colour and the colour there — written
/// <c>dx,dy,#RRGGBB</c>. Reading it is shared because two places must agree: the runner that uses
/// the pattern, and the box that lets a line be corrected.
/// </summary>
public static class ColourPointText
{
    /// <summary>
    /// Splits one line into its parts at the commas that separate them. A comma inside brackets
    /// belongs to the expression inside them — <c>max(1,3),-4,#FF0000</c> is three parts, not four —
    /// which is why this is not a plain split.
    /// </summary>
    public static IReadOnlyList<string> Split(string line)
    {
        var parts = new List<string>(3);
        var depth = 0;
        var start = 0;
        for (var at = 0; at < line.Length; at++)
        {
            var letter = line[at];
            if (letter == '(')
            {
                depth++;
            }
            else if (letter == ')' && depth > 0)
            {
                depth--;
            }
            else if (letter == ',' && depth == 0)
            {
                parts.Add(line[start..at].Trim());
                start = at + 1;
            }
        }

        parts.Add(line[start..].Trim());
        return parts;
    }
}
