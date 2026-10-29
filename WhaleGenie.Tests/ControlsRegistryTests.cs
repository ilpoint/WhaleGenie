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

        // A state class (.selected) decorates the class it is written on; it is not a class of its
        // own and does not need four states of its own. Only classes that are not declared as states
        // are checked below.
        var states = Entries(controls, "STATE").Keys.Select(name => name.Split('.')[^1])
            .ToHashSet(StringComparer.Ordinal);

        var classes = Selectors(controls)
            .Select(selector => selector.Split(':')[0])
            .Where(selector => selector.Contains('.', StringComparison.Ordinal))
            .Select(selector => selector.Split(' ')[0])
            .Where(selector => !states.Contains(selector.Split('.')[^1]))
            .ToHashSet(StringComparer.Ordinal);

        var hovering = Selectors(controls)
            .Where(selector => selector.Contains(":pointerover", StringComparison.Ordinal))
            .Select(selector => selector.Split(':')[0].Split(' ')[0])
            .ToHashSet(StringComparer.Ordinal);

        var registered = Entries(controls, "LAYER1").Keys
            .Concat(Entries(controls, "STATIC").Keys)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(classes);

        // A class that narrows another one (`Button.Tab.Root.Compact`) inherits its states only when
        // the element really carries both class lists — `Classes="Tab Root Compact"` is matched by
        // `Button.Tab.Root`, a plain `Classes="Compact"` is not. So the ancestor has to be a class of
        // its own in this file; a bare substring is not an inheritance.
        var silent = classes
            .Where(name => !Covered(name, hovering) && !Covered(name, registered))
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

    /// <summary>Every window's markup, since a class is only real where an element carries it.</summary>
    private static string ReadFromViews()
    {
        var folder = Path.Combine(RepositoryRoot(), "WhaleGenie", "Views");
        if (!Directory.Exists(folder))
        {
            Assert.Fail($"the windows are not where the repository root says they are: {folder}");
        }

        return string.Join(
            "\n",
            Directory.EnumerateFiles(folder, "*.axaml").Select(File.ReadAllText));
    }

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
    /// <summary>
    /// Whether the name, or a class it really narrows, is in the set. Narrowing is not a substring
    /// match: the ancestor has to be a class declared in this file, so `Button.Compact` cannot ride
    /// on a registration for `Button` — nothing in the file declares `Button` as a class.
    /// </summary>
    private static bool Covered(string name, HashSet<string> names)
    {
        var parts = name.Split('.');
        for (var length = parts.Length; length > 1; length--)
        {
            if (names.Contains(string.Join('.', parts.Take(length))))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Wherever a narrowed class is used in a window, the element has to carry the whole class list,
    /// which is the only thing that makes the ancestor's selectors match. A use with just the leaf
    /// class would look styled in the file and be styled by nothing on screen.
    /// </summary>
    [Fact]
    public void A_narrowed_class_is_used_with_the_classes_it_narrows()
    {
        var classes = Selectors(Read("Controls.axaml"))
            .Select(selector => selector.Split(':')[0].Split(' ')[0])
            .Where(selector => selector.Count(character => character == '.') > 1)
            .ToHashSet(StringComparer.Ordinal);

        var usages = Regex.Matches(ReadFromViews(), @"Classes=""([^""]+)""")
            .Select(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        foreach (var name in classes)
        {
            var steps = name.Split('.');
            var leaf = steps[^1];
            var parents = steps.Skip(1).SkipLast(1).ToList();

            foreach (var used in usages.Where(list => list.Contains(leaf, StringComparer.Ordinal)))
            {
                foreach (var parent in parents)
                {
                    Assert.True(
                        used.Contains(parent, StringComparer.Ordinal),
                        $"{name} is used as Classes=\"{string.Join(' ', used)}\" without {parent}, "
                        + "so the class is styled by nothing");
                }
            }
        }
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
            Assert.Fail($"the repository root was not found above {here}; check where the tests are built");
        }

        return folder!.FullName;
    }
}
