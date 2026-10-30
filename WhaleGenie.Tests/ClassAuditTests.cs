using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Abstractions;

namespace WhaleGenie.Tests;

/// <summary>
/// Reports class names a window puts on an element with nothing to style them. A name with no
/// selector is invisible twice: nothing fails, and the element simply keeps the stock look. Two real
/// ones turned up this way (five buttons in `BreakpointWindow` / `MainWindow`, six lines in
/// `MacroInterfaceView` whose host is constructed in code, so no XAML search finds it).
///
/// **It reports; it does not fail on findings.** The check cannot tell a class chain from a typo
/// without being told which chains exist, so it still produces false positives — and a check that
/// goes red on a false positive gets a whitelist on the first day and gets deleted on the second,
/// after which it reports nothing at all. What it does fail on is its own whitelist going stale: if
/// an entry stops appearing, that line has to be looked at again.
/// </summary>
public class ClassAuditTests
{
    private readonly ITestOutputHelper _output;

    public ClassAuditTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// Names known to be fine even though no selector matches them here, each with the reason. Being
    /// on this list is a statement about the repository, not about the check — so it is checked too.
    /// </summary>
    private static readonly Dictionary<string, string> Known =
        new(StringComparer.Ordinal)
        {
            ["Dock"] = "App.axaml 的 ListBox.Dock 是全局样式，两个窗口都用它",
        };

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

        var findings = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "WhaleGenie", "Views"), "*.axaml"))
        {
            var text = File.ReadAllText(path);
            var local = Declared(text);
            var hosts = HostStyles(path, root);

            foreach (Match tag in Regex.Matches(text, @"<([A-Za-z][\w:.]*)((?:[^>""']|""[^""]*""|'[^']*')*)>"))
            {
                var type = tag.Groups[1].Value;
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
        foreach (var candidate in Directory.EnumerateFiles(Path.Combine(root, "WhaleGenie", "Views")))
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

    // ── 第二条：定义了没人用 ────────────────────────────────────────────────
    //
    // 与上面那条成对。上面查"挂了类但没样式"（屏幕上少一段，报告扫描能发现）；这一条查
    // "写了样式但没人挂"（代码里多一段没人走的，**没有任何检查会发现**）。
    //
    // 实见：`RunWindow` 32 条里有 6 条从没被用过——`VarRow`、`VarInput` 全仓零出现，
    // `Button.Tab` 的消费者在另一个窗、够不着。它们能活到今天，就是因为缺这一条。
    //
    // 消费者要数三处，漏第三处会把"测试在用的类"判成死的：
    //   ① XAML 的 `Classes="…"`（含组合写法）与 `Classes.名字=`（绑定式加类）
    //   ② `.cs` 里的 `Classes.Add("…")` / `Classes = { "…" }` / `Classes.Set("…")`
    //   ③ 测试里的 `Contains("…")`（按类名找元素）
    //
    // **只报告，不失败**：静态数不出"运行时拼出来的类名"，误报会让人把检查关掉。
    // 唯一会失败的是白名单自己过期——与上面那条同一个理由。

    /// <summary>
    /// 已知"没有消费者也没问题"的类，每条必须写原因。与上一条的白名单同规矩：**它也在被检查**——
    /// 条目不再出现时同样要报，因为那说明原因可能已经不成立。
    /// </summary>
    private static readonly Dictionary<string, string> KnownUnused =
        new(StringComparer.Ordinal);

    [Fact]
    public void Styles_without_a_consumer_are_reported_and_the_whitelist_is_checked()
    {
        var findings = Unused().Where(pair => !KnownUnused.ContainsKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var (name, where) in findings)
        {
            _output.WriteLine($"no consumer for: {name} ({where})");
        }

        _output.WriteLine($"reported {findings.Count}, whitelisted {KnownUnused.Count}");

        var stale = KnownUnused.Keys.Where(name => !Unused().ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.True(
            stale.Count == 0,
            "these whitelist entries no longer appear, so their reasons need a second look: "
            + string.Join(", ", stale));
    }

    /// <summary>
    /// 每个窗自己声明、却找不到任何消费者的类名，带回它定义在哪个文件。
    /// 只查窗口/控件本地的样式：库里的类（`Controls.axaml`）是全仓共用的，不在这个范围。
    /// </summary>
    private static Dictionary<string, string> Unused()
    {
        var root = RepositoryRoot();
        var views = Path.Combine(root, "WhaleGenie", "Views");
        var tests = Path.Combine(root, "WhaleGenie.Tests");

        // 全部 .cs（含 Views 与 Runtime）与全部测试，拼成一份可搜的文本
        var code = string.Join("\n", Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));
        var testCode = string.Join("\n", Directory.EnumerateFiles(tests, "*.cs").Select(File.ReadAllText));

        var unused = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(views, "*.axaml"))
        {
            var text = File.ReadAllText(path);
            var name = Path.GetFileName(path);

            foreach (var (type, chains) in Declared(text))
            {
                foreach (var chain in chains)
                {
                    foreach (var token in chain)
                    {
                        // 只有"带修饰的那一层"可能是死的；基类那一层由父选择器共用
                        if (HasConsumer(text, testCode, code, token))
                        {
                            continue;
                        }

                        unused.TryAdd($"{type}.{string.Join('.', chain)}", name);
                    }
                }
            }
        }

        return unused;
    }

    private static bool HasConsumer(string xaml, string tests, string code, string name)
    {
        var escaped = Regex.Escape(name);

        // ① XAML：组合写法里出现这个词，或绑定式加类
        if (Regex.IsMatch(xaml, $@"Classes=""[^""]*\b{escaped}\b[^""]*"""))
        {
            return true;
        }

        if (Regex.IsMatch(xaml, $@"Classes\.{escaped}="))
        {
            return true;
        }

        // ② 代码里加类：Add / 初始化器 / Set
        if (Regex.IsMatch(code, $@"(?:Add|Set)\(\s*""{escaped}""")
            || Regex.IsMatch(code, $@"Classes\s*=\s*\{{[^}}]*""{escaped}"""))
        {
            return true;
        }

        // ③ 测试按类名找元素
        return Regex.IsMatch(tests, $@"Contains\(\s*""{escaped}""");
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
