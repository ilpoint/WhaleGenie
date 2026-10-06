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

namespace Viktor.Core.Devices.Platform;

/// <summary>
/// Finds and drives windows and controls through Windows UI Automation. Elements are found
/// by what the user sees, so a macro reads the way the screen reads.
/// </summary>
public sealed class FlaUiDevice : IUiDevice, IDisposable
{
    /// <summary>
    /// How far up the tree the picker looks for the window an element sits in. Deep visual trees
    /// are real — a browser or an editor can nest forty panels under one window — so the walk is
    /// bounded generously and simply gives up when a provider never produces a window.
    /// </summary>
    private const int WindowHops = 40;

    private readonly Lazy<UIA3Automation> _automation = new(() => new UIA3Automation());

    /// <summary>
    /// Used for the button UI Automation has no click of its own for, by clicking the
    /// element's own clickable point.
    /// </summary>
    private readonly Lazy<IInputDevice> _input;

    public FlaUiDevice(IInputDevice? input = null)
    {
        _input = new Lazy<IInputDevice>(() => input ?? new SharpHookInputDevice());
    }

    public bool Exists(UiQuery query, int timeoutMs)
    {
        Require();
        if (query.IsEmpty)
        {
            throw new DeviceActionException("Run.EmptySelector");
        }

        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (Find(query) is not null)
            {
                return true;
            }

            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= Math.Max(0, timeoutMs))
            {
                return false;
            }

