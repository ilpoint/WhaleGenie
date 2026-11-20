using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Conditions;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.UIA3;


namespace WhaleGenie.Platform.Windows;

// The last steps: matching a query against an element, the condition behind it, and the failures worth retrying.
public sealed partial class FlaUiDevice
{
    /// <summary>
    /// The element the query means: the one sitting at its own place in the list of matches, or
    /// null when there are not that many.
    /// </summary>
    private AutomationElement? Find(UiQuery query)
    {
        var index = Math.Max(1, query.Index);
        var hits = Search(query, index);
        return index <= hits.Count ? hits[index - 1] : null;
    }

    /// <summary>Where an element starts on screen, or the origin when the provider will not say.</summary>
    private static ScreenPoint Corner(AutomationElement element)
    {
        var bounds = Bounds(element);
        return new ScreenPoint(bounds.X, bounds.Y);
    }

    /// <summary>The same rectangle as a value the anchor rule can compare, in the four numbers a macro writes.</summary>
    private static ScreenRect BoxOf(AutomationElement element)
    {
        var bounds = Bounds(element);
        return new ScreenRect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
    }

    private ConditionBase? Condition(UiQuery query)
    {
        var factory = _automation.Value.ConditionFactory;
        ConditionBase? condition = null;

        void Add(ConditionBase part) => condition = condition is null ? part : condition.And(part);

        // 带通配符的那一项不进条件：提供方只认精确值，把 `保存*` 塞进去会一个都找不到。
        // 它留到 Matches 里按字面比，其余几项仍旧先把范围缩小。
        if (!string.IsNullOrWhiteSpace(query.Name) && !TextCompare.HasWildcard(query.Name))
        {
            Add(factory.ByName(query.Name!));
        }

        if (!string.IsNullOrWhiteSpace(query.AutomationId) && !TextCompare.HasWildcard(query.AutomationId))
        {
            Add(factory.ByAutomationId(query.AutomationId!));
        }

        if (!string.IsNullOrWhiteSpace(query.ClassName) && !TextCompare.HasWildcard(query.ClassName))
        {
            Add(factory.ByClassName(query.ClassName!));
        }

        if (!string.IsNullOrWhiteSpace(query.ControlType) && ControlTypeOf(query.ControlType!) is { } type)
        {
            Add(factory.ByControlType(type));
        }

        return condition;
    }

    private static bool Matches(AutomationElement element, UiQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.Name)
            && !UiQuery.Answers(Read(() => element.Name), query.Name!))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.AutomationId)
            && !UiQuery.Answers(Read(() => element.AutomationId), query.AutomationId!))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.ClassName)
            && !UiQuery.Answers(Read(() => element.ClassName), query.ClassName!))
        {
            return false;
        }

        // The kind of control has to be compared as well: a query that names only a kind — "the
        // edit boxes in this window" — would otherwise match the window itself, because the window
        // has no name to disagree with.
        if (!string.IsNullOrWhiteSpace(query.ControlType)
            && ControlTypeOf(query.ControlType!) is { } wanted
            && TypeOf(element) != wanted)
        {
            return false;
        }

        foreach (var condition in query.Conditions)
        {
            if (!Holds(element, condition))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 一条「还得像什么样」。这三件事都是 UI Automation 自己会答的：在不在屏幕上、现在能不能用、
    /// 名字里有没有这段字。名字读不出来时按不成立算，包括「不含」那一条：读不到名字的元素，
    /// 说它「不含这段字」只是拿空白对了一下，不该因此被选中。
    /// </summary>
    private static bool Holds(AutomationElement element, UiCondition condition) => condition.How switch
    {
        UiConditionHow.Visible => Flag(() => !element.IsOffscreen),
        UiConditionHow.Enabled => Flag(() => element.IsEnabled),
        _ => NameOf(element) is { } name
            && (condition.How == UiConditionHow.NameHas
                ? name.Contains(condition.Value, StringComparison.OrdinalIgnoreCase)
                : !name.Contains(condition.Value, StringComparison.OrdinalIgnoreCase)),
    };

    /// <summary>The name an element goes by, or nothing when the provider will not say.</summary>
    private static string? NameOf(AutomationElement element)
    {
        try
        {
            return element.Name;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return null;
        }
    }

    /// <summary>
    /// 每一个后代，一层层走下去。只有查询里写着通配符、条件为空时才用它：提供方只认精确值，
    /// 没法把 `保存*` 交给它去筛，只能自己走一遍。
    /// </summary>
    private static IEnumerable<AutomationElement> Descendants(AutomationElement root)
    {
        AutomationElement[] children;
        try
        {
            children = root.FindAllChildren();
        }
        catch (Exception error) when (Recoverable(error))
        {
            yield break;
        }

        foreach (var child in children)
        {
            yield return child;
            foreach (var deeper in Descendants(child))
            {
                yield return deeper;
            }
        }
    }

    /// <summary>The kind of control an element is, or null when the provider will not say.</summary>
    private static ControlType? TypeOf(AutomationElement element)
    {
        try
        {
            return element.ControlType;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return null;
        }
    }

    private void ClickAt(AutomationElement element, string button, UiQuery query)
    {
        if (!element.TryGetClickablePoint(out var point))
        {
            throw new DeviceActionException(RunKeys.ElementNotClickable, Describe(query));
        }

        _input.Value.Click(button, (int)point.X, (int)point.Y, 1, 0);
    }

    /// <summary>Turns "Button" or "Edit" into the control type it names.</summary>
    private static ControlType? ControlTypeOf(string text)
        => Enum.TryParse<ControlType>(text.Trim(), ignoreCase: true, out var type) ? type : null;

    /// <summary>How an element is described in a failure message.</summary>
    private static string Describe(UiQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.AutomationId))
        {
            return string.IsNullOrWhiteSpace(query.ControlType)
                ? query.AutomationId!
                : $"{query.ControlType}[automationId='{query.AutomationId}']";
        }

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            return string.IsNullOrWhiteSpace(query.ControlType)
                ? query.Name!
                : $"{query.ControlType}[name='{query.Name}']";
        }

        return query.ControlType ?? query.WindowTitle ?? string.Empty;
    }

    /// <summary>
    /// True for the failures that mean "that element is not there", which is an answer rather
    /// than a problem: anything the provider refuses, anything that went away, any timeout.
    /// Cancellation and the device's own refusals are left to travel on.
    /// </summary>
    private static bool Recoverable(Exception error)
        => error is not OperationCanceledException
            and not DeviceActionException
            and not DeviceUnavailableException
            and not OutOfMemoryException;

    private static void Require()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new DeviceUnavailableException("UI Automation");
        }
    }
}
