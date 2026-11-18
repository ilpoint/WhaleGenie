using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using Xunit;
using Xunit.Abstractions;

namespace WhaleGenie.Tests;

/// <summary>
/// Reports class names a window puts on an element with nothing to style them. A name with no
/// selector is invisible twice: nothing fails, and the element simply keeps the stock look. Two real
/// ones turned up this way (five buttons in `BreakpointWindow` / `MainWindow`, six lines in
/// `MacroInterfaceView` whose host is constructed in code, so no XAML search finds it).
///
/// A class declared in `App.axaml` used to be one of those invisible names — the scan read
/// `Controls.axaml` and each window, and the dock had been written above both. Application-level
/// styles belong in `Controls.axaml`; `App.axaml` is read here as well, so a style that drifts back
/// up there is still seen rather than needing a whitelist entry that quietly excuses it.
///
/// **It reports; it does not fail on findings.** The check cannot tell a class chain from a typo
/// without being told which chains exist, so it still produces false positives — and a check that
/// goes red on a false positive gets a whitelist on the first day and gets deleted on the second,
/// after which it reports nothing at all.
/// </summary>
public partial class ClassAuditTests
{
    private readonly ITestOutputHelper _output;

    public ClassAuditTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Names known to be fine even though no selector matches them here, each with the reason. Being
    /// on this list is a statement about the repository, not about the check — so it is checked too.
    /// Empty at the moment: the one entry it had (Dock, styled from App.axaml) is a class the scan
    /// now reads, so it is covered rather than excused.
    /// </summary>
    private static readonly Dictionary<string, string> Known =
        new(StringComparer.Ordinal);

    /// <summary>
    /// The audit is only worth its runtime if it read the windows. Every scan below walks
    /// `WhaleGenie/Views`; a scan pointed at a folder that no longer holds them — a move, a rename,
    /// a second folder — finds nothing and reports nothing, which reads exactly like a clean bill.
    /// </summary>
    [Fact]
    public void The_audit_reads_the_windows_it_claims_to()
    {
        var views = Path.Combine(RepositoryRoot(), "WhaleGenie", "Views");
        var markup = Directory.EnumerateFiles(views, "*.axaml", SearchOption.AllDirectories).Count();
        var behind = Directory.EnumerateFiles(views, "*.cs", SearchOption.AllDirectories).Count();

        Assert.True(markup >= 40, $"only {markup} markup files under {views}");
        Assert.True(behind >= 100, $"only {behind} code-behind files under {views}");
    }

    [Fact]
    public void Classes_without_a_style_are_reported_and_the_whitelist_is_checked()
    {
        var findings = Unstyled().Where(name => !Known.ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        foreach (var finding in findings)
        {
            _output.WriteLine("no style for: " + finding);
        }

        _output.WriteLine($"reported {findings.Count}, whitelisted {Known.Count}");

        // A whitelist entry that no longer appears is stale: the reason written beside it may have
        // stopped being true, and nobody would ever look again.
        var stale = Known.Keys.Where(name => !Unstyled().Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.True(
            stale.Count == 0,
            "these whitelist entries no longer appear, so their reasons need a second look: "
            + string.Join(", ", stale));
    }

    /// <summary>
    /// Every class token whose chain nothing declares. A token counts as covered when some declared
    /// chain of the same control type is a subset of the classes on that element — that is what makes
    /// `Key Small` covered by `Button.Key.Small`, and a lone `Cell` not covered by `TextBox.Field.Cell`.
    /// </summary>
    private static HashSet<string> Unstyled()
    {
        var root = RepositoryRoot();
        var app = Declared(File.ReadAllText(Path.Combine(root, "WhaleGenie", "Themes", "Controls.axaml")));
        foreach (var (type, chains) in Declared(File.ReadAllText(Path.Combine(root, "WhaleGenie", "App.axaml"))))
        {
            app[type] = app.TryGetValue(type, out var existing)
                ? [.. existing, .. chains]
                : chains;
        }

        var findings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "WhaleGenie", "Views"), "*.axaml", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            var local = Declared(text);
            var hosts = HostStyles(path, root);

            foreach (Match tag in Regex.Matches(text, @"<([A-Za-z][\w:.]*)((?:[^>""']|""[^""]*""|'[^']*')*)>"))
            {
                var type = tag.Groups[1].Value.Split('|', ':')[^1];
                var classes = Regex.Match(tag.Groups[2].Value, @"Classes=""([^""]+)""");
                if (!classes.Success)
                {
                    continue;
                }

                var used = classes.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                foreach (var name in used)
                {
                    if (Covered(type, used, name, app)
                        || Covered(type, used, name, local)
                        || Covered(type, used, name, hosts))
                    {
                        continue;
                    }

                    findings.Add(name);
                }
            }
        }

        return findings;
    }

    private static bool Covered(
        string type,
        string[] used,
        string name,
        IReadOnlyDictionary<string, IEnumerable<string[]>> declared)
        => declared.TryGetValue(type, out var chains)
            && chains.Any(chain => chain.Contains(name, StringComparer.Ordinal)
                && chain.All(step => used.Contains(step, StringComparer.Ordinal)));

    /// <summary>
    /// The styles a `UserControl` inherits from the window that hosts it. The host is looked for in
    /// the markup **and** in code: `MacroInterfaceView` is created with `new` inside
    /// `MacroEditorWindow.axaml.cs`, so searching only the XAML finds no host at all.
    /// </summary>
    private static IReadOnlyDictionary<string, IEnumerable<string[]>> HostStyles(string path, string root)
    {
        var text = File.ReadAllText(path);
        if (!text.Contains("<UserControl", StringComparison.Ordinal))
        {
            return new Dictionary<string, IEnumerable<string[]>>();
        }

        var view = Path.GetFileNameWithoutExtension(path);
        var merged = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        foreach (var candidate in Directory.EnumerateFiles(Path.Combine(root, "WhaleGenie", "Views"), "*", SearchOption.AllDirectories))
        {
            var candidateText = File.ReadAllText(candidate);
            if (!candidateText.Contains(view, StringComparison.Ordinal)
                || !candidateText.Contains("<Window", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var (type, chains) in Declared(candidateText))
            {
                if (!merged.TryGetValue(type, out var list))
                {
                    merged[type] = list = [];
                }

                list.AddRange(chains);
            }
        }

        return merged.ToDictionary(pair => pair.Key, pair => (IEnumerable<string[]>)pair.Value);
    }

    /// <summary>The class chains a style file declares, by control type.</summary>
    private static Dictionary<string, IEnumerable<string[]>> Declared(string xaml)
    {
        var chains = new Dictionary<string, List<string[]>>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(xaml, @"Selector=""([^""]+)"""))
        {
            var head = match.Groups[1].Value
                .Replace("/template/", " ", StringComparison.Ordinal)
                .Split('>')[0].Split(',')[0].Split(' ')[0].Split(':')[0].Trim();
            var parts = head.Split('.');
            if (parts.Length < 2)
            {
                continue;
            }

            if (!chains.TryGetValue(parts[0], out var list))
            {
                chains[parts[0]] = list = [];
            }

            list.Add(parts.Skip(1).ToArray());
        }

        return chains.ToDictionary(pair => pair.Key, pair => (IEnumerable<string[]>)pair.Value);
    }
}
