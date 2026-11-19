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

// Whether the menu is blocked for the window in front, and the order of its rules.
public sealed partial class ProjectTriggers
{
    /// <summary>
    /// Whether the foreground window belongs to a program that must not open the menu. The rule is
    /// checked against the foreground's own process or class instead of the first window the
    /// listing happens to return for that program.
    /// </summary>
    public bool IsMenuBlocked(WindowInfo foreground, IWindowDevice windows)
        => BlockedMenuRules.Any(rule => WindowRuleMatches(rule, foreground, windows));

    public static bool WindowRuleMatches(WindowRule rule, WindowInfo foreground,
        IWindowDevice windows)
    {
        try
        {
            var reading = rule.Reading;
            if (reading.Match is WindowMatch.Handle)
            {
                return rule.Handle != 0 && foreground.Handle == rule.Handle;
            }

            var text = reading.Match switch
            {
                WindowMatch.Process => windows.ProcessOf(foreground.Handle),
                WindowMatch.ClassName => windows.ClassOf(foreground.Handle),
                _ => foreground.Title,
            };
            return WindowHow.Matches(text, rule.MatchValue, reading.Compare, rule.Options);
        }
        catch
        {
            return false;
        }
    }

    public void MoveContextRule(string ruleKey, string targetRuleKey)
    {
        if (ruleKey == targetRuleKey)
        {
            return;
        }

        var groups = ContextRuleGroups().ToList();
        var moving = groups.FirstOrDefault(group => group.Key == ruleKey);
        var target = groups.FirstOrDefault(group => group.Key == targetRuleKey);
        if (moving is null || target is null)
        {
            return;
        }

        var movingIndex = groups.IndexOf(moving);
        var targetIndexBeforeMove = groups.IndexOf(target);
        groups.Remove(moving);
        var insertion = movingIndex < targetIndexBeforeMove
            ? groups.IndexOf(target) + 1
            : groups.IndexOf(target);
        groups.Insert(Math.Clamp(insertion, 0, groups.Count), moving);
        var flattened = groups.SelectMany(group => group.Pages).ToList();
        foreach (var page in ContextMenuPages())
        {
            MenuPages.Remove(page);
        }
        var anchor = MenuPages.LastOrDefault(page =>
            page.Region == MenuPageRegion.Context && page.IsDefault);
        var index = anchor is null ? MenuPages.Count : MenuPages.IndexOf(anchor) + 1;
        foreach (var page in flattened)
        {
            MenuPages.Insert(index++, page);
        }
    }

    public void ResetMenu()
    {
        BlockedMenuRules.Clear();
        MenuPages.Clear();
        EnsureMenuPages();
    }
}
