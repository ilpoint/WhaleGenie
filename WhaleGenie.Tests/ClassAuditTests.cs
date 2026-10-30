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

    // ── 第三条：选择器到底配不配得到元素 ──────────────────────────────────
    //
    // 前两条问的都是"这个词有没有人用"。样式真正要问的是**这条选择器会不会命中**——
    // 这一次就栽在这上面：`Button.Tab.on` 里的两个词各自都有消费者（`on` 在绑定里、`Tab` 在
    // `SettingsWindow`），**但从来没有同时出现在同一个元素上**，于是两个页签的选中态一直是空的。
    //
    // 前三条（用了没定义 / 定义了没人用 / 样式在 A 窗消费者在 B 窗）的共同弱点是**按词看**；
    // 这一条**按组合看**：选择器声明的类名集合，必须整套出现在某个同类型元素的类名集合里。
    //
    // 同样只报告不失败。白名单要写原因，并且**在 /template/ 里用的选择器不在此列**——
    // 模板内部的元素不写在 XAML 的 `Classes=` 上，静态比对必然报。

    /// <summary>已知"配不上但没问题"的选择器，每条写原因。条目同样会被检查是否过期。</summary>
    private static readonly Dictionary<string, string> KnownUnmatched =
        new(StringComparer.Ordinal);

    [Fact]
    public void Selectors_that_cannot_match_an_element_are_reported_with_what_differs()
    {
        var findings = Unmatched().Where(pair => !KnownUnmatched.ContainsKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var (selector, detail) in findings)
        {
            _output.WriteLine($"cannot match: {selector} — {detail}");
        }

        _output.WriteLine($"reported {findings.Count}, whitelisted {KnownUnmatched.Count}");

        var stale = KnownUnmatched.Keys.Where(selector => !Unmatched().ContainsKey(selector))
            .OrderBy(selector => selector, StringComparer.Ordinal)
            .ToList();
        Assert.True(
            stale.Count == 0,
            "these whitelist entries no longer appear, so their reasons need a second look: "
            + string.Join(", ", stale));
    }

    private static Dictionary<string, string> Unmatched()
    {
        var root = RepositoryRoot();
        var findings = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "WhaleGenie", "Views"), "*.axaml"))
        {
            var text = File.ReadAllText(path);
            var codeFile = path + ".cs";   // axaml 的代码后置是 `X.axaml.cs`，不是 `X.cs`
            var code = File.Exists(codeFile) ? File.ReadAllText(codeFile) : string.Empty;
            var fromCode = ClassesAddedInCode(code);

            // 每个元素（含选择器里的类型）身上有哪些类：`Classes="…"` 与 `Classes.名字=` 都算
            var onElements = Elements(text, fromCode);
            if (onElements.Count == 0)
            {
                continue;
            }

            foreach (Match match in Regex.Matches(text, @"Selector=""([^""]+)"""))
            {
                var raw = match.Groups[1].Value;

                // 模板内部的元素不写在 `Classes=` 上，比对必然失手——跳过，并在注释里说明
                if (raw.Contains("/template/", StringComparison.Ordinal))
                {
                    continue;
                }

                var head = raw.Split('>')[0].Split(' ')[0].Split(',')[0].Split(':')[0].Trim();
                var parts = head.Split('.');
                if (parts.Length < 3)
                {
                    // 只带一个类的选择器由前两条管；这一条只管"组合"
                    continue;
                }

                var type = parts[0];
                var wanted = parts.Skip(1).ToArray();
                if (!onElements.TryGetValue(type, out var sets))
                {
                    findings.TryAdd(raw, $"{type} 在这个窗口里一个都没有");
                    continue;
                }

                if (sets.Any(set => wanted.All(step => set.Contains(step, StringComparer.Ordinal))))
                {
                    continue;
                }

                // 差在哪：把这个类型上出现过的类名集合里，覆盖最多那一组摊开给人看
                var nearest = sets
                    .OrderByDescending(set => set.Count(step => wanted.Contains(step, StringComparer.Ordinal)))
                    .First();
                var missing = wanted.Where(step => !nearest.Contains(step, StringComparer.Ordinal)).ToList();
                findings.TryAdd(
                    raw,
                    missing.Count == 0
                        ? "没有任何元素同时带着这些类"
                        : $"最接近的元素是 [{string.Join(' ', nearest)}]，缺 [{string.Join(' ', missing)}]");
            }
        }

        return findings;
    }

    /// <summary>按控件类型收集元素身上的类名集合（含代码里加的类）。</summary>
    private static Dictionary<string, List<HashSet<string>>> Elements(string xaml, IEnumerable<string> fromCode)
    {
        var result = new Dictionary<string, List<HashSet<string>>>(StringComparer.Ordinal);
        foreach (Match tag in Regex.Matches(xaml, @"<([A-Za-z][\w:.]*)((?:[^>""']|""[^""]*""|'[^']*')*)>"))
        {
            var type = tag.Groups[1].Value;
            var attrs = tag.Groups[2].Value;
            var set = new HashSet<string>(StringComparer.Ordinal);

            var classes = Regex.Match(attrs, @"Classes=""([^""]+)""");
            if (classes.Success)
            {
                foreach (var name in classes.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    set.Add(name);
                }
            }

            // `Classes.on="{Binding …}"`：绑定的那一档也算这个元素可能带的类
            foreach (Match bound in Regex.Matches(attrs, @"Classes\.([A-Za-z][\w]*)\s*="))
            {
                set.Add(bound.Groups[1].Value);
            }

            // 代码里给这个窗加过的类，静态分不清加到哪个元素上，就当作每个元素都可能带
            set.UnionWith(fromCode);

            if (!result.TryGetValue(type, out var list))
            {
                result[type] = list = [];
            }

            list.Add(set);
        }

        return result;
    }

    /// <summary>代码里给控件加过的类名：`Classes.Add("X")`、`Classes = { "X" }`、`Classes.Set("X", …)`。</summary>
    private static HashSet<string> ClassesAddedInCode(string code)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        // 必须限定在 `Classes` 上：`Dictionary.Add("Ctrl", …)` / `List.Add("Ctrl")` 也是 `Add("…")`，
        // 但那是数据不是类名——第一版没限定，`HotkeyNameMapper` 的按键名全被当成类名报了出来。
        foreach (Match match in Regex.Matches(code, @"Classes\.(?:Add|Set)\(\s*""([^""]+)"""))
        {
            names.Add(match.Groups[1].Value);
        }

        foreach (Match match in Regex.Matches(code, @"Classes\s*=\s*\{([^}]*)\}"))
        {
            foreach (Match name in Regex.Matches(match.Groups[1].Value, @"""([^""]+)"""))
            {
                names.Add(name.Groups[1].Value);
            }
        }

        return names;
    }

    // ── 第四条：`.cs` 里 `Classes.Add("X")` 的 X 有没有样式 ───────────────
    //
    // 前三条只扫 `Views/*.axaml`。**13 个纯代码窗口一处不在覆盖面里**——它们没有 axaml，
    // 界面全在 C# 里 `new` 出来，类名也在 C# 里加。`MenuCell` 那次就是在这个盲区里被偶然发现的。
    //
    // 对这些窗口检查很便宜：**它们没有本地样式，"用了没定义"就等于"`Classes.Add("X")` 之后
    // 全仓找不到任何声明了 X 的选择器"**。
    //
    // 只报告不失败（与前三条同规矩）；白名单写原因，条目同样会被检查是否过期。

    /// <summary>代码里加的、明知没有本地样式的类，每条写原因。</summary>
    private static readonly Dictionary<string, string> KnownCodeOnly =
        new(StringComparer.Ordinal);

    [Fact]
    public void Classes_added_in_code_have_a_style_somewhere()
    {
        var findings = CodeOnlyUnstyled().Where(pair => !KnownCodeOnly.ContainsKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        foreach (var (name, where) in findings)
        {
            _output.WriteLine($"no style for: {name} ({where})");
        }

        _output.WriteLine($"reported {findings.Count}, whitelisted {KnownCodeOnly.Count}");

        var stale = KnownCodeOnly.Keys.Where(name => !CodeOnlyUnstyled().ContainsKey(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.True(
            stale.Count == 0,
            "these whitelist entries no longer appear, so their reasons need a second look: "
            + string.Join(", ", stale));
    }

    /// <summary>代码里加的类名，逐个数有没有任何选择器声明了它。</summary>
    private static Dictionary<string, string> CodeOnlyUnstyled()
    {
        var root = RepositoryRoot();
        var declared = new HashSet<string>(StringComparer.Ordinal);

        // 全局库 + 每个窗口自己的样式，都算"声明过"
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "WhaleGenie"), "*.axaml",
                     SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(path);
            foreach (var (_, chains) in Declared(text))
            {
                foreach (var chain in chains)
                {
                    foreach (var token in chain)
                    {
                        declared.Add(token);
                    }
                }
            }
        }

        // **测试也是消费者。** 六个菜单测试按 `Classes.Contains("MenuCell")` 找格子，
        // 按 `MenuIconChoice` 找图标钮——它们没有样式，但删了会让测试变成"找不到元素"。
        // "没有样式"不等于"没人用"：这条检查问的是样式，答案必须把"用途"也算进来。
        var tests = string.Join("\n", Directory.EnumerateFiles(
            Path.Combine(root, "WhaleGenie.Tests"), "*.cs").Select(File.ReadAllText));

        var findings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "WhaleGenie", "Views"), "*.cs"))
        {
            var code = File.ReadAllText(path);
            foreach (var name in ClassesAddedInCode(code))
            {
                if (declared.Contains(name)
                    || Regex.IsMatch(tests, $@"Contains\(\s*""{Regex.Escape(name)}"""))
                {
                    continue;
                }

                // 报错信息里给候选，等于把排查的一步省掉：`On` 与 `.on` 差大小写，
                // 人读代码时不会逐字比大小写，但扫描可以。
                var near = declared.FirstOrDefault(candidate =>
                    string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase));
                findings.TryAdd(
                    name,
                    near is null
                        ? Path.GetFileName(path)
                        : $"{Path.GetFileName(path)} — 最接近的是 .{near}（大小写不同）");
            }
        }

        return findings;
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
