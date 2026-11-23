using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
    /// <summary>The interface behind the page's edit/debug/icon commands, set by the shell.</summary>
    internal IMenuSurfaceActions? Actions { get; set; }

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
        return new SurfaceMenuSnapshot(Pages(layout.CommonPages), Pages(layout.ContextPages),
            CanUseForeground: foreground is not null);
    }

    /// <summary>Fills an empty cell with a macro the native picker returns.</summary>
    public async Task<SurfaceMenuSnapshot> PickCellAsync(string pageId, int row, int column)
    {
        if (Actions is null || await Actions.PickMacroAsync() is not { Length: > 0 } macroName)
        {
            return Snapshot();
        }

        var existing = workspace.Triggers.Entries.FirstOrDefault(entry =>
            entry.Spec is MenuSpec spec && spec.PageId == pageId
            && spec.Row == row && spec.Column == column);
        if (existing is not null)
        {
            existing.MacroName = macroName;
        }
        else
        {
            workspace.Triggers.Entries.Add(new TriggerEntry
            {
                Spec = new MenuSpec(pageId, row, column),
                MacroName = macroName,
                IsEnabled = true,
            });
        }

        workspace.MarkMenuChanged();
        return Snapshot();
    }

    /// <summary>Opens one cell's macro in the editor.</summary>
    public bool Edit(string entryId)
    {
        if (MacroOf(entryId) is not { } macro)
        {
            return false;
        }

        Actions?.OpenEditor(macro.Name);
        return true;
    }

    /// <summary>Starts a debug run of one cell's macro.</summary>
    public bool Debug(string entryId)
    {
        if (MacroOf(entryId) is not { } macro)
        {
            return false;
        }

        Actions?.OpenRun(macro.Name);
        return true;
    }

    /// <summary>Replaces one cell's icon with the one the native picker returns.</summary>
    public async Task<SurfaceMenuSnapshot> ChooseIconAsync(string entryId)
    {
        var entry = EntryOf(entryId);
        if (entry is null || Actions is null)
        {
            return Snapshot();
        }

        if (await Actions.PickIconAsync() is { } icon)
        {
            entry.SetMenuIcon(new MenuIconReference(
                string.Equals(icon.Kind, "image", StringComparison.OrdinalIgnoreCase)
                    ? MenuIconKind.Image
                    : MenuIconKind.Builtin,
                icon.Value));
            workspace.MarkMenuChanged();
        }

        return Snapshot();
    }

    /// <summary>Puts another macro in one cell, keeping the cell's place.</summary>
    public async Task<SurfaceMenuSnapshot> ReplaceAsync(string entryId)
    {
        var entry = EntryOf(entryId);
        if (entry is null || Actions is null)
        {
            return Snapshot();
        }

        if (await Actions.PickMacroAsync() is { Length: > 0 } name)
        {
            entry.MacroName = name;
            workspace.MarkMenuChanged();
        }

        return Snapshot();
    }

    private MacroItem? MacroOf(string entryId)
        => EntryOf(entryId) is { } entry
            ? workspace.Macros.FirstOrDefault(candidate =>
                string.Equals(candidate.Name.Trim(), entry.MacroName.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            : null;

    private TriggerEntry? EntryOf(string entryId)
        => workspace.Triggers.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, entryId, StringComparison.Ordinal));

    /// <summary>Adds one common page, or one more page under an existing page's rule.</summary>
    public SurfaceMenuSnapshot AddPage(string? pageId)
    {
        var triggers = workspace.Triggers;
        var page = pageId is { Length: > 0 }
            ? triggers.MenuPages.FirstOrDefault(candidate => candidate.Id == pageId)
            : null;

        if (page is null)
        {
            if (triggers.CommonMenuPages().Count < ProjectTriggers.MaxCommonMenuPages)
            {
                triggers.AddCommonMenuPage();
                workspace.MarkMenuChanged();
            }
        }
        else if (page.IsDefault)
        {
            if (triggers.DefaultContextMenuPages().Count < ProjectTriggers.MaxCommonMenuPages)
            {
                triggers.AddDefaultContextPage();
                workspace.MarkMenuChanged();
            }
        }
        else if (triggers.ContextPagesFor(page.Window).Count < ProjectTriggers.MaxCommonMenuPages)
        {
            triggers.AddContextMenuPage(page.Window);
            workspace.MarkMenuChanged();
        }

        return Snapshot();
    }

    /// <summary>Removes one page, unless it is the only one its rule has.</summary>
    public SurfaceMenuSnapshot RemovePage(string pageId)
    {
        if (workspace.Triggers.CanRemoveMenuPage(pageId))
        {
            workspace.Triggers.RemoveMenuPage(pageId);
            workspace.MarkMenuChanged();
        }

        return Snapshot();
    }

    /// <summary>Gives the program in front a menu page of its own.</summary>
    public SurfaceMenuSnapshot AddRuleForForeground()
    {
        if (ForegroundProcess() is { Length: > 0 } process && Foreground() is { } foreground)
        {
            workspace.Triggers.EnsureProcessMenuRule(foreground, process, ClassOf(foreground));
            workspace.MarkMenuChanged();
        }

        return Snapshot();
    }

    /// <summary>Stops the menu from opening over the program in front.</summary>
    public SurfaceMenuSnapshot BlockForeground()
    {
        if (ForegroundProcess() is { Length: > 0 } process && Foreground() is { } foreground
            && workspace.Triggers.BlockMenuForProcess(foreground, process, ClassOf(foreground)))
        {
            workspace.MarkMenuChanged();
        }

        return Snapshot();
    }

    /// <summary>Moves one cell's macro to another page and place; the entry keeps its identity.</summary>
    public SurfaceMenuSnapshot MoveCell(string entryId, string pageId, int row, int column)
    {
        var entry = workspace.Triggers.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.Id, entryId, StringComparison.Ordinal));
        if (entry is not null
            && row is >= 0 and < ProjectTriggers.MenuRegionRows
            && column is >= 0 and < ProjectTriggers.MenuColumns)
        {
            entry.MoveMenuCell(pageId, row, column);
            workspace.MarkMenuChanged();
        }

        return Snapshot();
    }

    private WindowInfo? Foreground()
    {
        try
        {
            return windows.Foreground();
        }
        catch
        {
            return null;
        }
    }

    private string? ForegroundProcess()
    {
        if (Foreground() is not { } foreground)
        {
            return null;
        }

        try
        {
            var process = windows.ProcessOf(foreground.Handle);
            return process.Length > 0 ? process : null;
        }
        catch
        {
            return null;
        }
    }

    private string ClassOf(WindowInfo window)
    {
        try
        {
            return windows.ClassOf(window.Handle);
        }
        catch
        {
            return string.Empty;
        }
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
            cell.Icon is { IsEmpty: false } value ? value.Value : null,
            cell.Macro.Description ?? string.Empty);
}
