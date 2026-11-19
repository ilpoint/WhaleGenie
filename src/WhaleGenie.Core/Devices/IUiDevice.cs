using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using WhaleGenie.Core.Recording;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Devices;

// A window’s own controls, read through the system’s accessibility interface.
/// <summary>Finding and driving windows through UI Automation.</summary>
public interface IUiDevice
{
    bool Exists(UiQuery query, int timeoutMs);

    /// <summary>
    /// The element at one place on the screen, or null when nothing Automation can name is there.
    /// This is the question the element picker asks while the pointer moves: it is about a place
    /// rather than about a rule, which is the other way round from every other call here.
    /// </summary>
    UiElementInfo? ElementAt(int x, int y);

    /// <summary>Clicks an element with one of the mouse buttons.</summary>
    bool Click(UiQuery query, string button);

    /// <summary>
    /// Works the element's default action without a mouse: the button is pressed the way the
    /// element itself knows how, so the pointer stays where it was and the window it sits in is
    /// not brought forward. False when the element offers no way to be worked that way — that is
    /// an answer about the element rather than a failure of its own.
    /// </summary>
    bool Invoke(UiQuery query);

    /// <summary>
    /// Everything one element says about itself right now, or null when there is no such element.
    /// A step that has to decide what to do with a control — is it usable, is it showing, what is
    /// in it — asks here rather than guessing from what it looked like a moment ago.
    /// </summary>
    UiElementFacts? Facts(UiQuery query);

    /// <summary>
    /// Brings the window a query names to the front. The window is recognised by the same rule every
    /// other step recognises one with, so the query carries both halves of it.
    /// </summary>
    bool FocusWindow(UiQuery query);

    /// <summary>Writes into an element, optionally clearing what was there first.</summary>
    bool SetText(UiQuery query, string text, bool clearFirst);

    /// <summary>
    /// Every element the query describes, in the order a person counts them on screen and at most
    /// <paramref name="limit"/> of them. Each one carries the rectangle it occupies, which is what
    /// a step that reads where something is, or that measures from it, needs.
    /// </summary>
    IReadOnlyList<UiElementInfo> FindAll(UiQuery query, int limit);

    /// <summary>
    /// Picks one entry of a list, a drop-down, a set of tabs or a menu: the one showing
    /// <paramref name="text"/>, or the one at <paramref name="itemIndex"/> counted from one when
    /// that is above zero. A drop-down that has not been opened yet is opened on the way.
    /// </summary>
    bool Select(UiQuery query, string text, int itemIndex);

    /// <summary>
    /// Turns a check box, a switch or a radio button on or off, or flips it when
    /// <paramref name="state"/> is null.
    /// </summary>
    bool SetChecked(UiQuery query, bool? state);

    /// <summary>Expands or collapses a node, or flips it: <c>expand</c>, <c>collapse</c>, <c>toggle</c>.</summary>
    bool SetExpanded(UiQuery query, string action);

    /// <summary>Scrolls an element inside its own container until it can be seen.</summary>
    bool ScrollIntoView(UiQuery query);

    /// <summary>
    /// Reads a table or a grid into rows of cell text, at most <paramref name="limit"/> rows.
    /// A list whose rows are rows rather than a real table reads the same way, one entry per row.
    /// </summary>
    UiTable ReadTable(UiQuery query, int limit);
}
