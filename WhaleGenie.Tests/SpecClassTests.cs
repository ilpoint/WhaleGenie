using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace WhaleGenie.Tests;

/// <summary>
/// The spec is the list of classes; the style file is the implementation of it. The two are only
/// worth anything while they agree, and they agree by hand today — a class named in the table and
/// never written is invisible until somebody goes looking, which is what happened once already
/// (sixteen classes were missing and nobody noticed for two rounds).
///
/// So the two lists are compared here. Only the component tables are read: the "裁定" sections quote
/// class names as examples, and taking those would make the spec agree with itself rather than with
/// the styles.
/// </summary>
public class SpecClassTests
{
    [Fact]
    public void Every_class_the_spec_names_has_a_style()
    {
        var missing = SpecClasses().Except(StyledNames(), StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "these classes are in the spec but not in Controls.axaml: " + string.Join(", ", missing));
    }

    /// <summary>
    /// The other direction: a class the styles invented is a class nobody agreed on. A state or a
    /// narrowing of a specced class is fine — `Border.Card.Selectable.selected` is a state of
    /// `Border.Card.Selectable`, and `Button.Rail.selected` of `Button.Rail`.
    /// </summary>
    [Fact]
    public void Every_styled_class_is_one_the_spec_names_or_a_state_of_one()
    {
        var spec = SpecClasses();
        var invented = StyledNames()
            .Where(name => !spec.Contains(name) && !Narrows(name, spec))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            invented.Count == 0,
            "these are styled but no line of the spec names them: " + string.Join(", ", invented));
    }

    private static bool Narrows(string name, HashSet<string> spec)
    {
        var parts = name.Split('.');
        for (var length = parts.Length - 1; length > 1; length--)
        {
            if (spec.Contains(string.Join('.', parts.Take(length))))
            {
                return true;
            }
        }

        return false;
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

        return Regex.Matches(section, @"^\|\s*`([A-Za-z][\w.]*)`", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .Where(name => name.Contains('.', StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Every class name the style file writes a selector for.</summary>
    private static HashSet<string> StyledNames()
    {
        var text = File.ReadAllText(Path.Combine(RepositoryRoot(), "WhaleGenie", "Themes", "Controls.axaml"));
        return Regex.Matches(text, @"Selector=""([^""]+)""")
            .Select(match => match.Groups[1].Value.Split(':')[0].Split(' ')[0])
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
