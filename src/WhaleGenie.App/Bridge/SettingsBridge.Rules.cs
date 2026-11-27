using System;
using System.Text.Json;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;

namespace WhaleGenie.Bridge;

// The rule shape both editors send back, read the same way whether it came from a menu rule or a
// wheel scene.
internal sealed partial class SettingsBridge
{
    private static WindowRuleState RuleDto(WindowRule rule) => new(
        rule.Title,
        rule.Process,
        rule.ClassName,
        rule.Handle,
        ProjectTriggers.CanonicalHow(rule.How),
        rule.CaseSensitive,
        rule.Trim,
        rule.Multiline);

    private static WindowRule ReadRule(JsonElement parameters)
    {
        var node = parameters.ValueKind == JsonValueKind.Object
                   && parameters.TryGetProperty("rule", out var wrapped)
            ? wrapped
            : parameters;
        if (node.ValueKind != JsonValueKind.Object)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Settings.WindowRuleRequired"));
        }

        var written = Text(node, "how")?.Trim();
        var how = written is { Length: > 0 } ? ReadHow(written) : WindowHow.TitleContains;
        var rule = new WindowRule(
            Text(node, "title") ?? string.Empty,
            Text(node, "process") ?? string.Empty,
            Text(node, "className") ?? string.Empty,
            Number(node, "handle"),
            how,
            Flag(node, "caseSensitive"),
            Flag(node, "trim", true),
            Flag(node, "multiline"));
        if (!rule.HasTarget)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Settings.WindowRuleRequired"));
        }

        return rule;
    }

    /// <summary>
    /// The comparison a page sent back, as one of the ten. The spelling is matched without case so a
    /// rule opened from an older file can be saved again; the value written to the project is the
    /// canonical one the settings form and the group key agree on.
    /// </summary>
    private static string ReadHow(string written)
    {
        foreach (var value in WindowHow.Values)
        {
            if (string.Equals(value, written, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        throw new BridgeFailureException(BridgeErrors.BadArgument,
            Strings.Get("Settings.WindowRuleRequired"));
    }

    private static long Number(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var value))
        {
            return 0;
        }

        if (value.TryGetInt64(out var number))
        {
            return number;
        }

        return long.TryParse(value.ValueKind == JsonValueKind.String ? value.GetString() : null,
            out number)
            ? number
            : 0;
    }
}
