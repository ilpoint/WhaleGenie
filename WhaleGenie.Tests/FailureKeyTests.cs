using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Tests;

/// <summary>
/// The keys a macro can branch on are a contract with the person writing the macro. The constants
/// are the ledger, the manual is the copy they read, and the engine may not spell a key by hand
/// anywhere else.
/// </summary>
public class FailureKeyTests
{
    [Fact]
    public void The_failure_keys_match_the_manual_table()
    {
        var table = KeysUnder("### 可以分支的失败原因");

        Assert.Equal(RunKeys.All, table);
    }

    [Fact]
    public void Every_failure_constant_is_thrown_or_logged_by_the_engine()
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in CoreSources())
        {
            var source = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(source, @"\bRunKeys\.([A-Za-z0-9_]+)"))
            {
                if (match.Groups[1].Value != nameof(RunKeys.All))
                {
                    used.Add(match.Groups[1].Value);
                }
            }
        }

        var declared = RunKeys.All
            .Select(key => key["Run.".Length..])
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(declared, used.Order(StringComparer.Ordinal).ToList());
    }

    [Fact]
    public void The_engine_spells_no_vocabulary_key_by_hand()
    {
        foreach (var file in CoreSources())
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("\"Run.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Log.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("\"Status.", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Failure_log_and_status_keys_are_disjoint()
    {
        Assert.Empty(RunKeys.All.Intersect(LogKeys.All, StringComparer.Ordinal));
        Assert.Empty(RunKeys.All.Intersect(StatusKeys.All, StringComparer.Ordinal));
        Assert.Empty(LogKeys.All.Intersect(StatusKeys.All, StringComparer.Ordinal));

        Assert.All(RunKeys.All, key => Assert.StartsWith("Run.", key, StringComparison.Ordinal));
        Assert.All(LogKeys.All, key => Assert.StartsWith("Log.", key, StringComparison.Ordinal));
        Assert.Equal(["Status.Aborted", "Status.Finished", "Status.Stopped"], StatusKeys.All);
    }

    [Fact]
    public void The_failure_table_names_no_log_or_status_key()
    {
        var section = Section("### 可以分支的失败原因");

        Assert.DoesNotContain("`Log.", section, StringComparison.Ordinal);
        Assert.DoesNotContain("`Status.", section, StringComparison.Ordinal);
        Assert.DoesNotContain("`User.", section, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> KeysUnder(string heading)
    {
        var section = Section(heading);
        return
        [
            .. Regex.Matches(section, @"^\|\s*`(Run\.[A-Za-z0-9_]+)`\s*\|", RegexOptions.Multiline)
                .Select(match => match.Groups[1].Value)
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string Section(string heading)
    {
        var manual = File.ReadAllText(Path.Combine(Repository(), "doc", "manual.md"));
        var start = manual.IndexOf(heading, StringComparison.Ordinal);
        Assert.True(start >= 0, $"the manual has no {heading} section");
        var next = Regex.Match(manual[(start + heading.Length)..], @"\n#{2,3} ");
        return next.Success ? manual[start..(start + heading.Length + next.Index)] : manual[start..];
    }

    private static IEnumerable<string> CoreSources()
    {
        var core = Path.Combine(Repository(), "WhaleGenie.Core");
        return Directory.EnumerateFiles(core, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                              StringComparison.Ordinal)
                          && !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                              StringComparison.Ordinal)
                          && Path.GetFileName(file) != "RunVocabulary.cs");
    }

    private static string Repository()
    {
        for (var at = new DirectoryInfo(AppContext.BaseDirectory); at is not null; at = at.Parent)
        {
            if (File.Exists(Path.Combine(at.FullName, "WhaleGenie.slnx")))
            {
                return at.FullName;
            }
        }

        throw new InvalidOperationException("WhaleGenie.slnx was not found above the test binaries.");
    }
}
