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
        // The newest row wins for the place. Files written before the comparison spelling was
        // normalized can hold more than one row for the same rule; the last one is the place the
        // panel was moved to after the rule was last saved. A row written by the shared form may
        // carry no toggle key while the older row still does, and the person never asked for the
        // key to be dropped, so the newest non-empty one is kept.
        var matches = InterfaceLayouts.Where(layout => layout.GroupKey == key).ToList();
        if (matches.Count == 0)
        {
            return new InterfaceLayout(rule, 16, 64);
        }

        var newest = matches[^1];
        var hotkey = matches.LastOrDefault(layout => layout.ToggleHotkey.Trim().Length > 0)
            ?.ToggleHotkey ?? string.Empty;
        return new InterfaceLayout(rule, newest.OffsetX, newest.OffsetY, hotkey);
    }

    public void StoreLayout(WindowRule rule, int x, int y)
    {
        var key = GroupKey(rule);
        var hotkey = InterfaceLayouts
            .Where(layout => layout.GroupKey == key)
            .Select(layout => layout.ToggleHotkey)
            .LastOrDefault(written => written.Trim().Length > 0) ?? string.Empty;
        // Remove every row that is now the same rule, not only the first: older files can carry a
        // row under the pre-normalization spelling next to one written by the shared form.
        for (var index = InterfaceLayouts.Count - 1; index >= 0; index--)
        {
            if (InterfaceLayouts[index].GroupKey == key)
            {
                InterfaceLayouts.RemoveAt(index);
            }
        }

        InterfaceLayouts.Add(new InterfaceLayout(rule, x, y, hotkey));
    }

    public void StoreInterfaceHotkey(WindowRule rule, string hotkey)
    {
        var key = GroupKey(rule);
        var current = InterfaceLayouts.LastOrDefault(layout => layout.GroupKey == key);
        var next = new InterfaceLayout(rule, current?.OffsetX ?? 16, current?.OffsetY ?? 64,
            hotkey.Trim());
        for (var index = InterfaceLayouts.Count - 1; index >= 0; index--)
        {
            if (InterfaceLayouts[index].GroupKey == key)
            {
                InterfaceLayouts.RemoveAt(index);
            }
        }

        InterfaceLayouts.Add(next);
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
