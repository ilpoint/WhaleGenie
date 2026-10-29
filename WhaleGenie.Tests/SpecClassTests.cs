using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace WhaleGenie.Tests;

/// <summary>
/// The spec is the list of classes; the style file is the implementation of it. They only stay in
/// agreement by being compared, and the comparison has to be automatic: sixteen classes were once
/// named in the table and never written, and it took two review rounds to notice.
///
/// Three things are checked. Nothing the spec names is missing; nothing is styled that the spec never
/// names, once the states and colour variants written on top of a named class are stripped; and the
/// words that may be stripped are a fixed list, checked against the file's own registry — without
/// that last one the second check would let any invented class through, because every extra segment
/// could just be called a state.
/// </summary>
public class SpecClassTests
{
    /// <summary>
    /// The only words allowed to sit on top of a class the spec names. Both halves are declared in
    /// the style file's header as well; <see cref="The_vocabulary_matches_the_registry"/> keeps the
    /// two copies honest.
    /// </summary>
    private static readonly HashSet<string> Vocabulary = new(StringComparer.Ordinal)
    {
        // States: things a control is, not colours it can be.
        "selected", "chosen", "problem", "Rejected", "Occupied", "Current",

        // Variants: colour tiers a family offers.
        "Success", "Warning", "Danger", "Info", "Disabled",
    };

    [Fact]
    public void Nothing_the_spec_names_is_missing()
    {
        var missing = SpecClasses().Except(StyledNames(), StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "these classes are in the spec but not in Controls.axaml: " + string.Join(", ", missing));
    }

    [Fact]
    public void Nothing_is_styled_that_the_spec_never_names()
    {
        var spec = SpecClasses();
        var invented = new List<string>();

        foreach (var name in StyledNames().OrderBy(name => name, StringComparer.Ordinal))
        {
            var parts = name.Split('.');
            var hit = 0;
            for (var length = parts.Length; length >= 2; length--)
            {
                if (spec.Contains(string.Join('.', parts.Take(length))))
                {
                    hit = length;
                    break;
                }
            }

            if (hit == 0)
            {
                invented.Add(name);
                continue;
            }

            // Everything stripped has to be a word from the vocabulary: `selected` on a named class
            // is a state, `Anything` on one would be an invented class wearing a state's clothes.
            var stripped = parts.Skip(hit).ToList();
            var unknown = stripped.Where(part => !Vocabulary.Contains(part)).ToList();
            if (unknown.Count > 0)
            {
                invented.Add(name + " (not a state: " + string.Join('.', unknown) + ")");
            }
        }

        Assert.True(
            invented.Count == 0,
            "these are styled but no line of the spec names them: " + string.Join(", ", invented));
    }

    [Fact]
    public void The_vocabulary_matches_the_registry()
    {
        var header = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "WhaleGenie", "Themes", "Controls.axaml"));

        var declared = Regex.Matches(header, @"^\s*(?:STATE|VARIANT)\s+(\S+)\s*$", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .Select(name => name.Split('.')[^1])
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(
            declared.OrderBy(word => word, StringComparer.Ordinal),
            Vocabulary.OrderBy(word => word, StringComparer.Ordinal));
    }

    /// <summary>
    /// The class names in the component tables — the first cell of each row, in backticks. The
    /// section runs from its heading to the next top-level one, so the examples quoted inside the
    /// decisions above are not read as declarations.
    /// </summary>
    private static HashSet<string> SpecClasses()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "doc", "ui-style.md"));
        var start = text.IndexOf("## 组件族表", StringComparison.Ordinal);
        Assert.True(start >= 0, "the spec has no component table section");
        var end = text.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        var section = end < 0 ? text[start..] : text[start..end];

        // The first cell sometimes holds a whole selector head — `ListBox.List.Pill ListBoxItem` —
        // so the backticked text is taken whole and then cut at the first space, exactly the way the
        // style side is cut. Otherwise the two sides count the same class differently, which is how
        // this file ended up with two totals for one thing.
        return Regex.Matches(section, @"^\|\s*`([^`]+)`", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value.Split(' ')[0])
            .Where(name => name.Contains('.', StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Every class name a selector starts with. The first space ends it, so `ListBox.List.Pill
    /// ListBoxItem` contributes `ListBox.List.Pill` whole; pseudo-classes, `/template/` and child
    /// combinators are dropped. Taking only the part before the first dot would count a family
    /// instead of a class, and that mismatch is what made two different totals for the same thing.
    /// </summary>
    private static HashSet<string> StyledNames()
    {
        var text = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "WhaleGenie", "Themes", "Controls.axaml"));

        return Regex.Matches(text, @"Selector=""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .Select(selector => selector.Replace("/template/", " ", StringComparison.Ordinal))
            .Select(selector => selector.Split('>')[0])
            .Select(selector => selector.Split(',')[0])
            .Select(selector => selector.Split(' ')[0])
            .Select(selector => selector.Split(':')[0])
            .Select(selector => selector.Trim())
            .Where(name => name.Contains('.', StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string RepositoryRoot([CallerFilePath] string here = "")
    {
        var folder = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "WhaleGenie", "Themes")))
        {
            folder = folder.Parent;
        }

        if (folder is null)
        {
            Assert.Fail($"the repository root was not found above {here}");
        }

        return folder!.FullName;
    }
}
