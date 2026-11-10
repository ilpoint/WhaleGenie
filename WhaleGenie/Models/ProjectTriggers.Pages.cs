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

// Adding and removing menu pages, and the rules that decide whether one may go.
public sealed partial class ProjectTriggers
{
    public MenuPage DefaultContextMenuPage()
        => MenuPages.FirstOrDefault(page =>
               page.Region == MenuPageRegion.Context && page.IsDefault)
           ?? EnsureDefaultContextPage();

    public MenuPage AddCommonMenuPage()
    {
        var common = CommonMenuPages();
        if (common.Count >= MaxCommonMenuPages)
        {
            return common[^1];
        }

        var page = new MenuPage(NewMenuPageId(), MenuPageRegion.Common, WindowRule.Empty);
        MenuPages.Add(page);
        return page;
    }

    public MenuPage AddContextMenuPage(WindowRule rule, bool first = false)
    {
        var page = new MenuPage(NewMenuPageId(), MenuPageRegion.Context, rule);
        if (first)
        {
            var at = MenuPages
                .Select((candidate, index) => (candidate, index))
                .FirstOrDefault(item => item.candidate.Region == MenuPageRegion.Context
                    && !item.candidate.IsDefault);
            MenuPages.Insert(at.candidate is null ? MenuPages.Count : at.index, page);
            return page;
        }

        var last = MenuPages
            .Select((candidate, index) => (candidate, index))
            .LastOrDefault(item => item.candidate.Region == MenuPageRegion.Context
                && !item.candidate.IsDefault
                && GroupKey(item.candidate.Window) == GroupKey(rule));
        if (last.candidate is null)
        {
            MenuPages.Add(page);
        }
        else
        {
            MenuPages.Insert(last.index + 1, page);
        }
        return page;
    }

    public MenuPage AddDefaultContextPage()
    {
        var page = new MenuPage(NewMenuPageId(), MenuPageRegion.Context,
            WindowRule.Empty, IsDefault: true);
        var last = MenuPages
            .Select((candidate, index) => (candidate, index))
            .LastOrDefault(item => item.candidate.Region == MenuPageRegion.Context
                && item.candidate.IsDefault);
        MenuPages.Insert(last.candidate is null ? MenuPages.Count : last.index + 1, page);
        return page;
    }

    public void RemoveMenuPage(string pageId)
    {
        if (MenuPages.FirstOrDefault(page => page.Id == pageId) is not { } page
            || (page.IsDefault && DefaultContextMenuPages().Count <= 1))
        {
            return;
        }

        foreach (var entry in Entries.Where(entry =>
                     entry.Kind == TriggerKind.Menu && entry.MenuPageId == pageId).ToList())
        {
            Entries.Remove(entry);
        }

        MenuPages.Remove(page);
    }

    /// <summary>
    /// Whether a page may be removed from the menu. Each paging region keeps its first page so the
    /// runtime view always has a stable starting position.
    /// </summary>
    public bool CanRemoveMenuPage(string pageId)
    {
        var page = MenuPages.FirstOrDefault(candidate => candidate.Id == pageId);
        if (page is null)
        {
            return false;
        }

        var siblings = page.Region == MenuPageRegion.Common
            ? CommonMenuPages()
            : page.IsDefault
                ? DefaultContextMenuPages()
                : ContextPagesFor(page.Window);
        return siblings.Count > 1 && siblings[0].Id != pageId;
    }

    public void RemoveContextRule(string ruleKey)
    {
        var pages = ContextMenuPages()
            .Where(page => GroupKey(page.Window) == ruleKey)
            .ToList();
        var pageIds = pages.Select(page => page.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in Entries.Where(entry =>
                     entry.Kind == TriggerKind.Menu && pageIds.Contains(entry.MenuPageId)).ToList())
        {
            Entries.Remove(entry);
        }
        foreach (var page in pages)
        {
            MenuPages.Remove(page);
        }
    }
}
