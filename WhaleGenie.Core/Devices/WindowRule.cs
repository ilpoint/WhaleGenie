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
    bool Multiline = false)
{
    public static WindowRule Empty { get; } = new();

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

    public bool HasTarget => Reading.Match is WindowMatch.Handle
        ? Handle != 0
        : MatchValue.Trim().Length > 0;

    public JsonObject ToJson() => new()
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

    public static WindowRule FromJson(JsonObject node)
        => new(
            Text(node, FieldNames.Title),
            Text(node, FieldNames.Process),
            Text(node, FieldNames.Class),
            Number(node, FieldNames.Handle),
            Text(node, FieldNames.How, WindowHow.TitleContains),
            Flag(node, FieldNames.CaseSensitive),
            Flag(node, FieldNames.Trim, true),
            Flag(node, FieldNames.Multiline));

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
