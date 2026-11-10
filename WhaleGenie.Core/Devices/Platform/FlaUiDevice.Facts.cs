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

// What an element says about itself, and a table read out of a grid.
public sealed partial class FlaUiDevice
{
    /// <summary>
    /// Works the element's own action, without the mouse. Only the invoke pattern is used: an
    /// element that does not offer it is answered for rather than clicked, because falling back to
    /// a mouse click would move the pointer and bring the window forward — the two things this
    /// exists to avoid. A step that wants the element clicked can say so with the click action.
    /// </summary>
    public bool Invoke(UiQuery query)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException(RunKeys.ElementNotFound, Describe(query));

        try
        {
            if (!element.Patterns.Invoke.IsSupported)
            {
                return false;
            }

            element.Patterns.Invoke.Pattern.Invoke();
            return true;
        }
        catch (Exception error) when (Recoverable(error))
        {
            return false;
        }
    }

    /// <summary>
    /// Everything the element says about itself. Each part is read on its own, because a provider
    /// that will not answer one question usually answers the others — an element whose value is
    /// unreadable still knows whether it is enabled.
    /// </summary>
    public UiElementFacts? Facts(UiQuery query)
    {
        Require();
        var element = Find(query);
        if (element is null)
        {
            return null;
        }

        var bounds = Bounds(element);
        return new UiElementFacts(
            Flag(() => element.IsEnabled),
            Flag(() => !element.IsOffscreen),
            Read(() => TextOf(element)),
            Read(() => element.Name),
            Read(() => element.AutomationId),
            Read(() => element.ControlType.ToString()),
            new ScreenPoint(bounds.X, bounds.Y),
            new ScreenSize(bounds.Width, bounds.Height));
    }

    /// <summary>
    /// A yes-or-no question put to a provider. An element that refuses to answer counts as asked-
    /// about and answered "no", which is the safe reading: a macro that waits for something to
    /// become usable is not helped by being told it is ready when nobody knows.
    /// </summary>
    private static bool Flag(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (Exception error) when (Recoverable(error))
        {
            return false;
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

    public UiTable ReadTable(UiQuery query, int limit)
    {
        Require();
        var element = Find(query) ?? throw new DeviceActionException(RunKeys.ElementNotFound, Describe(query));
        var rows = Math.Max(1, limit);

        try
        {
            // A grid is the usual way a table says it is one, and it is the only way that knows
            // its own rows and columns rather than leaving them to be guessed at.
            if (element.ControlType == ControlType.DataGrid)
            {
                var view = element.AsDataGridView();
                return new UiTable(Titles(view.Header), [.. Rows(view.Rows.Select(RowText), rows)]);
            }

            if (element.Patterns.Grid.IsSupported)
            {
                var grid = element.AsGrid();
                return new UiTable(Titles(grid.Header), [.. Rows(grid.Rows.Select(RowText), rows)]);
            }

            // Nothing that calls itself a table: the rows are then whatever looks like one, and
            // each cell is read off the controls inside that row.
            var condition = element.ConditionFactory.ByControlType(ControlType.DataItem);
            return new UiTable([],
            [
                .. Rows(
                    element.FindAllDescendants(condition)
                        .Select(row => (IReadOnlyList<string>)
                            [.. row.FindAllChildren().Select(TextOf)]),
                    rows),
            ]);
        }
        catch (Exception error) when (Recoverable(error))
        {
            throw new DeviceActionException(RunKeys.ElementNotATable, Describe(query));
        }
    }

    /// <summary>
    /// The names of a grid's columns, which it keeps in a strip of its own. A table with no such
    /// strip hands back nothing rather than failing: the data is still there to read.
    /// </summary>
    private static IReadOnlyList<string> Titles(GridHeader? header)
        => header is null ? [] : [.. header.Columns.Select(item => item.Text ?? string.Empty)];

    /// <summary>The names of a grid view's columns, on a WinForms grid.</summary>
    private static IReadOnlyList<string> Titles(DataGridViewHeader? header)
        => header is null ? [] : [.. header.Columns.Select(item => item.Text ?? string.Empty)];

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
}