            Thread.Sleep(100);
        }
    }

    public bool Click(UiQuery query, string button)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException("Run.ElementNotFound", Describe(query));

        try
        {
            switch (button.Trim().ToLowerInvariant())
            {
                case "right":
                    element.RightClick(true);
                    break;
                case "middle":
                    ClickAt(element, "middle", query);
                    break;
                default:
                    // The invoke pattern works even when the window is not in front, so it
                    // is worth trying before falling back to a real mouse click.
                    if (element.Patterns.Invoke.IsSupported)
                    {
                        element.Patterns.Invoke.Pattern.Invoke();
                    }
                    else
                    {
                        element.Click(true);
                    }

                    break;
            }

            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return false;
        }
    }

    public bool FocusWindow(string title)
    {
        Require();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new DeviceActionException("Run.MissingWindow", title);
        }

        var window = Root(new UiQuery(WindowTitle: title));
        if (window is null)
        {
            return false;
        }

        try
        {
            window.SetForeground();
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return false;
        }
    }

    public string? GetText(UiQuery query)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException("Run.ElementNotFound", Describe(query));

        try
        {
            return TextOf(element);
        }
        catch (Exception error) when (Recoverable(error))
        {
            return element.Name;
        }
    }

    /// <summary>What an element reads as: the value it holds when it has one, and its name otherwise.</summary>
    private static string TextOf(AutomationElement element)
    {
        try
        {
            var value = element.Patterns.Value;
            if (value.IsSupported)
            {
                return value.Pattern.Value.Value ?? string.Empty;
            }
        }
        catch (Exception error) when (Recoverable(error))
        {
            // An element that will not give up its value still has a name worth reading.
        }

        return element.Name ?? string.Empty;
    }

    public IReadOnlyList<IReadOnlyList<string>> ReadTable(UiQuery query, int limit)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException("Run.ElementNotFound", Describe(query));
        var rows = Math.Max(1, limit);

        try
        {
            // A grid is the usual way a table says it is one, and it is the only way that knows
            // its own rows and columns rather than leaving them to be guessed at.
            if (element.ControlType == ControlType.DataGrid)
            {
                return [.. Rows(element.AsDataGridView().Rows.Select(RowText), rows)];
            }

            if (element.Patterns.Grid.IsSupported)
            {
                return [.. Rows(element.AsGrid().Rows.Select(RowText), rows)];
            }

            // Nothing that calls itself a table: the rows are then whatever looks like one, and
            // each cell is read off the controls inside that row.
            var condition = element.ConditionFactory.ByControlType(ControlType.DataItem);
            return
            [
                .. Rows(
                    element.FindAllDescendants(condition)
                        .Select(row => (IReadOnlyList<string>)
                            [.. row.FindAllChildren().Select(TextOf)]),
                    rows),
            ];
        }
        catch (Exception error) when (Recoverable(error))
        {
            throw new DeviceActionException("Run.ElementNotATable", Describe(query));
        }
    }

    /// <summary>The text of one row of a grid, one entry per cell.</summary>
    private static IReadOnlyList<string> RowText(DataGridViewRow row)
        => [.. row.Cells.Select(cell => TextOf(cell))];

    /// <summary>The text of one row of a grid, one entry per cell.</summary>
    private static IReadOnlyList<string> RowText(GridRow row)
        => [.. row.Cells.Select(cell => TextOf(cell))];

    /// <summary>
    /// The rows worth reading, at most <paramref name="limit"/> of them. A grid reports rows that
    /// hold nothing at all — the one the column titles live in, and the empty row the bottom of a
    /// WinForms grid keeps for typing a new one in — and neither is a row of data, so they are
    /// left out rather than handed to the macro as a blank line.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<string>> Rows(IEnumerable<IReadOnlyList<string>> rows,
        int limit)
    {
        var kept = new List<IReadOnlyList<string>>();
        foreach (var row in rows)
        {
            if (row.Any(cell => cell.Length > 0))
            {
                kept.Add(row);
            }

            if (kept.Count >= limit)
            {
                break;
            }
        }

        return kept;
    }

    public bool SetText(UiQuery query, string text, bool clearFirst)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException("Run.ElementNotFound", Describe(query));

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
            throw new DeviceActionException("Run.EmptySelector");
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
        var element = Find(query) ?? throw new DeviceActionException("Run.ElementNotFound", Describe(query));
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

                throw new DeviceActionException("Run.ItemNotFound", Which(wanted, itemIndex));
            }

            return Choose(item);
        }
        catch (Exception error) when (Recoverable(error))
        {
            throw new DeviceActionException("Run.ElementNotSelectable", Describe(query));
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
        var element = Find(query) ?? throw new DeviceActionException("Run.ElementNotFound", Describe(query));

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

                throw new DeviceActionException("Run.ElementNotCheckable", Describe(query));
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
            throw new DeviceActionException("Run.ElementNotCheckable", Describe(query));
        }
    }

    public bool SetExpanded(UiQuery query, string action)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException("Run.ElementNotFound", Describe(query));

        try
        {
            var pattern = element.Patterns.ExpandCollapse;
            if (!pattern.IsSupported)
            {
                throw new DeviceActionException("Run.ElementNotExpandable", Describe(query));
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
            throw new DeviceActionException("Run.ElementNotExpandable", Describe(query));
        }
    }

    public bool ScrollIntoView(UiQuery query)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException("Run.ElementNotFound", Describe(query));

        try
        {
            var pattern = element.Patterns.ScrollItem;
            if (!pattern.IsSupported)
            {
                throw new DeviceActionException("Run.ElementNotScrollable", Describe(query));
            }

            pattern.Pattern.ScrollIntoView();
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            throw new DeviceActionException("Run.ElementNotScrollable", Describe(query));
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

            var condition = Condition(query);
            if (condition is null)
            {
                // Only a window was named, so the window itself is the answer.
                return [root];
            }

            var found = new List<AutomationElement>(root.FindAllDescendants(condition));
            if (Matches(root, query))
            {
                found.Add(root);
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

    /// <summary>
    /// What UI Automation says about an element. An element nobody can name is one a macro could
    /// never look up again, so it answers with null the way the picker wants, rather than with a
    /// description nothing can match.
    /// </summary>
    private static UiElementInfo? Info(AutomationElement element)
    {
        var controlType = Read(() => element.ControlType.ToString());
        var name = Read(() => element.Name);
        var automationId = Read(() => element.AutomationId);

        if (controlType.Length == 0 && name.Length == 0 && automationId.Length == 0)
        {
            return null;
        }

        // UI Automation reports the rectangle in screen pixels, which is the unit a selector
        // taken here has to come back in.
        var bounds = Bounds(element);
        return new UiElementInfo(
            name,
            automationId,
            controlType,
            Read(() => element.ClassName),
            new ScreenPoint(bounds.X, bounds.Y),
            new ScreenSize(bounds.Width, bounds.Height),
            WindowTitleOf(element));
    }

    /// <summary>
    /// Where an element sits. A provider that refuses to say is not worth losing the element over,
    /// so an empty rectangle comes back instead.
    /// </summary>
    private static System.Drawing.Rectangle Bounds(AutomationElement element)
    {
        try
        {
            return element.BoundingRectangle;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return System.Drawing.Rectangle.Empty;
        }
    }

    /// <summary>
    /// The title of the window an element sits in, which is the filter a macro writes next to its
    /// selector. Walking up stops at the first window, so a dialog is named rather than the
    /// program behind it.
    /// </summary>
    private static string WindowTitleOf(AutomationElement element)
    {
        for (var hop = 0; hop < WindowHops; hop++)
        {
            if (Read(() => element.ControlType.ToString()) == nameof(ControlType.Window))
            {
                return Read(() => element.Name);
            }

            if (ParentOf(element) is not { } parent)
            {
                break;
            }

            element = parent;
        }

        return string.Empty;
    }

    private static AutomationElement? ParentOf(AutomationElement element)
    {
        try
        {
            return element.Parent;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return null;
        }
    }

    /// <summary>
    /// Reads one property. A provider is allowed to refuse a property it does not support, and
    /// that is worth an empty string rather than losing the whole element.
    /// </summary>
    private static string Read(Func<string?> value)
    {
        try
        {
            return value() ?? string.Empty;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return string.Empty;
        }
    }

    public void Dispose()
    {
        if (_automation.IsValueCreated)
        {
            _automation.Value.Dispose();
        }
    }

    /// <summary>The window to search in: the whole desktop unless the query names one.</summary>
    private AutomationElement? Root(UiQuery query)
    {
        var desktop = _automation.Value.GetDesktop();
        if (string.IsNullOrWhiteSpace(query.WindowTitle))
        {
            return desktop;
        }

        foreach (var window in desktop.FindAllChildren(candidate => candidate.ByControlType(ControlType.Window)))
        {
            if (window.Name?.Contains(query.WindowTitle, StringComparison.OrdinalIgnoreCase) == true)
            {
                return window;
            }
        }

        return null;
    }

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

    private ConditionBase? Condition(UiQuery query)
    {
        var factory = _automation.Value.ConditionFactory;
        ConditionBase? condition = null;

        void Add(ConditionBase part) => condition = condition is null ? part : condition.And(part);

        if (!string.IsNullOrWhiteSpace(query.Name))
        {
            Add(factory.ByName(query.Name!));
        }

        if (!string.IsNullOrWhiteSpace(query.AutomationId))
        {
            Add(factory.ByAutomationId(query.AutomationId!));
        }

        if (!string.IsNullOrWhiteSpace(query.ClassName))
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
            && !string.Equals(Read(() => element.Name), query.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.AutomationId)
            && !string.Equals(Read(() => element.AutomationId), query.AutomationId,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(query.ClassName)
            && !string.Equals(Read(() => element.ClassName), query.ClassName,
                StringComparison.OrdinalIgnoreCase))
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

        return true;
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
            throw new DeviceActionException("Run.ElementNotClickable", Describe(query));
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
