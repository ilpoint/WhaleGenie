using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Tests;

/// <summary>
/// Where the web interface ends and the program begins. The bridge handlers answer pages over a
/// plain text pipe, so they must not know the web control; and the pages draw only the words the
/// program hands them, so a user-visible sentence in a page source is a word the tables never
/// reviewed.
/// </summary>
public class WebUiBoundaryTests
{
    /// <summary>
    /// The web control's own types (every WebView2 adapter type carries one of these two names)
    /// belong to the Web/ folder, which is the one adapter between them. A handler that names them
    /// cannot be tested without a browser, which is the failure this check exists to prevent.
    /// Other Avalonia platform types — the file pickers, for one — are ordinary interface tools
    /// and may be named.
    /// </summary>
    [Fact]
    public void The_bridge_handlers_do_not_know_the_web_control()
    {
        var offenders = Directory.EnumerateFiles(
                Path.Combine(RepoPaths.Source("WhaleGenie"), "Bridge"), "*.cs",
                SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("NativeWebView", StringComparison.Ordinal)
                           || File.ReadAllText(file).Contains("WindowsWebView2", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .ToList();

        Assert.True(offenders.Count == 0,
            "these bridge handlers know the web control: " + string.Join(", ", offenders));
    }

    /// <summary>
    /// The pages and the app shell may not carry a word of their own: every user-visible string
    /// comes from the word tables through the bridge. The mock, which pretends to be the host,
    /// holds a fixture copy of the words and is exempt. Comments are stripped before looking:
    /// the rule is about what a page can draw, not about how its code is explained.
    /// </summary>
    [Fact]
    public void The_web_pages_write_no_user_text_of_their_own()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(RepoPaths.Root, "webui", "src"), "*.ts", SearchOption.AllDirectories))
        {
            // The mock host holds fixture copies of user-visible words; the editor mock is split
            // across mock-editor*.ts and stays the same kind of fixture.
            var name = Path.GetFileName(file);
            if (name == "mock.ts"
                || name.StartsWith("mock-editor", StringComparison.Ordinal)
                || name.StartsWith("mock-value-editor", StringComparison.Ordinal))
            {
                continue;
            }

            var text = StripComments(File.ReadAllText(file));
            if (text.Any(character => character >= '\u4e00' && character <= '\u9fff'))
            {
                offenders.Add(Path.GetRelativePath(RepoPaths.Root, file));
            }
        }

        Assert.True(offenders.Count == 0,
            "these page sources carry Chinese text instead of a word-table key: "
            + string.Join(", ", offenders));
    }

    /// <summary>Line comments are cut off; block comments go whole. A "//" inside a string only
    /// makes the check see less of that line, which errs in the direction the check wants.</summary>
    private static string StripComments(string text)
    {
        var withoutBlock = Regex.Replace(text, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return string.Join('\n', withoutBlock.Split('\n').Select(line =>
        {
            var index = line.IndexOf("//", StringComparison.Ordinal);
            return index >= 0 ? line[..index] : line;
        }));
    }

    /// <summary>
    /// A key a page names has to exist in the word tables of both languages, or the page would
    /// draw the key name itself to the user. The pages name keys as literal strings, so the
    /// sources can be read for them here, against the same tables the C# interface binds to.
    /// </summary>
    [Fact]
    public void Every_key_the_pages_name_exists_in_the_word_tables()
    {
        var matches = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(
            Path.Combine(RepoPaths.Root, "webui", "src"), "*.ts", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);

            // Every direct lookup, whatever domain it belongs to. The old scan only looked at
            // Shell./Main. and quietly missed the MCP and editor keys the pages already used.
            foreach (Match match in Regex.Matches(text, @"\bt\('([^']+)'\)"))
            {
                matches.Add(match.Groups[1].Value);
            }

            // Keys kept in a table and looked up indirectly (the navigation labels, the area map,
            // the mock fixture) are still literal keys; a quoted dotted word is what they look like.
            foreach (Match match in Regex.Matches(text,
                @"'((?:Shell|Main|Mcp|Editor|Common|GlobalVariables|Settings|Run|Recover)\.[A-Za-z0-9.]+)'"))
            {
                matches.Add(match.Groups[1].Value);
            }
        }

        // The dock labels are built as a template (Main.Dock.<view>) and cannot be scanned as a
        // literal; the view names are the fixed half of that key.
        foreach (var view in Enum.GetNames<MacroView>())
        {
            matches.Add($"Main.Dock.{view}");
        }

        Assert.NotEmpty(matches);

        var missing = matches
            .Where(key => Strings.Snapshot().TryGetValue(key, out var value) is false || value == key)
            .ToList();

        Assert.True(missing.Count == 0,
            "the pages name word-table keys that do not exist: " + string.Join(", ", missing));
    }
}
