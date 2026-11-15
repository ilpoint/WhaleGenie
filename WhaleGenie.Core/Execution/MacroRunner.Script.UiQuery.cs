using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// Turning a step into a query for a window element, and the conditions that narrow it.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Reads a selector such as <c>Button[name='Save']</c> or <c>Edit[automationId='input']</c>,
    /// together with the window it is looked for in. That window is recognised by the same rule
    /// every other step recognises one with, and the search is narrowed by it rather than by a title
    /// alone: a step that aims at a window by its program or by a pattern finds the same window a
    /// window action would.
    /// </summary>
    private UiQuery Query(ExecutableStep step)
    {
        var rule = WindowRuleOf(step);
        var query = UiQuery.Parse(Args(step).Text("uiSelector"), rule.MatchValue,
            Index(step, "matchIndex", 1), rule.Reading.Match, rule.Reading.Compare, rule.Options);
        return query with { Narrow = NarrowConditions(step), Anchor = AnchorOf(step, rule) };
    }

    /// <summary>
    /// 两个同名按钮里要右边那一个时写的那一行：挨着谁、在哪一边、至少隔开多少。锚点选择器与
    /// 主选择器走同一个解析器、同一条窗口规则，因为它说的也是这个窗口里的一个元素。
    /// </summary>
    /// <remarks>
    /// 那一格空着就不设锚点，不是「间隔为零的锚点」：这个动作本来就不问锚点。哪一边写了目录
    /// 之外的词由目录的选项表拦下（Run.BadOption），不在这里另写一份判断；间隔的数值范围同理
    /// 由目录的 Minimum/Maximum 管。
    /// </remarks>
    private UiAnchor? AnchorOf(ExecutableStep step, WindowRule rule)
    {
        var selector = Args(step).Text("anchorElement").Trim();
        if (selector.Length == 0)
        {
            return null;
        }

        var side = Args(step).Text("anchorSide").Trim().ToLowerInvariant() switch
        {
            "right" => UiSide.Right,
            "above" => UiSide.Above,
            "below" => UiSide.Below,
            _ => UiSide.Left,
        };

        var anchor = UiQuery.Parse(selector, rule.MatchValue, 1,
            rule.Reading.Match, rule.Reading.Compare, rule.Options);

        return new UiAnchor(anchor, side, Math.Max(0, (int)Args(step).Number("anchorGap")));
    }

    /// <summary>
    /// 元素除了选择器说的那几项，还得像什么样：一行一条，全部同时成立。两件事分得很清楚——
    /// 选择器说它叫什么，这里说它在不在屏幕上、能不能用、名字里有没有一段字。
    /// </summary>
    /// <remarks>
    /// 没填「哪一种」的行当它不存在：那是一个还没写完的表单，不是一条谁都没要的条件。写了
    /// 一个引擎不认识的方式则报错，不放过：少了一个条件，这一步会落到另一个元素上，而且不说。
    /// 要一段字的两条，字没写也当不存在；「看得见」「能用」本来就不需要字，空着才是它们的样子。
    /// </remarks>
    private IReadOnlyList<UiCondition> NarrowConditions(ExecutableStep step)
    {
        var conditions = new List<UiCondition>();
        foreach (var row in step.Rows("narrow"))
        {
            var how = Word(row, FieldNames.How);
            if (how.Length == 0)
            {
                continue;
            }

            if (!Enum.TryParse<UiConditionHow>(how, ignoreCase: true, out var kind))
            {
                throw new StepFailure(RunKeys.ElementBadCondition, how);
            }

            var value = kind is UiConditionHow.NameHas or UiConditionHow.NameHasNot
                ? Text(row, FieldNames.Value).Trim()
                : string.Empty;

            if (kind is (UiConditionHow.NameHas or UiConditionHow.NameHasNot) && value.Length == 0)
            {
                continue;
            }

            conditions.Add(new UiCondition(kind, value));
        }

        return conditions;
    }

    /// <summary>
    /// A yes/no setting. A step that says nothing takes the catalogue's own default, so a
    /// missing switch behaves the way the action editor showed it rather than always being on.
    /// </summary>
    private static bool Flag(ExecutableStep step, string name, bool fallback)
    {
        var text = step.Text(name).Trim();
        return text.Length == 0
            ? fallback
            : !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static LogLevel Level(string level) => level.ToLowerInvariant() switch
    {
        "warn" => LogLevel.Warn,
        "error" => LogLevel.Error,
        "debug" => LogLevel.Debug,
        _ => LogLevel.Info,
    };
}
