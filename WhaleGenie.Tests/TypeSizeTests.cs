using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace WhaleGenie.Tests;

/// <summary>
/// The 700-line rule counts files, so a type split across partial files walks around it: the runner
/// is sixty-odd files, none near the limit, and one type of thirteen thousand lines. This counts by
/// the type a file declares, so the thing somebody actually has to read is what is measured.
/// </summary>
public class TypeSizeTests
{
    private readonly ITestOutputHelper _output;

    public TypeSizeTests(ITestOutputHelper output) => _output = output;

    /// <summary>The product projects; the localization tables are measured by the file hook.</summary>
    private static readonly string[] Projects =
    [
        "WhaleGenie", "WhaleGenie.Core", "WhaleGenie.Domain", "WhaleGenie.Mcp",
        "WhaleGenie.Platform.Windows", "WhaleGenie.GraphicsCapture",
    ];

    /// <summary>The type a file declares, after any attributes and modifiers.</summary>
    private static readonly Regex Declaration = new(
        @"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|file|static|sealed|abstract|partial|readonly|ref|unsafe|new)\s+)*(?:class|struct|record|interface|enum)\s+([A-Za-z_][A-Za-z0-9_]*)",
        RegexOptions.Multiline);

    /// <summary>
    /// Types over the limit that are being split, and the size each may not grow past. A budget only
    /// goes down: when one of these comes under the limit its entry goes away, and the next type to
    /// cross the line has to be argued for rather than slipped into the list.
    /// </summary>
    private static readonly Dictionary<string, int> Known = new(StringComparer.Ordinal)
    {
        ["WhaleGenie.Core/MacroRunner"] = 10_500,
        ["WhaleGenie/MacroEditorViewModel"] = 4_200,
        ["WhaleGenie/MacroEditorWindow"] = 3_850,
        ["WhaleGenie/MainWindow"] = 3_450,
    };

    [Fact]
    public void A_type_over_the_limit_is_recorded_and_cannot_grow_past_its_budget()
    {
        var sizes = Sizes();

        // A scan that found nothing would pass every rule below while measuring nothing at all.
        Assert.Contains("WhaleGenie.Core/MacroRunner", sizes.Keys);
        Assert.True(sizes.Count > 100, $"only {sizes.Count} types were read");

        foreach (var (name, lines) in sizes.Where(pair => pair.Value > 3_000)
                     .OrderByDescending(pair => pair.Value))
        {
            _output.WriteLine($"{lines,6} {name}");

            Assert.True(Known.TryGetValue(name, out var budget),
                $"{name} is {lines} lines in one type; split it or record a budget and why.");

            Assert.True(lines <= budget,
                $"{name} is {lines} lines, past its recorded budget of {budget}; "
                + "split it rather than raising the budget.");
        }
    }

    [Fact]
    public void A_type_that_has_come_under_the_limit_comes_off_the_list()
    {
        var sizes = Sizes();

        var stale = Known.Keys
            .Where(name => !sizes.TryGetValue(name, out var lines) || lines <= 3_000)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(stale.Count == 0,
            "these types are under the limit now, so their budgets no longer say anything: "
            + string.Join(", ", stale));
    }

    /// <summary>Every declared type's lines, summed over the partial files that carry it.</summary>
    private static Dictionary<string, int> Sizes()
    {
        var sizes = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var project in Projects)
        {
            foreach (var file in Directory.EnumerateFiles(Path.Combine(Repository(), project),
                         "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var text = File.ReadAllText(file);
                if (Declaration.Match(text) is not { Success: true } match)
                {
                    continue;
                }

                var name = project + "/" + match.Groups[1].Value;
                sizes[name] = sizes.GetValueOrDefault(name) + File.ReadAllLines(file).Length;
            }
        }

        return sizes;
    }

    private static string Repository()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "WhaleGenie.slnx")))
        {
            folder = folder.Parent;
        }

        return folder?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
