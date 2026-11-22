using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// The menu as the project has it, projected for whichever page draws it: the native window today,
/// the overlay page next. The layout itself is built by the project; this only names the window in
/// front and turns icons into plain names the wire can carry.
/// </summary>
internal sealed class MenuSurface(ProjectWorkspace workspace, IWindowDevice windows,
    TriggerRuntime runtime)
{
    /// <summary>The pages that fit the window in front, or the default ones when none does.</summary>
    public SurfaceMenuSnapshot Snapshot()
    {
        WindowInfo? foreground;
        try
        {
            foreground = windows.Foreground();
        }
        catch
        {
            // A machine that will not name its foreground window still has a menu: the context
            // pages fall back to the default ones.
            foreground = null;
        }

        var layout = workspace.Triggers.BuildMenuLayout(foreground, windows, workspace.Macros);
        return new SurfaceMenuSnapshot(Pages(layout.CommonPages), Pages(layout.ContextPages));
    }

    /// <summary>
    /// Runs the macro one cell points at. The entry is looked up by its stable id rather than by the
    /// name the page saw, so a rename between opening the menu and clicking does not run a stranger.
    /// </summary>
    public bool Activate(string entryId)
    {
        var entry = workspace.Triggers.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, entryId, StringComparison.Ordinal));
        if (entry is null)
        {
            return false;
        }

        var macro = workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), entry.MacroName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (macro is not { IsEnabled: true })
        {
            return false;
        }

        runtime.Triggers.RunOnce(macro);
        return true;
    }

    private static IReadOnlyList<SurfaceMenuPage> Pages(IReadOnlyList<MenuRuntimePage> pages)
        => [.. pages.Select(page => new SurfaceMenuPage(page.PageId, page.IsDefault, page.RuleSummary,
            [.. page.Cells.Select(Cell)]))];

    private static SurfaceMenuCell Cell(MenuRuntimeCell cell)
        => new(cell.Row, cell.Column, cell.Entry.Id, cell.Name,
            cell.Icon is { IsEmpty: false } icon
                ? icon.Kind.ToString().ToLowerInvariant()
                : null,
            cell.Icon is { IsEmpty: false } value ? value.Value : null);
}
