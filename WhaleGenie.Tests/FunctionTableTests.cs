using System.Text.RegularExpressions;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Tests;

/// <summary>
/// The manual carries the list of built-in expression functions, and the editor offers the same
/// list as buttons. Two lists of the same thing drift apart the moment one of them is added to, so
/// this reads the manual back and compares the names with the table the language itself keeps.
/// </summary>
public class FunctionTableTests
{
    [Fact]
    public void The_manual_lists_every_function_the_language_has_and_nothing_else()
    {
        var names = ManualNames();
        var kept = Expression.Functions.Select(function => function.Name).ToList();

        Assert.Empty(kept.Except(names, StringComparer.Ordinal).Order());
        Assert.Empty(names.Except(kept, StringComparer.Ordinal).Order());
    }

    [Fact]
    public void Every_row_of_the_manual_says_what_its_function_does()
    {
        // A row is the signature and a line of Chinese, which is what the table is for: the manual
        // is the only place a function is explained to whoever is writing a macro.
        foreach (Match row in Rows())
        {
            Assert.True(row.Groups[2].Value.Trim().Length > 0,
                $"{row.Groups[1].Value} has no line saying what it does");
        }
    }

    /// <summary>The function names the manual's table spells out.</summary>
    private static List<string> ManualNames()
        => [.. Rows().Select(row => row.Groups[1].Value)];

    /// <summary>Every row of the manual's function table: the name written, and the line beside it.</summary>
    private static IEnumerable<Match> Rows()
    {
        // Line endings are the checkout's business, not the text's, and this is read a line at a
        // time: a checkout that writes CRLF would otherwise find no rows at all and the two checks
        // below would compare the table against nothing.
        var manual = File.ReadAllText(Path.Combine(Repository(), "doc", "manual.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var start = manual.IndexOf("## 表达式函数一览", StringComparison.Ordinal);
        Assert.True(start >= 0, "the manual no longer has a section listing the expression functions");

        // Up to the next heading, so a table somewhere else in the manual cannot slip in as one of
        // the function rows.
        var next = manual.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = next < 0 ? manual[start..] : manual[start..next];

        return Regex.Matches(section, @"^\| `([A-Za-z]+)\([^`]*\)` \| (.+) \|$",
            RegexOptions.Multiline);
    }

    /// <summary>The repository root, found by walking up from the test binaries.</summary>
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
