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


namespace WhaleGenie.Core.Devices.Platform;

// Finding elements: the search itself, the anchor a query counts from, and what sits at a point.
public sealed partial class FlaUiDevice
{
    /// <summary>
    /// The elements the query describes, in the order a person counts them on screen: across the
    /// row first, then down. The list is cut to <paramref name="limit"/>, and a query that turns up
    /// nothing but a window it names answers with that window.
    /// </summary>
    private List<AutomationElement> Search(UiQuery query, int limit)
    {
        try
        {
            var root = Root(query);
            if (root is null)
            {
                return [];
            }

            // 锚点只查一次，候选再多也共用一个位置：每挑一个候选就去问一次锚点，代价是每个候选
            // 一次跨进程调用。锚点不在、或提供方说不出它在哪儿，这次查找就当什么也没找到，
            // 而不是退回「不管锚点、随便挑一个」：那正是两个同名按钮里选错的那个。
            var anchorBox = query.Anchor is { } asked ? AnchorBox(asked) : null;
            if (query.Anchor is not null && anchorBox is null)
            {
                return [];
            }

            var condition = Condition(query);

            // 通配符与「还得像什么样」都交给提供方说不出来：前者它只认精确值，后者它没有这种
            // 条件。这两样在的时候，把选择器缩出来的候选按 Matches 逐项比一遍。
            var byHand = query.HasWildcard || query.Conditions.Count > 0;
            if (condition is null && !byHand)
            {
                // Only a window was named, so the window itself is the answer.
                return query.Anchor is { } only && !only.Holds(BoxOf(root), anchorBox!.Value)
                    ? []
                    : [root];
            }

            var candidates = condition is null
                ? Descendants(root)
                : root.FindAllDescendants(condition);
            var found = byHand
                ? [.. candidates.Where(element => Matches(element, query))]
                : new List<AutomationElement>(candidates);
            if (Matches(root, query))
            {
                found.Add(root);
            }

            // 方位过滤必须排在排序与 Take 之前：「第几个命中」数的是过了锚点之后的那一串。
            // 先截断再过滤，被锚点排除掉的那些也占了号，宏拿到的是另一个元素，而且不会有人
            // 告诉它这件事。
            if (query.Anchor is { } anchor)
            {
                found = [.. found.Where(element => anchor.Holds(BoxOf(element), anchorBox!.Value))];
            }

            // Reading the rectangle of every hit costs a call into the provider each, so a query
            // that turned up one thing — by far the common case — is answered without ordering.
            if (found.Count <= 1)
            {
                return found;
            }

            return
            [
                .. found
                    .Select(element => (Element: element, Corner: Corner(element)))
                    .OrderBy(hit => hit.Corner.Y)
                    .ThenBy(hit => hit.Corner.X)
                    .Take(limit)
                    .Select(hit => hit.Element),
            ];
        }
        catch (Exception error) when (Recoverable(error))
        {
            return [];
        }
    }

    /// <summary>
    /// Where the anchor element sits, or null when it is not there or the provider will not say.
    /// The anchor's own query is cleared of its anchor first: an anchor that went looking for an
    /// anchor of its own would never come back.
    /// </summary>
    private ScreenRect? AnchorBox(UiAnchor anchor)
    {
        if (Find(anchor.Query with { Anchor = null }) is not { } element)
        {
            return null;
        }

        var box = BoxOf(element);
        return box.IsEmpty ? null : box;
    }

    /// <summary>
    /// What UI Automation sees under a screen point, for the picker that writes selectors out of
    /// what is on screen. Null when there is nothing there, or when nothing useful can be read
    /// about it.
    /// </summary>
    public UiElementInfo? ElementAt(int x, int y)
    {
        Require();

        try
        {
            var element = _automation.Value.FromPoint(new System.Drawing.Point(x, y));
            return element is null ? null : Info(element);
        }
        catch (Exception error) when (Recoverable(error))
        {
            return null;
        }
    }
}
