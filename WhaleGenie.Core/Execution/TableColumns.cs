using System;
using System.Collections.Generic;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// What each cell of a row is called. A table that writes names above its columns keeps them; one
/// that does not — a file with no header, a grid, a block of writing read off the screen — names its
/// columns by where they sit. File, browser, UI Automation and screen readers all hand back the same
/// shape, so they name their columns the same way.
/// </summary>
internal static class TableColumns
{
    /// <summary>
    /// The names of a row's cells: the name written above a column, or its letter. A column nobody
    /// named keeps its letter rather than taking the name of the one before it, and so does a column
    /// that repeats a name already taken: two columns under one name would put one of them out of
    /// reach of a record.
    /// </summary>
    public static IReadOnlyList<string> Names(IReadOnlyList<Value>? header, int width)
    {
        var names = new List<string>(width);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < width; index++)
        {
            var written = index < (header?.Count ?? 0) ? header![index].AsText().Trim() : string.Empty;
            var name = written.Length > 0 && taken.Add(written) ? written : Letter(index);
            names.Add(name);
            taken.Add(name);
        }

        return names;
    }

    /// <summary>The way a person writes a column place on its own: A, B, ... Z, AA.</summary>
    public static string Letter(int place)
    {
        var text = string.Empty;
        for (var at = place; at >= 0; at = (at / 26) - 1)
        {
            text = (char)('A' + (at % 26)) + text;
        }

        return text;
    }
}
