using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace WhaleGenie.Tests;

/// <summary>
/// The size ledger records the decision behind a file that grew past a check line, so it has to
/// describe the files that are actually here. These checks keep it honest: every named file still
/// exists at the recorded size, every code file that crossed a line is named, every entry past the
/// debt line carries a verdict, and an entry that has come back under its line is dropped instead of
/// reviewed twice. A stale row is a decision about a file that no longer needs one.
/// </summary>
public class FileSizeTests
{
    /// <summary>Below this many lines a code file is healthy and has no entry.</summary>
    private const int CodeCheckLine = 300;

    /// <summary>At this many lines a code file is reviewed in the ledger.</summary>
    private const int CodeSuggestLine = 500;

    /// <summary>Past this many lines a code file has to be split and recorded as debt.</summary>
    private const int CodeDebtLine = 700;

    /// <summary>Below this many bytes a document has no entry.</summary>
    private const long DocumentCheckLine = 20_000;

    private static readonly string[] CodeExtensions = [".cs", ".axaml", ".ps1"];

    private static readonly string[] Verdicts = ["keep:", "split:", "exempt:"];

    // The same folders the pre-commit check never reports: a scratch or build folder says nothing
    // about how a task reads this repository.
    private static readonly HashSet<string> SkippedFolders = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "_site",
        "TestResults",
        "third_party",
    };

    [Fact]
    public void Every_file_the_ledger_names_still_exists()
    {
        var missing = ReadLedger()
            .Select(entry => entry.Path)
            .Where(path => !File.Exists(AtRoot(path)))
            .ToList();

        Assert.True(missing.Count == 0,
            "the ledger names files that are no longer in the checkout: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_ledger_path_appears_once()
    {
        var duplicates = ReadLedger()
            .GroupBy(entry => entry.Path, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        Assert.True(duplicates.Count == 0,
            "the ledger names the same file more than once: " + string.Join(", ", duplicates));
    }

    [Fact]
    public void Every_ledger_metric_matches_the_file_it_names()
    {
        var stale = new List<string>();

        foreach (var entry in ReadLedger())
        {
            var path = AtRoot(entry.Path);
            if (!File.Exists(path))
            {
                continue;
            }

            var actual = Measure(path);
            if (actual != entry.Metric)
            {
                stale.Add($"{entry.Path}: ledger says {entry.Metric}, the file is {actual}");
            }
        }

        Assert.True(stale.Count == 0, "the ledger metrics are stale: " + string.Join("; ", stale));
    }

    [Fact]
    public void Every_code_file_at_or_above_the_suggest_line_is_in_the_ledger()
    {
        var root = RepoPaths.Root;
        var known = ReadLedger()
            .Select(entry => entry.Path)
            .ToHashSet(StringComparer.Ordinal);

        var unlisted = CodeFiles(root)
            .Where(file => LineCount(file) >= CodeSuggestLine)
            .Select(file => Relative(root, file))
            .Where(path => !known.Contains(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        Assert.True(unlisted.Count == 0,
            $"code files at or above {CodeSuggestLine} lines need a ledger verdict: "
            + string.Join(", ", unlisted));
    }

    [Fact]
    public void Every_code_entry_past_the_debt_line_carries_a_verdict()
    {
        var unreviewed = new List<string>();

        foreach (var entry in ReadLedger())
        {
            var path = AtRoot(entry.Path);
            if (!IsCode(entry.Path) || !File.Exists(path) || LineCount(path) < CodeDebtLine)
            {
                continue;
            }

            if (!Verdicts.Any(verdict => entry.Review.StartsWith(verdict, StringComparison.Ordinal)))
            {
                unreviewed.Add($"{entry.Path} ({entry.Metric} lines): '{entry.Review}'");
            }
        }

        Assert.True(unreviewed.Count == 0,
            $"every code file past {CodeDebtLine} lines needs a keep/split/exempt verdict: "
            + string.Join("; ", unreviewed));
    }

    [Fact]
    public void The_ledger_drops_files_that_are_below_their_check_line()
    {
        var stale = new List<string>();

        foreach (var entry in ReadLedger())
        {
            var path = AtRoot(entry.Path);
            if (!File.Exists(path))
            {
                continue;
            }

            var code = IsCode(entry.Path);
            var metric = Measure(path);
            if (code && metric < CodeCheckLine)
            {
                stale.Add($"{entry.Path}: {metric} lines");
            }
            else if (!code && metric < DocumentCheckLine)
            {
                stale.Add($"{entry.Path}: {metric} bytes");
            }
        }

        Assert.True(stale.Count == 0,
            "the ledger still reviews files below their check line: " + string.Join(", ", stale));
    }

    /// <summary>
    /// Reads the ledger from the checkout. Blank lines separate groups; comments and the header are
    /// not entries. A malformed row fails here rather than being skipped, because a row that cannot
    /// be read is a row that silently stops guarding its file.
    /// </summary>
    private static List<LedgerEntry> ReadLedger()
    {
        var path = Path.Combine(RepoPaths.Root, ".githooks", "large-file-inventory.tsv");
        var entries = new List<LedgerEntry>();
        var lines = File.ReadAllLines(path);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var parts = line.Split('\t', 3);
            if (parts.Length < 3
                || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var metric))
            {
                throw new InvalidDataException(
                    $"{path}:{index + 1} is not <metric>\\t<path>\\t<review>");
            }

            entries.Add(new LedgerEntry(metric, parts[1], parts[2]));
        }

        return entries;
    }

    /// <summary>
    /// A code file is counted in lines; a document, page source or data file in bytes, exactly as
    /// the ledger's own header says. The bytes are the repository's, not the checkout's: CRLF is
    /// folded back to LF first, because Git stores these files with LF and the pre-commit check
    /// measures the staged blob. Without that fold a CRLF checkout and an LF clone would report
    /// different sizes for the same file.
    /// </summary>
    private static long Measure(string path) =>
        IsCode(path) ? LineCount(path) : ByteCount(path);

    /// <summary>All lines, blank ones included: the ledger counts what a task has to read.</summary>
    private static int LineCount(string path) => File.ReadAllLines(path).Length;

    /// <summary>The repository bytes: CRLF pairs folded to one LF, as Git stores a text file.</summary>
    private static long ByteCount(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var size = bytes.LongLength;

        for (var index = 1; index < bytes.Length; index++)
        {
            if (bytes[index - 1] == (byte)'\r' && bytes[index] == (byte)'\n')
            {
                size--;
            }
        }

        return size;
    }

    private static bool IsCode(string path) =>
        CodeExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> CodeFiles(string folder)
    {
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (IsCode(name) && !IsGenerated(name))
            {
                yield return file;
            }
        }

        foreach (var child in Directory.EnumerateDirectories(folder))
        {
            if (SkippedFolders.Contains(Path.GetFileName(child)))
            {
                continue;
            }

            foreach (var file in CodeFiles(child))
            {
                yield return file;
            }
        }
    }

    private static bool IsGenerated(string name) =>
        name.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase)
        || name.Contains(".generated.", StringComparison.OrdinalIgnoreCase);

    private static string AtRoot(string relative) => Path.Combine(RepoPaths.Root, relative);

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');

    private sealed record LedgerEntry(int Metric, string Path, string Review);
}
