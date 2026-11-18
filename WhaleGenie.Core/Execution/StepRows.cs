using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// What a step listed, written out for a failure that has to show what it could not find or read.
/// One line per row, one comma between the cells, the way the catalogue draws the list.
/// </summary>
internal static class StepRows
{
    public static string Written(IReadOnlyList<IReadOnlyDictionary<string, ExpressionNode>> rows)
        => string.Join("; ", rows.Select(row =>
            string.Join(',', row.Values.Select(value => Formula.Speak(value)))));
}
