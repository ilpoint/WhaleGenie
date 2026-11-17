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

// The layout the menu window is drawn from, and the problems worth saying about it.
public sealed partial class ProjectTriggers
{
    public MenuRuntimeLayout BuildMenuLayout(WindowInfo? foreground,
        IWindowDevice windows, IReadOnlyList<MacroItem> macros)
    {
        var byName = macros
            .GroupBy(macro => macro.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);

        MenuRuntimePage Page(MenuPage page)
        {
            var cells = Entries
                .Select(entry => (Entry: entry, Menu: entry.Spec as MenuSpec))
                .Where(item => item.Entry.IsEnabled && item.Menu is not null
                    && item.Menu.PageId == page.Id
                    && item.Menu.Row is >= 0 and < MenuRegionRows
                    && item.Menu.Column is >= 0 and < MenuColumns)
                .Select(item => (item.Entry, Menu: item.Menu!,
                    Macro: byName.GetValueOrDefault(item.Entry.MacroName)))
                .Where(item => item.Macro is { IsEnabled: true })
                .GroupBy(item => (item.Menu.Row, item.Menu.Column))
                .Select(group =>
                {
                    var item = group.First();
                    return new MenuRuntimeCell(
                        item.Menu.Row,
                        item.Menu.Column,
                        item.Menu.Icon,
                        item.Entry,
                        item.Macro!);
                })
                .OrderBy(cell => cell.Row)
                .ThenBy(cell => cell.Column)
                .ToList();
            return new MenuRuntimePage(page.Id, page.IsDefault,
                page.IsDefault
                    ? Strings.Get("Menu.DefaultContext")
                    : $"{page.Window.MatchValue} · {Strings.Get($"WindowRule.Short.{page.Window.How}", page.Window.How)}",
                cells);
        }

        var common = CommonMenuPages().Select(Page).ToList();
        if (common.Count == 0)
        {
            common.Add(Page(AddCommonMenuPage()));
        }

        IReadOnlyList<MenuPage>? selectedContext = null;
        if (foreground is not null)
        {
            foreach (var candidate in ContextRuleGroups())
            {
                try
                {
                    var rule = candidate.Pages[0].Window;
                    if (WindowRuleMatches(rule, foreground, windows))
                    {
                        selectedContext = candidate.Pages;
                        break;
                    }
                }
                catch
                {
                    // An unreadable rule leaves the next page to answer.
                }
            }
        }

        var contextPages = selectedContext is null
            ? DefaultContextMenuPages().Select(Page).ToList()
            : selectedContext.Select(Page).ToList();
        if (contextPages.Count == 0)
        {
            contextPages.Add(Page(DefaultContextMenuPage()));
        }
        return new MenuRuntimeLayout(common, contextPages);
    }

    /// <summary>
    /// What is wrong with the menu as written. <paramref name="knowsIcon"/> answers whether an icon
    /// name still exists in this build; without it, a name is left alone rather than reported as
    /// missing — a project saved by a build that had that icon is not broken.
    /// </summary>
    public IReadOnlyList<string> MenuProblems(IReadOnlyList<MacroItem> macros,
        Func<string, bool>? knowsIcon = null)
    {
        var problems = new List<string>();
        if (CommonMenuPages().Count > MaxCommonMenuPages)
        {
            problems.Add(Strings.Get("Menu.Problem.TooManyPages"));
        }

        if (MenuPages.GroupBy(page => page.Id).Any(group => group.Count() > 1))
        {
            problems.Add(Strings.Get("Menu.Problem.PageId"));
        }

        if (ContextMenuPages().Any(page => !page.Window.HasTarget))
        {
            problems.Add(Strings.Get("Menu.Problem.RuleEmpty"));
        }
        if (BlockedMenuRules.Any(rule => !rule.HasTarget))
        {
            problems.Add(Strings.Get("Menu.Problem.BlockedRuleEmpty"));
        }

        var names = macros.Select(macro => macro.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (entry, menu) in Entries
                     .Select(entry => (Entry: entry, Menu: entry.Spec as MenuSpec))
                     .Where(item => item.Menu is not null))
        {
            if (menu is null)
            {
                continue;
            }

            if (!MenuPages.Any(page => page.Id == menu.PageId))
            {
                problems.Add(Strings.Format("Menu.Problem.PageMissing", entry.MacroName));
            }
            if (menu.Row is < 0 or >= MenuRegionRows
                || menu.Column is < 0 or >= MenuColumns)
            {
                problems.Add(Strings.Format("Menu.Problem.CellRange", entry.MacroName));
            }
            if (!names.Contains(entry.MacroName))
            {
                problems.Add(Strings.Format("Menu.Problem.MacroMissing", entry.MacroName));
            }
            if (menu.Icon is { IsEmpty: false } icon
                && icon.Kind == MenuIconKind.Builtin
                && knowsIcon is not null
                && !knowsIcon(icon.Value))
            {
                problems.Add(Strings.Format("Menu.Problem.Icon", entry.MacroName));
            }
        }

        foreach (var duplicate in Entries
                     .Select(entry => (Entry: entry, Menu: entry.Spec as MenuSpec))
                     .Where(item => item.Entry.IsEnabled && item.Menu is not null)
                     .GroupBy(item => (item.Menu!.PageId, item.Menu.Row, item.Menu.Column))
                     .Where(group => group.Count() > 1))
        {
            problems.Add(Strings.Format("Menu.Problem.CellConflict",
                duplicate.Key.Row + 1, duplicate.Key.Column + 1));
        }

        return problems;
    }
}
