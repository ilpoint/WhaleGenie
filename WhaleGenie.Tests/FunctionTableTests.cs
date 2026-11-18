using System;
using System.Collections.Generic;
using System.Linq;
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
            Assert.True(row.Groups[3].Value.Trim().Length > 0,
                $"{row.Groups[1].Value} has no line saying what it does");
        }
    }

    /// <summary>
    /// The manual's middle column is the signature said in Chinese, which is what the editor puts
    /// on the second line of a function's button. It is not written twice: the column is compared
    /// with what the editor makes of the same function, so the manual cannot drift away from the
    /// names the arguments go by.
    /// </summary>
    [Fact]
    public void The_manual_writes_the_signature_in_chinese_the_way_the_editor_does()
    {
        var wrong = new List<string>();
        foreach (Match row in Rows())
        {
            var function = Expression.Functions.First(
                one => string.Equals(one.Name, row.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
            var manual = row.Groups[2].Value.Trim();
            var editor = ExpressionText.ChineseSignature(function);
            if (!string.Equals(manual, editor, StringComparison.Ordinal))
            {
                wrong.Add($"{function.Name}: the manual says \"{manual}\" and the editor \"{editor}\"");
            }
        }

        Assert.Empty(wrong);
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
            var manual = row.Groups[3].Value.Replace("`", string.Empty, StringComparison.Ordinal).Trim();
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

    /// <summary>
    /// Every function carries a Chinese name, which is what the button says beside the code name and
    /// what a reader searches by. Adding one to the engine without its name — or deleting one and
    /// leaving the name behind — is caught here rather than by a gap in the list of buttons.
    /// </summary>
    [Fact]
    public void Every_function_has_a_name_of_its_own_and_no_name_is_left_over()
    {
        var wanted = Expression.Functions
            .Select(function => $"Expr.FnName.{function.Name}")
            .ToHashSet(StringComparer.Ordinal);
        var held = Strings.Chinese.Keys
            .Where(key => key.StartsWith("Expr.FnName.", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(wanted.Except(held).Order());
        Assert.Empty(held.Except(wanted).Order());
    }

    /// <summary>
    /// Every function also carries a sentence saying what calling it does in plain Chinese, which is
    /// the line the structure page shows under a formula. The places that sentence names have to be
    /// places the function has, or it would read out an argument a call cannot take: what is held
    /// under a function has to be wanted, and a sentence for a function that is gone may not sit in
    /// the table forever. The sentences that belong to the formula rather than to a function — the
    /// heading, a place still to fill, a list, and the words for true, false and nothing — are the
    /// ones named here by hand.
    /// </summary>
    [Fact]
    public void Every_function_says_what_it_does_in_plain_chinese()
    {
        string[] of_the_formula =
        [
            "Expr.Say.Header", "Expr.Say.Hole", "Expr.Say.List",
            "Expr.Say.Value.True", "Expr.Say.Value.False", "Expr.Say.Value.Null",
        ];
        var wanted = Expression.Functions
            .Select(function => $"Expr.Say.{function.Name}")
            .ToHashSet(StringComparer.Ordinal);
        var held = Strings.Chinese.Keys
            .Where(key => key.StartsWith("Expr.Say.", StringComparison.Ordinal)
                && !key.StartsWith("Expr.Say.Op.", StringComparison.Ordinal)
                && !of_the_formula.Contains(key, StringComparer.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Empty(wanted.Except(held).Order());
        Assert.Empty(held.Except(wanted).Order());

        var wrong = new List<string>();
        foreach (var function in Expression.Functions)
        {
            var sentence = Strings.Chinese[$"Expr.Say.{function.Name}"];
            foreach (Match slot in Regex.Matches(sentence, @"\{([0-9]+)\}"))
            {
                if (int.TryParse(slot.Groups[1].Value, out var at) && at >= function.Parameters.Count)
                {
                    wrong.Add($"{function.Name} says what argument {at} does");
                }
            }

            if (sentence.Contains("{...}", StringComparison.Ordinal)
                && function.MaxArguments <= function.Parameters.Count)
            {
                wrong.Add($"{function.Name} says it takes more arguments than it does");
            }
        }

        Assert.Empty(wrong.Order());
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
                var ownLabel = $"Expr.Arg.{function.Name}.{parameter.Name}.label";
                var sharedLabel = $"Expr.Arg.{parameter.Name}.label";
                wanted.Add(Strings.Chinese.ContainsKey(ownLabel) ? ownLabel : sharedLabel);
                if (!Strings.Chinese.ContainsKey(ownLabel) && !Strings.Chinese.ContainsKey(sharedLabel))
                {
                    missing.Add($"{ownLabel} (or {sharedLabel})");
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

                // What the box says while it is empty is a fact about the argument of its own: the
                // box standing for a regular expression cannot say "a number or a piece of text".
                var ownPut = $"Expr.Arg.{function.Name}.{parameter.Name}.put";
                var sharedPut = $"Expr.Arg.{parameter.Name}.put";
                if (Strings.Chinese.ContainsKey(ownPut) || Strings.Chinese.ContainsKey(sharedPut))
                {
                    wanted.Add(Strings.Chinese.ContainsKey(ownPut) ? ownPut : sharedPut);
                }
                else
                {
                    missing.Add($"{ownPut} (or {sharedPut})");
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

        return Regex.Matches(section, @"^\| `([A-Za-z]+)\([^`]*\)` \| (.+?) \| (.+) \|$",
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
