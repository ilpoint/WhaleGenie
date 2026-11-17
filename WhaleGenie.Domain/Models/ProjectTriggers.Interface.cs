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

// The interface group beside a window: its members, its layout and the slots they take.
public sealed partial class ProjectTriggers
{
    public IReadOnlyList<TriggerEntry> MenuMembers(string pageId)
        => [.. Entries.Where(entry => entry.IsEnabled
            && entry.Spec is MenuSpec menu && menu.PageId == pageId)];

    public IReadOnlyList<TriggerEntry> GroupedInterfaceEntries(WindowRule rule)
    {
        var key = GroupKey(rule);
        return [.. InterfaceEntries().Where(entry =>
            entry.Spec is InterfaceSpec group && GroupKey(group.Window) == key)];
    }

    public IReadOnlyList<InterfaceGroupView> InterfaceGroups()
        => [.. InterfaceEntries()
            .Select(entry => (Entry: entry, Group: (InterfaceSpec)entry.Spec))
            .GroupBy(item => GroupKey(item.Group.Window))
            .Select(group =>
            {
                var members = group.ToList();
                return new InterfaceGroupView(group.Key, members[0].Group.Window,
                    [.. members.Select(member => member.Entry)]);
            })];

    public InterfaceLayout LayoutFor(WindowRule rule)
    {
        var key = GroupKey(rule);
        return InterfaceLayouts.FirstOrDefault(layout => layout.GroupKey == key)
            ?? new InterfaceLayout(rule, 16, 64);
    }

    public void StoreLayout(WindowRule rule, int x, int y)
    {
        var key = GroupKey(rule);
        var current = InterfaceLayouts.FirstOrDefault(layout => layout.GroupKey == key);
        var hotkey = current?.ToggleHotkey ?? string.Empty;
        if (current is not null)
        {
            InterfaceLayouts.Remove(current);
        }

        InterfaceLayouts.Add(new InterfaceLayout(rule, x, y, hotkey));
    }

    public void StoreInterfaceHotkey(WindowRule rule, string hotkey)
    {
        var key = GroupKey(rule);
        var current = InterfaceLayouts.FirstOrDefault(layout => layout.GroupKey == key);
        var next = new InterfaceLayout(rule, current?.OffsetX ?? 16, current?.OffsetY ?? 64,
            hotkey.Trim());
        if (current is null)
        {
            InterfaceLayouts.Add(next);
        }
        else
        {
            InterfaceLayouts[InterfaceLayouts.IndexOf(current)] = next;
        }
    }

    public void AssignFirstEmptyMenuCell(TriggerEntry entry, string pageId)
    {
        var occupied = Entries
            .Where(candidate => !ReferenceEquals(candidate, entry)
                && candidate.Spec is MenuSpec { Row: >= 0, Column: >= 0 } placed
                && placed.PageId == pageId)
            .Select(candidate => (((MenuSpec)candidate.Spec).Row, ((MenuSpec)candidate.Spec).Column))
            .ToHashSet();
        for (var row = 0; row < MenuRegionRows; row++)
        {
            for (var column = 0; column < MenuColumns; column++)
            {
                if (occupied.Add((row, column)))
                {
                    entry.MoveMenuCell(pageId, row, column);
                    return;
                }
            }
        }

        entry.MoveMenuCell(pageId, -1, -1);
    }

    public void ReplaceMacro(string macroName, IEnumerable<TriggerEntry> entries)
    {
        var replacements = entries.Select(entry => entry.Copy()).ToList();
        var old = Entries.Where(entry =>
            string.Equals(entry.MacroName, macroName, StringComparison.OrdinalIgnoreCase)).ToList();
        var keepAt = old.Count > 0 ? Entries.IndexOf(old[0]) : Entries.Count;

        // One replace is one change: taking the old rows out one at a time would announce the
        // project to the list, the menu and the runtime once per row.
        BeginBatch();
        try
        {
            foreach (var entry in old)
            {
                Entries.Remove(entry);
            }

            foreach (var entry in replacements)
            {
                entry.MacroName = macroName;
                Entries.Insert(Math.Min(keepAt++, Entries.Count), entry);
            }
        }
        finally
        {
            EndBatch();
        }
    }

    public void RenameMacro(string oldName, string newName)
    {
        foreach (var entry in Entries.Where(entry =>
                     string.Equals(entry.MacroName, oldName, StringComparison.OrdinalIgnoreCase)))
        {
            entry.MacroName = newName;
        }
    }

    public void RemoveMacro(string macroName)
    {
        foreach (var entry in Entries.Where(entry =>
                     string.Equals(entry.MacroName, macroName, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            Entries.Remove(entry);
        }
    }
}
