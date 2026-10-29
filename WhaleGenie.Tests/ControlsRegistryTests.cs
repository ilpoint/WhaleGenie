using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace WhaleGenie.Tests;

/// <summary>
/// The rule that keeps <c>Controls.axaml</c> from growing silent gaps: every class either paints its
/// own hover state or is registered as one whose four states come from layer one. Both halves need a
/// check, because a missing hover state does not fail anything — the control simply falls back to the
/// stock Fluent appearance and the styles look correct.
///
/// The registry is text in the file's header, so these checks read the file rather than a second copy
/// of the list.
/// </summary>
public class ControlsRegistryTests
{
    [Fact]
    public void Every_class_paints_its_own_hover_or_is_registered()
    {
        var controls = Read("Controls.axaml");

        var classes = Selectors(controls)
            .Select(selector => selector.Split(':')[0])
            .Where(selector => selector.Contains('.', StringComparison.Ordinal))
            .Select(selector => selector.Split(' ')[0])
            .ToHashSet(StringComparer.Ordinal);

        var hovering = Selectors(controls)
            .Where(selector => selector.Contains(":pointerover", StringComparison.Ordinal))
            .Select(selector => selector.Split(':')[0].Split(' ')[0])
            .ToHashSet(StringComparer.Ordinal);

        var registered = Entries(controls, "LAYER1").Keys
            .Concat(Entries(controls, "STATIC").Keys)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(classes);

        var silent = classes
            .Where(name => !hovering.Contains(name) && !registered.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.True(
            silent.Count == 0,
            "these classes neither paint a hover state nor are registered as layer one's: "
            + string.Join(", ", silent));

        var stale = registered.Where(name => !classes.Contains(name)).ToList();
        Assert.True(
            stale.Count == 0,
            "these are registered but have no style: " + string.Join(", ", stale));
    }

    /// <summary>
    /// A registration has to be backed by keys that exist, or it becomes a way of saying "layer one
    /// does it" while nothing does it at all. The prefix is the control type, which is how the
    /// theme's own keys are named.
    /// </summary>
    [Fact]
    public void Every_registered_class_has_keys_of_its_family_in_layer_one()
    {
        var controls = Read("Controls.axaml");
        var layerOne = Regex.Matches(Read("FluentKeys.axaml"), @"x:Key=""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToList();

        var registered = Entries(controls, "LAYER1");
        Assert.NotEmpty(registered);

        foreach (var (name, family) in registered)
        {
            Assert.True(
                layerOne.Any(key => key.StartsWith(family, StringComparison.Ordinal)),
                $"{name} says layer one paints it, but FluentKeys.axaml has no {family}* key");
        }
    }

    /// <summary>
    /// One registry line: a class and, for <c>LAYER1</c>, the prefix its keys are named with. Fluent
    /// does not always name its keys after the control — the text box's are <c>TextControl*</c> — so
    /// the prefix is written out rather than guessed, and a guess that is wrong shows up here.
    /// </summary>
    private static Dictionary<string, string> Entries(string xaml, string kind)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var pattern = @"^\s*" + kind + @"\s+(\S+)(?:\s+(\S+))?\s*$";
        foreach (Match match in Regex.Matches(xaml, pattern, RegexOptions.Multiline))
        {
            var name = match.Groups[1].Value;
            var family = match.Groups[2].Success ? match.Groups[2].Value : name.Split('.')[0];
            entries[name] = family;
        }

        return entries;
    }

    private static IReadOnlyList<string> Selectors(string xaml)
        => Regex.Matches(xaml, @"Selector=""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToList();

    private static string Read(string file)
    {
        var path = Path.Combine(RepositoryRoot(), "WhaleGenie", "Themes", file);
        if (!File.Exists(path))
        {
            Assert.Fail($"the styles are not where the repository root says they are: {path}");
        }

        return File.ReadAllText(path);
    }

    /// <summary>
    /// Where this file's own path says the repository is: the checks run out of bin, where the
    /// dictionaries are copied but the repository layout is not.
    /// </summary>
    private static string RepositoryRoot([CallerFilePath] string here = "")
    {
        var folder = new DirectoryInfo(Path.GetDirectoryName(here)!);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "WhaleGenie", "Themes")))
        {
            folder = folder.Parent;
        }

        if (folder is null)
        {
            Assert.Fail($"the repository root was not found above {here}; check where the tests are built");
        }

        return folder!.FullName;
    }
}
