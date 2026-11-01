using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Runtime;

public enum GlobalHotkeyAction
{
    None,
    ToggleSystem,
    ToggleInterfaceGroup,
}

public sealed record GlobalHotkeyMatch(GlobalHotkeyAction Action, string GroupKey = "")
{
    public static GlobalHotkeyMatch None { get; } = new(GlobalHotkeyAction.None);
}

public sealed record InterfaceHotkeyGroup(string GroupKey, string Binding);

/// <summary>
/// An immutable copy of the shortcuts a hook callback may answer. Hook threads never read the
/// project or a window: they match against this plan and post the resulting action to the UI.
/// </summary>
public sealed record GlobalHotkeyPlan(
    string SystemHotkey,
    bool AutomationEnabled,
    IReadOnlyList<string> ClaimedBindings,
    IReadOnlyList<InterfaceHotkeyGroup> InterfaceGroups,
    string MenuLauncher = "")
{
    public static GlobalHotkeyPlan Empty { get; } =
        new(string.Empty, false, [], [], string.Empty);

    public static GlobalHotkeyPlan From(MainViewModel viewModel)
    {
        var claimed = new List<string>();
        var groups = new List<InterfaceHotkeyGroup>();
        var menuLauncher = Localization.LocalSettings.LoadMenuLauncher();
        if (viewModel.IsRunning)
        {
            foreach (var entry in viewModel.Triggers.Entries)
            {
                if (entry.IsEnabled && entry.Kind == TriggerKind.Key
                    && entry.BindKey.Trim().Length > 0)
                {
                    claimed.Add(entry.BindKey.Trim());
                }
            }

            if (menuLauncher.Length > 0)
            {
                claimed.Add(menuLauncher);
            }
            var liveGroups = viewModel.Triggers.InterfaceGroups()
                .Select(group => group.Key)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var layout in viewModel.Triggers.InterfaceLayouts
                         .Where(layout => layout.ToggleHotkey.Trim().Length > 0
                             && liveGroups.Contains(layout.GroupKey)))
            {
                groups.Add(new InterfaceHotkeyGroup(layout.GroupKey, layout.ToggleHotkey.Trim()));
            }
        }

        return new GlobalHotkeyPlan(
            Localization.LocalSettings.LoadSystemHotkey(),
            viewModel.IsRunning,
            [.. claimed],
            [.. groups],
            menuLauncher);
    }

    public GlobalHotkeyMatch Match(string key, IReadOnlyCollection<string> held)
    {
        foreach (var claimed in ClaimedBindings)
        {
            if (MacroTriggerService.Binds(claimed, key, held))
            {
                return GlobalHotkeyMatch.None;
            }
        }

        if (SystemHotkey.Length > 0 && MacroTriggerService.Binds(SystemHotkey, key, held))
        {
            return new GlobalHotkeyMatch(GlobalHotkeyAction.ToggleSystem);
        }

        if (!AutomationEnabled)
        {
            return GlobalHotkeyMatch.None;
        }

        var group = InterfaceGroups.FirstOrDefault(candidate =>
            MacroTriggerService.Binds(candidate.Binding, key, held));
        return group is null
            ? GlobalHotkeyMatch.None
            : new GlobalHotkeyMatch(GlobalHotkeyAction.ToggleInterfaceGroup, group.GroupKey);
    }
}
