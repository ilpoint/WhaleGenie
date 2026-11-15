using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// Which entries belong to a macro or a view, and which menu cells are free.
public sealed partial class ProjectTriggers
{
    public IReadOnlyList<TriggerEntry> ForMacro(string macroName)
        => [.. Entries.Where(entry =>
            string.Equals(entry.MacroName, macroName, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// Whether the macro is listed under a view. These are filters, not places a macro is filed:
    /// one macro can be in several of them, and a macro with no trigger at all is still in
    /// <see cref="MacroView.All"/>.
    /// </summary>
    public bool InView(string macroName, MacroView view)
    {
        if (view == MacroView.All)
        {
            return true;
        }

        var entries = ForMacro(macroName);
        return view switch
        {
            MacroView.Keys => entries.Any(entry => entry.Kind == TriggerKind.Key),
            MacroView.Interface => entries.Any(entry => entry.Kind == TriggerKind.Interface),
            MacroView.Hotstring => entries.Any(entry => entry.Kind == TriggerKind.Hotstring),
            MacroView.Menu => entries.Any(entry => entry.Kind == TriggerKind.Menu),
            MacroView.Wheel => Wheel.Uses(macroName),
            // A watched pixel is a watch like a schedule or a file, not a key.
            _ => entries.Any(entry => entry.Kind is TriggerKind.Color or TriggerKind.Timer
                or TriggerKind.File or TriggerKind.Process or TriggerKind.Window or TriggerKind.Idle),
        };
    }

    public IReadOnlyList<TriggerEntry> InterfaceEntries()
        => [.. Entries.Where(entry => entry.IsEnabled && entry.Kind == TriggerKind.Interface
            && entry.WindowTarget.HasTarget)];

    public IReadOnlyList<TriggerEntry> HotstringEntries()
        => [.. Entries.Where(entry => entry.IsEnabled && entry.Kind == TriggerKind.Hotstring
            && entry.Abbreviation.Length > 0)];

    public IReadOnlyList<TriggerEntry> MenuMembers()
        => [.. Entries.Where(entry => entry.IsEnabled && entry.Kind == TriggerKind.Menu)];

    /// <summary>
    /// The first cell no menu entry occupies, across the common pages in order. A macro created
    /// from the menu view goes here, so "add from this page" really does add it to this page.
    /// </summary>
    public (string PageId, int Row, int Column)? FirstFreeMenuCell()
    {
        foreach (var page in CommonMenuPages())
        {
            var used = Entries
                .Where(entry => entry.Kind == TriggerKind.Menu && entry.MenuPageId == page.Id)
                .Select(entry => (entry.MenuRow, entry.MenuColumn))
                .ToHashSet();
            for (var row = 0; row < MenuRegionRows; row++)
            {
                for (var column = 0; column < MenuColumns; column++)
                {
                    if (!used.Contains((row, column)))
                    {
                        return (page.Id, row, column);
                    }
                }
            }
        }

        return null;
    }

    public IReadOnlyList<MenuPage> CommonMenuPages()
        => [.. MenuPages.Where(page => page.Region == MenuPageRegion.Common)];

    public IReadOnlyList<MenuPage> ContextMenuPages()
        => [.. MenuPages.Where(page => page.Region == MenuPageRegion.Context && !page.IsDefault)];

    public IReadOnlyList<MenuPage> DefaultContextMenuPages()
        => [.. MenuPages.Where(page => page.Region == MenuPageRegion.Context && page.IsDefault)];

    public IReadOnlyList<MenuPage> ContextPagesFor(WindowRule rule)
    {
        var key = GroupKey(rule);
        return [.. MenuPages.Where(page => page.Region == MenuPageRegion.Context
            && !page.IsDefault && GroupKey(page.Window) == key)];
    }

    public IReadOnlyList<MenuRuleGroupView> ContextRuleGroups()
        => [.. ContextMenuPages()
            .GroupBy(page => GroupKey(page.Window))
            .Select(group =>
            {
                var pages = group.ToList();
                return new MenuRuleGroupView(group.Key, pages[0].Window, [.. pages]);
            })];

    /// <summary>
    /// Finds the ordinary menu rule for one program, creating an empty one first when needed. The
    /// returned page is the one the runtime menu should select for the foreground window.
    /// </summary>
    public MenuPage EnsureProcessMenuRule(WindowInfo foreground, string process, string className)
    {
        var key = process.Trim();
        var existing = ContextRuleGroups().FirstOrDefault(group =>
            group.Rule.How == WindowHow.ProcessEquals
            && string.Equals(group.Rule.Process, key, StringComparison.OrdinalIgnoreCase));
        return existing?.Pages[0]
               ?? AddContextMenuPage(new WindowRule(
                   foreground.Title,
                   key,
                   className,
                   foreground.Handle,
                   WindowHow.ProcessEquals), first: true);
    }

    /// <summary>Adds one program-wide launcher block, answering false when it already exists.</summary>
    public bool BlockMenuForProcess(WindowInfo foreground, string process, string className)
    {
        var key = process.Trim();
        if (BlockedMenuRules.Any(rule =>
                rule.How == WindowHow.ProcessEquals
                && string.Equals(rule.Process, key, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        BlockedMenuRules.Add(new WindowRule(
            foreground.Title,
            key,
            className,
            foreground.Handle,
            WindowHow.ProcessEquals));
        return true;
    }
}
