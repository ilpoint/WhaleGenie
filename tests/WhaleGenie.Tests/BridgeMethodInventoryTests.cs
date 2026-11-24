using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using WhaleGenie;
using WhaleGenie.Bridge;
using WhaleGenie.Contracts;

namespace WhaleGenie.Tests;

/// <summary>
/// The page's commands and the program's registered methods are one list, checked from both ends:
/// a handler that is gone from the program but still called by a page is a dead button, and a
/// handler the program registered while no page names it is a door with no sign — allowed only
/// while it is written down below with the work that builds its caller. The page may also use
/// bridge.ready, which is the client handshake and not a registered method.
/// </summary>
public class BridgeMethodInventoryTests
{
    private static readonly HashSet<string> Expected = [.. BridgeMethods.All];

    /// <summary>
    /// The registered methods no real page names yet, each with the work that will build its
    /// caller. A door is a promise with a date on it, so every entry states the package that
    /// closes it; the checks below fail when a name leaves BridgeMethods.All (a renamed method
    /// must not leave its old door behind) and when a page starts calling one (the list is then
    /// stale and has to go), so this table cannot outlive its reasons.
    /// </summary>
    private static readonly Dictionary<string, string> DoorsWithoutCallers = new(StringComparer.Ordinal);

    private static readonly Regex[] CallSitePatterns =
    [
        // The old pattern only saw .call('method'); it missed .call<T>('method') and the method
        // names handed to the editor's command helpers. Scan call sites instead of every dotted
        // literal so action-catalogue keys such as window.activate do not look like bridge methods.
        new(@"\.call(?:\s*<[^>]*>)?\s*\(\s*['""]([a-zA-Z.]+)['""]", RegexOptions.CultureInvariant),
        new(@"\.(?:snapshotCommand|actionCommand|declaration|trigger)\(\s*['""]([a-zA-Z.]+)['""]",
            RegexOptions.CultureInvariant),
    ];

    /// <summary>A quoted dotted name, for the places where the literal itself is the claim.</summary>
    private static readonly Regex MethodLiteralPattern =
        new(@"'([a-zA-Z]+\.[a-zA-Z.]+)'", RegexOptions.CultureInvariant);

    [Fact]
    public void The_registered_methods_are_the_page_inventory()
    {
        using var services = WindowedServices.Build();
        var server = services.GetRequiredService<BridgeServer>();

        var missing = Expected.Except(server.Methods).Order(StringComparer.Ordinal).ToList();
        var extra = server.Methods.Except(Expected).Order(StringComparer.Ordinal).ToList();
        Assert.True(missing.Count == 0 && extra.Count == 0,
            "missing: " + string.Join(", ", missing) + " | extra: " + string.Join(", ", extra));
    }

    [Fact]
    public void The_page_calls_only_registered_methods()
    {
        Assert.Empty(MethodsThePagesName().Except(Expected).Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// A method the program registers and no page ever names is a door with no sign: it can be
    /// reached over the pipe but no click leads to it. Either a page names it, or it is written
    /// down below with the work that builds the caller.
    /// </summary>
    [Fact]
    public void Every_registered_method_a_page_never_calls_is_a_declared_door()
    {
        var named = MethodsThePagesName();

        // The door table is read against the registry: a method renamed or removed leaves a name
        // here that no longer means anything, and a door for nothing hides the next real one.
        var unknown = DoorsWithoutCallers.Keys
            .Where(method => !Expected.Contains(method))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(unknown.Count == 0,
            "these doors are not registered methods: " + string.Join(", ", unknown));

        // A door a page now knocks on is a list entry that has expired; keeping it would let the
        // method be deleted again without anyone noticing that a page still uses it.
        var stale = DoorsWithoutCallers.Keys
            .Where(named.Contains)
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(stale.Count == 0,
            "these doors are already called by a page: " + string.Join(", ", stale));

        var unexplained = Expected
            .Where(method => !named.Contains(method) && !DoorsWithoutCallers.ContainsKey(method))
            .Order(StringComparer.Ordinal)
            .ToList();
        Assert.True(unexplained.Count == 0,
            "these registered methods have no page caller and no door entry: "
            + string.Join(", ", unexplained));
    }

    /// <summary>
    /// Every bridge method name a real page claims. A .call or a command helper is read as one
    /// call site; a quoted literal that names a registered method is read as the same claim,
    /// because the pages also hand names over through typed constants (BridgeMethod variables,
    /// the rule editor's kind-to-method choice) instead of writing them at the call. The mock
    /// family is skipped: its files start with mock and dispatch on the method names to pretend
    /// they are the host, so counting that echo would answer almost every door with a fixture.
    /// The generated union is skipped for the same reason — it restates the registry rather than
    /// calling it.
    /// </summary>
    private static HashSet<string> MethodsThePagesName()
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in RealPageSources())
        {
            var text = StripComments(File.ReadAllText(file));
            foreach (var pattern in CallSitePatterns)
            {
                foreach (Match match in pattern.Matches(text))
                {
                    named.Add(match.Groups[1].Value);
                }
            }

            foreach (Match match in MethodLiteralPattern.Matches(text))
            {
                if (Expected.Contains(match.Groups[1].Value))
                {
                    named.Add(match.Groups[1].Value);
                }
            }
        }

        // The handshake belongs to the client, not to the program's registry.
        named.Remove("bridge.ready");
        return named;
    }

    /// <summary>The page sources that really run: the mock host and the generated files are not
    /// the interface, and each of them names methods without anyone clicking.</summary>
    private static IEnumerable<string> RealPageSources()
    {
        var pages = Path.Combine(RepoPaths.Root, "webui", "src");
        foreach (var file in Directory.EnumerateFiles(pages, "*.ts", SearchOption.AllDirectories))
        {
            // Every file in the mock family starts with mock — the word-table check exempts the
            // same family — and between them they dispatch on almost every registered method.
            if (Path.GetFileName(file).StartsWith("mock", StringComparison.Ordinal))
            {
                continue;
            }

            var parts = Path.GetRelativePath(pages, file)
                .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Contains("generated"))
            {
                continue;
            }

            yield return file;
        }
    }

    /// <summary>Line comments are cut off; block comments go whole. A name a page explains in a
    /// comment is not a name it calls, and a "//" inside a string only makes the check see less of
    /// that line, which errs in the direction the check wants.</summary>
    private static string StripComments(string text)
    {
        var withoutBlock = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join('\n', withoutBlock.Split('\n').Select(line =>
        {
            var index = line.IndexOf("//", StringComparison.Ordinal);
            return index >= 0 ? line[..index] : line;
        }));
    }
}
