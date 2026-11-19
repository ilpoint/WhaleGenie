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

// Changing a control: writing into it, choosing an entry, ticking it, opening it and scrolling to it.
public sealed partial class FlaUiDevice
{
    public bool SetText(UiQuery query, string text, bool clearFirst)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException(RunKeys.ElementNotFound, Describe(query));

        try
        {
            var value = element.Patterns.Value;
            if (!value.IsSupported)
            {
                // Nothing to write into, so put the caret there and type instead — but only
                // once the element really holds the keyboard, or the text lands somewhere else.
                element.Focus();
                if (element.Properties.HasKeyboardFocus.ValueOrDefault is not true)
                {
                    return false;
                }

                _input.Value.TypeText(text, 0);
                return true;
            }

            var current = value.Pattern.Value.Value ?? string.Empty;
            value.Pattern.SetValue(clearFirst ? text : current + text);
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return false;
        }
    }

    public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit)
    {
        Require();
        if (query.IsEmpty)
        {
            throw new DeviceActionException(RunKeys.EmptySelector);
        }

        var hits = new List<UiElementInfo>();
        foreach (var element in Search(query, Math.Max(1, limit)))
        {
            if (Info(element) is { } info)
            {
                hits.Add(info);
            }
        }

        return hits;
    }

    public bool Select(UiQuery query, string text, int itemIndex)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException(RunKeys.ElementNotFound, Describe(query));
        var wanted = (text ?? string.Empty).Trim();

        try
        {
            var items = element.ControlType == ControlType.ComboBox
                ? DropDownEntries(query, element.AsComboBox())
                : ItemsOf(element);
            var item = Pick(items, wanted, itemIndex);

            if (item is null)
            {
                // Naming the item itself as the answer is what makes one tab of a tab strip
                // selectable: there is nothing inside it to look through.
                if (itemIndex <= 0 && element.Patterns.SelectionItem.IsSupported)
                {
                    element.Patterns.SelectionItem.Pattern.Select();
                    return true;
                }

                throw new DeviceActionException(RunKeys.ItemNotFound, Which(wanted, itemIndex));
            }

            return Choose(item);
        }
        catch (Exception error) when (Recoverable(error))
        {
            throw new DeviceActionException(RunKeys.ElementNotSelectable, Describe(query));
        }
    }

    /// <summary>
    /// The entries of a drop-down. A closed one hides them, so it is opened first — and looked up
    /// again afterwards, because opening it leaves the element found a moment ago stale in some
    /// toolkits. Only the box's own children are read here: walking everything underneath a
    /// WinForms drop-down comes back as the whole desktop, which is neither quick nor true.
    /// </summary>
    private IReadOnlyList<AutomationElement> DropDownEntries(UiQuery query, ComboBox box)
    {
        try
        {
            if (box.Items.Length == 0 && box.ExpandCollapseState == ExpandCollapseState.Collapsed)
            {
                box.Expand();
            }
        }
        catch (Exception error) when (Recoverable(error))
        {
            // A box that will not open is still worth reading: some of them list their entries
            // whether they are open or not.
        }

        return [.. (Find(query)?.AsComboBox() ?? box).Items];
    }

    /// <summary>How an entry that could not be found is named in a failure.</summary>
    private static string Which(string text, int itemIndex)
        => itemIndex > 0 ? itemIndex.ToString(CultureInfo.InvariantCulture) : text;

    public bool SetChecked(UiQuery query, bool? state)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException(RunKeys.ElementNotFound, Describe(query));

        try
        {
            var toggle = element.Patterns.Toggle;
            if (!toggle.IsSupported)
            {
                // A radio button or a menu entry is picked rather than flipped, and "on" is the
                // only thing it can be asked for.
                if (state is true && element.Patterns.SelectionItem.IsSupported)
                {
                    element.Patterns.SelectionItem.Pattern.Select();
                    return true;
                }

                throw new DeviceActionException(RunKeys.ElementNotCheckable, Describe(query));
            }

            var on = toggle.Pattern.ToggleState.Value == ToggleState.On;
            if (state is null || state.Value != on)
            {
                toggle.Pattern.Toggle();
            }

            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            throw new DeviceActionException(RunKeys.ElementNotCheckable, Describe(query));
        }
    }

    public bool SetExpanded(UiQuery query, string action)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException(RunKeys.ElementNotFound, Describe(query));

        try
        {
            var pattern = element.Patterns.ExpandCollapse;
            if (!pattern.IsSupported)
            {
                throw new DeviceActionException(RunKeys.ElementNotExpandable, Describe(query));
            }

            switch (action.Trim().ToLowerInvariant())
            {
                case "collapse":
                    pattern.Pattern.Collapse();
                    break;
                case "toggle":
                    if (pattern.Pattern.ExpandCollapseState.Value == ExpandCollapseState.Collapsed)
                    {
                        pattern.Pattern.Expand();
                    }
                    else
                    {
                        pattern.Pattern.Collapse();
                    }

                    break;
                default:
                    pattern.Pattern.Expand();
                    break;
            }

            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            throw new DeviceActionException(RunKeys.ElementNotExpandable, Describe(query));
        }
    }

    public bool ScrollIntoView(UiQuery query)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException(RunKeys.ElementNotFound, Describe(query));

        try
        {
            var pattern = element.Patterns.ScrollItem;
            if (!pattern.IsSupported)
            {
                throw new DeviceActionException(RunKeys.ElementNotScrollable, Describe(query));
            }

            pattern.Pattern.ScrollIntoView();
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            throw new DeviceActionException(RunKeys.ElementNotScrollable, Describe(query));
        }
    }

    /// <summary>
    /// The entries a list, a drop-down, a tree, a set of tabs or a menu offers, in the order they
    /// are listed. Anything else answers with nothing, which leaves the element itself as the only
    /// thing that could have been meant.
    /// </summary>
    private static List<AutomationElement> ItemsOf(AutomationElement element)
    {
        var factory = element.ConditionFactory;
        var entries = factory.ByControlType(ControlType.ListItem)
            .Or(factory.ByControlType(ControlType.DataItem))
            .Or(factory.ByControlType(ControlType.TreeItem))
            .Or(factory.ByControlType(ControlType.TabItem))
            .Or(factory.ByControlType(ControlType.MenuItem));

        return [.. element.FindAllDescendants(entries)];
    }

    /// <summary>
    /// The entry a step named: by its number, counted from one, or by the text it shows. A text
    /// match that reads the same wins over one that merely contains it, so a list holding both
    /// "Open" and "Open recent" picks the one that was asked for.
    /// </summary>
    private static AutomationElement? Pick(IReadOnlyList<AutomationElement> items, string text,
        int itemIndex)
    {
        if (itemIndex > 0)
        {
            return itemIndex <= items.Count ? items[itemIndex - 1] : null;
        }

        if (text.Length == 0)
        {
            return null;
        }

        return items.FirstOrDefault(item =>
                   string.Equals(Read(() => item.Name), text, StringComparison.OrdinalIgnoreCase))
               ?? items.FirstOrDefault(item =>
                   Read(() => item.Name).Contains(text, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Takes an entry: by the pattern meant for it when there is one, and by clicking it when
    /// there is not, which is what a plain list row often needs.
    /// </summary>
    private static bool Choose(AutomationElement item)
    {
        if (item.Patterns.SelectionItem.IsSupported)
        {
            item.Patterns.SelectionItem.Pattern.Select();
            return true;
        }

        if (item.Patterns.Invoke.IsSupported)
        {
            item.Patterns.Invoke.Pattern.Invoke();
            return true;
        }

        item.Click(true);
        return true;
    }
}
