using System.Text.RegularExpressions;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;

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

    /// <summary>
    /// The editor shows the same sentence on the tooltip of a function's button, so the manual and the
    /// tooltip have to be one text rather than two that drift: what the manual writes for a function is
    /// what the table holds for it. The manual writes it with code marks where they help the reader —
    /// <c>yyyy-MM-dd</c> reads better as code — and a tooltip carries plain text, so the comparison is
    /// on the words rather than on those marks.
    /// </summary>
    [Fact]
    public void The_tooltip_says_what_the_manual_says()
    {
        var wrong = new List<string>();
        foreach (Match row in Rows())
        {
            var name = row.Groups[1].Value;
            var manual = row.Groups[2].Value.Replace("`", string.Empty, StringComparison.Ordinal).Trim();
            if (!Strings.Chinese.TryGetValue($"Expr.Fn.{name}", out var table))
            {
                wrong.Add($"{name} has no line in the text table");
            }
            else if (!string.Equals(manual, table, StringComparison.Ordinal))
            {
                wrong.Add($"{name}: the manual says \"{manual}\" and the table says \"{table}\"");
            }
        }

        Assert.Empty(wrong);
    }

    /// <summary>
    /// Every function has one of those lines and nothing holds a line for a function that is gone, so
    /// adding one to the engine without its sentence — or deleting one and leaving the sentence behind
    /// — is caught here rather than by a reader noticing an English tooltip.
    /// </summary>
    [Fact]
    public void Every_function_has_a_line_and_no_line_is_left_over()
    {
        var wanted = Expression.Functions
            .Select(function => $"Expr.Fn.{function.Name}")
            .ToHashSet(StringComparer.Ordinal);
        var held = Strings.Chinese.Keys
            .Where(key => key.StartsWith("Expr.Fn.", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(wanted.Except(held).Order());
        Assert.Empty(held.Except(wanted).Order());

        // The English half of a tooltip is the sentence the engine carries, so it has to be there too.
        var silent = Expression.Functions
            .Where(function => string.IsNullOrWhiteSpace(function.Description))
            .Select(function => function.Name)
            .Order()
            .ToList();
        Assert.Empty(silent);
    }

    /// <summary>The function names the manual's table spells out.</summary>
    private static List<string> ManualNames()
        => [.. Rows().Select(row => row.Groups[1].Value)];

    /// <summary>
    /// The arguments of a function are what the structure page names its lines after, and a line
    /// with no name and no sentence is a place a reader has to guess at. What a name means is
    /// written once for every function that uses it, and the few arguments that mean something
    /// narrower carry the function's own name in the key, so what every argument needs is a line
    /// under one of the two — and what is held has to be wanted, or a sentence for a function that
    /// is gone would sit in the table forever.
    /// </summary>
    [Fact]
    public void Every_argument_of_a_function_is_named_and_explained()
    {
        var wanted = new HashSet<string>(StringComparer.Ordinal);
        var missing = new List<string>();
        foreach (var function in Expression.Functions)
        {
            foreach (var parameter in function.Parameters)
            {
                wanted.Add($"Expr.Arg.{parameter.Name}.label");
                if (!Strings.Chinese.ContainsKey($"Expr.Arg.{parameter.Name}.label"))
                {
                    missing.Add($"Expr.Arg.{parameter.Name}.label");
                }

                var own = $"Expr.Arg.{function.Name}.{parameter.Name}.hint";
                var shared = $"Expr.Arg.{parameter.Name}.hint";
                if (Strings.Chinese.ContainsKey(own) || Strings.Chinese.ContainsKey(shared))
                {
                    wanted.Add(Strings.Chinese.ContainsKey(own) ? own : shared);
                }
                else
                {
                    missing.Add($"{own} (or {shared})");
                }
            }
        }

        Assert.Empty(missing.Order());

        var held = Strings.Chinese.Keys
            .Where(key => key.StartsWith("Expr.Arg.", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Empty(held.Except(wanted).Order());
    }

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
