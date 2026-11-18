using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Models;

// Taking in another set of triggers whole, and merging the part of it a package brings along.
public sealed partial class ProjectTriggers
{
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

        foreach (var entry in Entries.Where(entry => importedNames.Contains(entry.MacroName)
                     && entry.Spec is MenuSpec menu
                     && idMap.ContainsKey(menu.PageId)))
        {
            var menu = (MenuSpec)entry.Spec;
            entry.MoveMenuCell(idMap[menu.PageId], menu.Row, menu.Column);
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
