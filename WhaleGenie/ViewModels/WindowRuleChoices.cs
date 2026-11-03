using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// 本宏里已经用过的一条窗口规则：怎么读它、原文，以及用在几处。窗口那一格原来只认得这一格自己
/// 写过什么，同样的窗口要在二十个步骤里各绑一次；有了这张表，从已经绑过的那几条里挑一条就行。
/// </summary>
public sealed record WindowRuleChoice(string Summary, string Rule, int Count);

/// <summary>
/// 把宏里散落在各步的窗口规则收成一张表，并回答"落点之上最近那一条是什么"。两件事都是读步骤，
/// 不碰界面，所以挑选窗口与给新步骤定默认值用的是同一套判断。
/// </summary>
public static class WindowRuleChoices
{
    /// <summary>
    /// 每条规则只在第一次出现的位置列一次，后面的次数累加上去。空着的一格（前台窗口）与"沿用
    /// 上一次"都不进这张表：它们不是一条可以复用的窗口。
    /// </summary>
    public static IReadOnlyList<WindowRuleChoice> Of(IEnumerable<MacroStep> steps)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var order = new List<(string Summary, string Rule)>();

        foreach (var written in Written(steps))
        {
            if (!WindowRule.TryParse(written, out var rule) || !rule.HasTarget)
            {
                continue;
            }

            var json = rule.ToJson().ToJsonString();
            if (seen.TryGetValue(json, out var count))
            {
                seen[json] = count + 1;
                continue;
            }

            seen[json] = 1;
            order.Add((Summary(rule), json));
        }

        return [.. order.Select(entry => new WindowRuleChoice(entry.Summary, entry.Rule,
            seen[entry.Rule]))];
    }

    /// <summary>
    /// 落点之上最近一条窗口规则。新步骤放在 <paramref name="beforeId"/> 指的那一步后面（没有选中
    /// 就是末尾），所以它上面最近用过的窗口就是这一步多半要用的那个；一条都没有时是空串。
    /// </summary>
    public static string NearestAbove(IEnumerable<MacroStep> steps, string? beforeId)
    {
        var found = string.Empty;
        var looking = !string.IsNullOrEmpty(beforeId);

        foreach (var step in Walk(steps))
        {
            if (looking && string.Equals(step.Id, beforeId, StringComparison.Ordinal))
            {
                break;
            }

            if (WindowText(step) is { } written)
            {
                found = written;
            }
        }

        return found;
    }

    /// <summary>窗口格写成"记事本 · 标题包含"这样，与字段里那行摘要同一种读法。</summary>
    private static string Summary(WindowRule rule)
        => $"{rule.MatchValue.Trim()} · {Strings.Get($"WindowRule.Short.{rule.How}", rule.How)}";

    private static IEnumerable<string> Written(IEnumerable<MacroStep> steps)
    {
        foreach (var step in Walk(steps))
        {
            if (WindowText(step) is { } written)
            {
                yield return written;
            }
        }
    }

    /// <summary>
    /// A step's window field, when it wrote one that is neither empty nor a variable nor another
    /// "the same as before": a chain of those is answered by the last real rule above it.
    /// </summary>
    private static string? WindowText(MacroStep step)
    {
        var written = step.Parameters
            .FirstOrDefault(parameter => string.Equals(parameter.Name, "window", StringComparison.Ordinal))
            ?.Value.Trim();

        if (string.IsNullOrEmpty(written) || written.StartsWith('$'))
        {
            return null;
        }

        return WindowRule.TryParse(written, out var rule) && rule.Follow ? null : written;
    }

    /// <summary>Every step in the order it is read, the ones inside a block included, in place.</summary>
    private static IEnumerable<MacroStep> Walk(IEnumerable<MacroStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;

            foreach (var nested in step.StepLists.Select(list => list.Steps).SelectMany(Walk))
            {
                yield return nested;
            }
        }
    }
}
