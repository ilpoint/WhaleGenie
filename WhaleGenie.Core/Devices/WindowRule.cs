using System;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Execution;

namespace WhaleGenie.Core.Devices;

/// <summary>
/// A window a macro may act on: what it looked like when it was taken, and the one part of it that
/// is compared on every run. A handle rule identifies only that live window instance; the other
/// rules find a window again by title, program or class and remain usable after a restart.
/// </summary>
public sealed record WindowRule(
    string Title = "",
    string Process = "",
    string ClassName = "",
    long Handle = 0,
    string How = WindowHow.TitleContains,
    bool CaseSensitive = false,
    bool Trim = true,
    bool Multiline = false,
    bool Follow = false)
{
    public static WindowRule Empty { get; } = new();

    /// <summary>
    /// 一条只说"跟前面一样"的规则：它自己不认识任何窗口，用的是这一轮里上一个自己指定了窗口的
    /// 步骤那句话。宏里每做一件事就把窗口重绑一遍，本来就不该是必须的。
    /// </summary>
    public static WindowRule FollowPrevious { get; } = new(Follow: true);

    public static WindowRule From(WindowInfo window, string process, string className)
        => new(window.Title, process, className, window.Handle);

    public string MatchValue => WindowHow.Read(How).Match switch
    {
        WindowMatch.Process => Process,
        WindowMatch.ClassName => ClassName,
        WindowMatch.Handle => Handle.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => Title,
    };

    public (WindowMatch Match, string Compare) Reading => WindowHow.Read(How);

    public TextCompareOptions Options => new(CaseSensitive, Trim, Multiline);

    public bool HasTarget => !Follow && (Reading.Match is WindowMatch.Handle
        ? Handle != 0
        : MatchValue.Trim().Length > 0);

    public JsonObject ToJson()
    {
        var node = new JsonObject
        {
            [FieldNames.Handle] = Handle,
            [FieldNames.Title] = Title,
            [FieldNames.Process] = Process,
            [FieldNames.Class] = ClassName,
            [FieldNames.How] = How,
            [FieldNames.CaseSensitive] = CaseSensitive,
            [FieldNames.Trim] = Trim,
            [FieldNames.Multiline] = Multiline,
        };

        // 只有真的要沿用才写这一格：别的规则写成原来的样子，读起来、比起来都不变。
        if (Follow)
        {
            node[FieldNames.Follow] = true;
        }

        return node;
    }

    public static WindowRule FromJson(JsonObject node)
        => new(
            Text(node, FieldNames.Title),
            Text(node, FieldNames.Process),
            Text(node, FieldNames.Class),
            Number(node, FieldNames.Handle),
            Text(node, FieldNames.How, WindowHow.TitleContains),
            Flag(node, FieldNames.CaseSensitive),
            Flag(node, FieldNames.Trim, true),
            Flag(node, FieldNames.Multiline),
            Flag(node, FieldNames.Follow));

    public static bool TryParse(string? text, out WindowRule rule)
    {
        rule = Empty;
        var written = (text ?? string.Empty).Trim();
        if (written.Length == 0 || written[0] == '$')
        {
            return false;
        }

        try
        {
            if (JsonNode.Parse(written) is not JsonObject node)
            {
                return false;
            }

            rule = FromJson(node);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string Text(JsonObject node, string name, string fallback = "")
        => node[name]?.GetValue<string>() ?? fallback;

    private static long Number(JsonObject node, string name)
        => node[name]?.GetValue<long>() ?? 0;

    private static bool Flag(JsonObject node, string name, bool fallback = false)
        => node[name]?.GetValue<bool>() ?? fallback;
}
