using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace WhaleGenie.Tests;

/// <summary>
/// The words that may sit on top of a class name are written twice: as a set here, and as the
/// `STATE` / `VARIANT` lines in the header of <c>Controls.axaml</c>, which is where both the styles
/// and <c>ControlsRegistryTests</c> read them. Two copies of the same list drift apart unless
/// something compares them, so this does.
///
/// The set is what keeps a class from wearing an invented word as if it were a state: adding one
/// here without a matching line in that header turns this red, and adding a line there without a
/// word here turns it red the other way.
/// </summary>
public class StyleVocabularyTests
{
    /// <summary>
    /// The only words allowed to sit on top of a class. Both halves are declared in the style
    /// file's header as well; <see cref="The_vocabulary_matches_the_registry"/> keeps the two
    /// copies honest.
    /// </summary>
    private static readonly HashSet<string> Vocabulary = new(StringComparer.Ordinal)
    {
        // States: things a control is, not colours it can be.
        "selected", "chosen", "problem", "Rejected", "Occupied", "Current", "Picked", "DropTarget",

        // Variants: colour tiers a family offers.
        "Success", "Warning", "Danger", "Info", "Disabled",
    };

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
