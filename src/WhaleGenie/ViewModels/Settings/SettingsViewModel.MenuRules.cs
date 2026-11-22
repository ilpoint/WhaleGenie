using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;


namespace WhaleGenie.ViewModels;

// Which windows the menu shows rules for, and the pages and order of those rules.
public partial class SettingsViewModel
{
    public void RefreshMenuRules()
    {
        MenuRules.Clear();
        if (Project is null)
        {
            return;
        }

        var defaults = Project.Triggers.DefaultContextMenuPages();
        MenuRules.Add(new MenuRuleRow("default",
            Strings.Get("Settings.MenuRuleDefault"),
            WhaleGenie.Core.Devices.WindowRule.Empty, true, defaults.Count));
        foreach (var group in Project.Triggers.ContextRuleGroups())
        {
            MenuRules.Add(new MenuRuleRow(group.Key,
                $"{group.Rule.MatchValue} · {Strings.Get($"WindowRule.Short.{group.Rule.How}", group.Rule.How)}",
                group.Rule, false, group.Pages.Count));
        }
        foreach (var rule in Project.Triggers.BlockedMenuRules)
        {
            MenuRules.Add(new MenuRuleRow(
                ProjectTriggers.GroupKey(rule),
                $"{rule.MatchValue} · {Strings.Get($"WindowRule.Short.{rule.How}", rule.How)}",
                rule,
                false,
                0,
                true));
        }
        OnPropertyChanged(nameof(MenuRuleProblem));
        OnPropertyChanged(nameof(MenuLauncherStatus));
    }

    public void AddMenuRule(WhaleGenie.Core.Devices.WindowRule rule)
    {
        if (Project is null || !rule.HasTarget)
        {
            return;
        }

        Project.Triggers.AddContextMenuPage(rule);
        _markMenuChanged?.Invoke();
        RefreshMenuRules();
    }

    public void UpdateMenuRule(MenuRuleRow row, WhaleGenie.Core.Devices.WindowRule rule)
    {
        if (Project is null || row.IsDefault || !rule.HasTarget)
        {
            return;
        }

        if (row.IsBlocked)
        {
            var blocked = Project.Triggers.BlockedMenuRules
                .Select((candidate, index) => (candidate, index))
                .FirstOrDefault(item => ProjectTriggers.GroupKey(item.candidate) == row.GroupKey);
            if (blocked.candidate is null)
            {
                return;
            }
            Project.Triggers.BlockedMenuRules[blocked.index] = rule;
            _markMenuChanged?.Invoke();
            RefreshMenuRules();
            return;
        }

        var pages = Project.Triggers.ContextPagesFor(row.Rule).ToList();
        if (pages.Count == 0)
        {
            return;
        }

        foreach (var page in pages)
        {
            var index = Project.Triggers.MenuPages.IndexOf(page);
            Project.Triggers.MenuPages[index] = page with { Window = rule };
        }
        _markMenuChanged?.Invoke();
        RefreshMenuRules();
    }

    public void RemoveMenuRule(MenuRuleRow row)
    {
        if (Project is null || row.IsDefault)
        {
            return;
        }

        if (row.IsBlocked)
        {
            var blocked = Project.Triggers.BlockedMenuRules.FirstOrDefault(rule =>
                ProjectTriggers.GroupKey(rule) == row.GroupKey);
            if (blocked is not null)
            {
                Project.Triggers.BlockedMenuRules.Remove(blocked);
            }
        }
        else
        {
            Project.Triggers.RemoveContextRule(row.GroupKey);
        }
        _markMenuChanged?.Invoke();
        RefreshMenuRules();
    }

    public void AddMenuPage(MenuRuleRow row)
    {
        if (Project is null || row.IsBlocked)
        {
            return;
        }

        if (row.IsDefault)
        {
            if (Project.Triggers.DefaultContextMenuPages().Count
                < ProjectTriggers.MaxCommonMenuPages)
            {
                Project.Triggers.AddDefaultContextPage();
            }
        }
        else if (Project.Triggers.ContextPagesFor(row.Rule).Count
                 < ProjectTriggers.MaxCommonMenuPages)
        {
            Project.Triggers.AddContextMenuPage(row.Rule);
        }
        _markMenuChanged?.Invoke();
        RefreshMenuRules();
    }

    public void MoveMenuRule(MenuRuleRow row, int step)
    {
        if (Project is null || row.IsDefault || row.IsBlocked)
        {
            return;
        }

        var groups = Project.Triggers.ContextRuleGroups().ToList();
        var currentGroup = groups.FindIndex(group => group.Key == row.GroupKey);
        if (currentGroup < 0)
        {
            return;
        }
        var target = groups[Math.Clamp(currentGroup + step, 0, groups.Count - 1)];
        Project.Triggers.MoveContextRule(row.GroupKey, target.Key);
        _markMenuChanged?.Invoke();
        RefreshMenuRules();
    }

    public void MoveMenuRuleTo(MenuRuleRow row, MenuRuleRow target)
    {
        if (Project is null || row.IsDefault || target.IsDefault
            || row.IsBlocked || target.IsBlocked
            || row.GroupKey == target.GroupKey)
        {
            return;
        }
        Project.Triggers.MoveContextRule(row.GroupKey, target.GroupKey);
        _markMenuChanged?.Invoke();
        RefreshMenuRules();
    }

    private IEnumerable<string> ConflictBindings()
    {
        if (SystemHotkey.Length > 0)
        {
            yield return SystemHotkey;
        }

        if (Project is null)
        {
            yield break;
        }

        foreach (var entry in Project.Triggers.Entries.Where(entry =>
                     entry.IsEnabled && entry.Spec is KeySpec { Binding.Length: > 0 }))
        {
            yield return ((KeySpec)entry.Spec).Binding.Trim();
        }
        foreach (var layout in Project.Triggers.InterfaceLayouts.Where(layout =>
                     layout.ToggleHotkey.Trim().Length > 0))
        {
            yield return layout.ToggleHotkey.Trim();
        }
    }

    private static bool AllowedMenuLauncher(string binding)
        => ProjectTriggers.IsAllowedMenuLauncher(binding);
}
