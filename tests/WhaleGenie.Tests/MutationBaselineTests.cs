using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using WhaleGenie.TestSupport;

namespace WhaleGenie.Tests;

/// <summary>
/// The mutation table has two copies: the library's README, which records what a run measured, and
/// these testing notes, which are what a person working here reads. The subtree delivers the first
/// one without anyone editing it, so the second has to be edited to match — this check is what asks
/// for that edit at the moment the copy arrives, rather than after a number has been quoted from the
/// notes for a while.
/// </summary>
public class MutationBaselineTests
{
    private readonly record struct Measured(int Scored, string Score);

    [Fact]
    public void The_notes_say_what_the_library_measured()
    {
        var library = ReadLibrary();
        var notes = ReadNotes();

        Assert.NotEmpty(library);

        var problems = new List<string>();
        foreach (var (area, measured) in library)
        {
            if (!notes.TryGetValue(area, out var written))
            {
                problems.Add(area + ": the notes do not list it");
                continue;
            }

            if (written.Scored != measured.Scored)
            {
                problems.Add(area + ": the notes say " + written.Scored
                    + " mutants, the library measured " + measured.Scored);
            }

            if (!string.Equals(written.Score, measured.Score, StringComparison.Ordinal))
            {
                problems.Add(area + ": the notes say " + written.Score
                    + ", the library says " + measured.Score);
            }
        }

        foreach (var area in notes.Keys.Except(library.Keys, StringComparer.Ordinal))
        {
            problems.Add(area + ": the notes list an area the library does not measure");
        }

        Assert.True(problems.Count == 0,
            "the mutation table here has drifted from the library's:\n" + string.Join("\n", problems));
    }

    /// <summary>The library's table: one row per measured area, with the number of mutants and the score.</summary>
    private static Dictionary<string, Measured> ReadLibrary()
    {
        var table = new Dictionary<string, Measured>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(RepoPaths.Root, "core", "README.md")))
        {
            var cells = Cells(line);
            if (cells.Count < 4 || !int.TryParse(cells[2], out var scored))
            {
                continue;
            }

            table[cells[1]] = new Measured(scored, cells[3].Replace(" ", string.Empty));
        }

        return table;
    }

    /// <summary>The notes' table: the rows about the library, named with its area in backticks.</summary>
    private static Dictionary<string, Measured> ReadNotes()
    {
        var table = new Dictionary<string, Measured>(StringComparer.Ordinal);
        foreach (var line in File.ReadLines(Path.Combine(RepoPaths.Root, "docs", "dev", "testing.md")))
        {
            var cells = Cells(line);
            if (cells.Count < 4
                || !cells[1].StartsWith("库仓的 `", StringComparison.Ordinal)
                || !int.TryParse(cells[2], out var scored))
            {
                continue;
            }

            const int start = 5;
            var end = cells[1].IndexOf('`', start);
            if (end < 0)
            {
                continue;
            }

            table[cells[1][start..end]] = new Measured(scored, cells[3].Replace(" ", string.Empty));
        }

        return table;
    }

    private static List<string> Cells(string line)
        => [.. line.Split('|').Select(cell => cell.Trim())];
}
