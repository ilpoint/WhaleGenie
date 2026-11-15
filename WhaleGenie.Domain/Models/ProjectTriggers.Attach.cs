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

// Keeping the entries in step with the macros: attaching, replacing and merging a project.
public sealed partial class ProjectTriggers
{
    public void Attach(IReadOnlyList<MacroItem> macros)
    {
        foreach (var macro in macros)
        {
            macro.Triggers.Clear();
            var entries = ForMacro(macro.Name);
            if (entries.Count == 0)
            {
                entries = [TriggerEntry.FromMacro(macro)];
            }

            macro.Triggers.AddRange(entries.Select(entry => entry.Copy()));
            macro.InterfaceLayouts.Clear();
            macro.InterfaceLayouts.AddRange(InterfaceLayouts.Select(layout => layout with { }));
            macro.RefreshTriggers();
        }
    }

    public void ReplaceAll(ProjectTriggers other)
    {
        Entries.Clear();
        foreach (var entry in other.Entries)
        {
            Entries.Add(entry.Copy());
        }

        InterfaceLayouts.Clear();
        foreach (var layout in other.InterfaceLayouts)
        {
            InterfaceLayouts.Add(layout with { });
        }

        MenuPages.Clear();
        foreach (var page in other.MenuPages)
        {
            MenuPages.Add(page with { });
        }
        BlockedMenuRules.Clear();
        foreach (var rule in other.BlockedMenuRules)
        {
            BlockedMenuRules.Add(rule with { });
        }

        // The wheel's positions are part of what a project holds, so opening one has to take them
        // with it rather than leave the previous project's wheel on screen.
        Wheel = other.Wheel.Copy();
        EnsureMenuPages();
    }

    /// <summary>Merges menu pages and blocked rules from an imported package.</summary>
    public void MergeMenuFrom(ProjectTriggers imported, IReadOnlyList<MacroItem> importedMacros)
    {
        var importedNames = importedMacros.Select(macro => macro.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var defaultPages = DefaultContextMenuPages().ToList();
        if (defaultPages.Count == 0)
        {
            defaultPages.Add(DefaultContextMenuPage());
        }
        var defaultIndex = 0;
        var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var page in imported.MenuPages)
        {
            if (page.Region == MenuPageRegion.Context && page.IsDefault)
            {
                if (defaultIndex >= defaultPages.Count)
                {
                    defaultPages.Add(AddDefaultContextPage());
                }
                idMap[page.Id] = defaultPages[defaultIndex++].Id;
                continue;
            }

            if (page.Region == MenuPageRegion.Common && CommonMenuPages().Count >= MaxCommonMenuPages)
            {
                continue;
            }

            var id = MenuPages.Any(existing => existing.Id == page.Id)
                ? NewMenuPageId()
                : page.Id;
            idMap[page.Id] = id;
            MenuPages.Add(page with { Id = id });
        }

        foreach (var entry in Entries.Where(entry => entry.Kind == TriggerKind.Menu
                     && importedNames.Contains(entry.MacroName)
                     && idMap.TryGetValue(entry.MenuPageId, out _)))
        {
            entry.MenuPageId = idMap[entry.MenuPageId];
        }

        foreach (var rule in imported.BlockedMenuRules)
        {
            if (!BlockedMenuRules.Any(existing => GroupKey(existing) == GroupKey(rule)))
            {
                BlockedMenuRules.Add(rule with { });
            }
        }

        EnsureMenuPages();
    }
}
