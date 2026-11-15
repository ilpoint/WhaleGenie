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
        => [.. Entries.Where(entry => entry.Kind == TriggerKind.Menu
            && entry.IsEnabled && entry.MenuPageId == pageId)];

    public IReadOnlyList<TriggerEntry> GroupedInterfaceEntries(WindowRule rule)
    {
        var key = GroupKey(rule);
        return [.. InterfaceEntries().Where(entry => GroupKey(entry.WindowTarget) == key)];
    }

    public IReadOnlyList<InterfaceGroupView> InterfaceGroups()
        => [.. InterfaceEntries()
            .GroupBy(entry => GroupKey(entry.WindowTarget))
            .Select(group =>
            {
                var members = group.ToList();
                return new InterfaceGroupView(group.Key, members[0].WindowTarget, [.. members]);
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
                && candidate.Kind == TriggerKind.Menu
                && candidate.MenuPageId == pageId
                && candidate.MenuRow >= 0 && candidate.MenuColumn >= 0)
            .Select(candidate => (candidate.MenuRow, candidate.MenuColumn))
            .ToHashSet();
        for (var row = 0; row < MenuRegionRows; row++)
        {
            for (var column = 0; column < MenuColumns; column++)
            {
                if (occupied.Add((row, column)))
                {
                    entry.MenuPageId = pageId;
                    entry.MenuRow = row;
                    entry.MenuColumn = column;
                    return;
                }
            }
        }

        entry.MenuRow = -1;
        entry.MenuColumn = -1;
    }

    public void ReplaceMacro(string macroName, IEnumerable<TriggerEntry> entries)
    {
        var replacements = entries.Select(entry => entry.Copy()).ToList();
        var old = Entries.Where(entry =>
            string.Equals(entry.MacroName, macroName, StringComparison.OrdinalIgnoreCase)).ToList();
        var keepAt = old.Count > 0 ? Entries.IndexOf(old[0]) : Entries.Count;
        foreach (var entry in old)
        {
            Entries.Remove(entry);
        }

        foreach (var entry in replacements)
        {
            entry.MacroName = macroName;
            Entries.Insert(Math.Min(keepAt++, Entries.Count), entry);
        }

        Changed?.Invoke(this, EventArgs.Empty);
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
