using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The pretend UI Automation tree: what a search turns up, what reading an element says, and every
/// query the engine asked.
/// </summary>
internal sealed class FakeUi(FakeDesk desk) : IUiDevice
{
    /// <summary>Whether UI Automation should say the element is there.</summary>
    public bool ElementExists { get; set; } = true;

    /// <summary>
    /// The last search the element device was handed, so a check can see the window rule the engine
    /// read out of the step: which part of a window it is held against and how they are compared is
    /// the device's business, and what the engine owes it is the rule as the fields stand.
    /// </summary>
    public UiQuery? LastQuery { get; private set; }

    /// <summary>What a UI Automation search turns up, in the order it reports them.</summary>
    public List<UiElementInfo> Elements { get; } = [];

    /// <summary>What reading an element gives back.</summary>
    public string? ElementText { get; set; }

    /// <summary>Whether the element can be worked without the mouse.</summary>
    public bool ElementInvocable { get; set; } = true;

    /// <summary>Whether the element says it can be used.</summary>
    public bool ElementEnabled { get; set; } = true;

    /// <summary>Whether the element says it is on screen.</summary>
    public bool ElementVisible { get; set; } = true;

    /// <summary>Whether writing into an element is allowed.</summary>
    public bool ElementWritable { get; set; } = true;

    /// <summary>Whether the window can be brought to the front.</summary>
    public bool WindowFocused { get; set; } = true;

    /// <summary>The control the element picker would find under a place, which is what the picker
    /// asks for; a check that is about the picker sets it.</summary>
    public UiElementInfo? ElementUnderPointer { get; set; }

    /// <summary>What picking an entry of a drop-down or a list answers.</summary>
    public bool ItemSelectable { get; set; } = true;

    /// <summary>What turning a check box on, off or over answers.</summary>
    public bool Checkable { get; set; } = true;

    /// <summary>What opening or closing a node answers.</summary>
    public bool Expandable { get; set; } = true;

    /// <summary>What scrolling an element into view answers.</summary>
    public bool Scrollable { get; set; } = true;

    /// <summary>What reading a table answers: one array of cells per row.</summary>
    public List<string[]> Table { get; } = [];

    /// <summary>What reading a table answers about the names of its columns.</summary>
    public List<string> Columns { get; } = [];

    public bool Exists(UiQuery query, int timeoutMs)
    {
        LastQuery = query;
        desk.Note($"ui {query.ControlType}/{query.Name}/{query.AutomationId}");
        return ElementExists;
    }

    public UiElementInfo? ElementAt(int x, int y)
    {
        desk.Note($"elementAt {x},{y}");
        return ElementUnderPointer;
    }

    public bool Click(UiQuery query, string button)
    {
        desk.Note($"clickElement {query.Name} #{query.Index} {button}");
        return ElementExists;
    }

    public bool FocusWindow(UiQuery query)
    {
        LastQuery = query;
        desk.Note($"focus {query.WindowTitle}");
        return WindowFocused;
    }

    public bool Invoke(UiQuery query)
    {
        desk.Note($"invoke {query.Name} #{query.Index}");
        return ElementInvocable;
    }

    public UiElementFacts? Facts(UiQuery query)
    {
        desk.Note($"read {query.Name}");
        if (!ElementExists)
        {
            return null;
        }

        var place = Elements.Count > 0 ? Elements[0] : null;
        return new UiElementFacts(ElementEnabled, ElementVisible, ElementText ?? string.Empty,
            place?.Name ?? string.Empty, place?.AutomationId ?? string.Empty,
            place?.ControlType ?? string.Empty, place?.Location ?? default,
            place?.Size ?? default);
    }

    public bool SetText(UiQuery query, string text, bool clearFirst)
    {
        desk.Note($"fill {query.ControlType}/{query.AutomationId} {text} {clearFirst}");
        return ElementWritable;
    }

    public IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit)
    {
        desk.Note($"findElements {query.ControlType}/{query.Name}/{query.AutomationId} #{query.Index} take {limit}");
        return [.. Elements.Take(Math.Max(1, limit))];
    }

    public bool Select(UiQuery query, string text, int itemIndex)
    {
        desk.Note($"selectItem {query.ControlType}/{query.Name}/{query.AutomationId}#{query.Index} \"{text}\" {itemIndex}");
        return ItemSelectable;
    }

    public bool SetChecked(UiQuery query, bool? state)
    {
        desk.Note($"setChecked {query.Name} {state?.ToString() ?? "flip"}");
        return Checkable;
    }

    public bool SetExpanded(UiQuery query, string action)
    {
        desk.Note($"setExpanded {query.Name} {action}");
        return Expandable;
    }

    public bool ScrollIntoView(UiQuery query)
    {
        desk.Note($"scrollIntoView {query.Name}");
        return Scrollable;
    }

    public UiTable ReadTable(UiQuery query, int limit)
    {
        desk.Note($"readTable {query.Name}#{query.Index} take {limit}");
        return new UiTable([.. Columns],
            [.. Table.Take(Math.Max(1, limit)).Select(row => (IReadOnlyList<string>)row)]);
    }
}
