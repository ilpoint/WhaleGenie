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
    ToggleMacro,
}

public sealed record GlobalHotkeyMatch(GlobalHotkeyAction Action, string GroupKey = "",
    string MacroName = "")
{
    public static GlobalHotkeyMatch None { get; } = new(GlobalHotkeyAction.None);
}

public sealed record InterfaceHotkeyGroup(string GroupKey, string Binding);

/// <summary>
/// The key that arms and disarms one macro. It is matched even while that macro is disarmed, since
/// arming it again is exactly what the key is for.
/// </summary>
public sealed record MacroToggleHotkey(string MacroName, string Binding);

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

    /// <summary>
    /// The arm/disarm keys of the project's macros. A property rather than a constructor argument so
    /// a plan that has none is written the same way it always was.
    /// </summary>
    public IReadOnlyList<MacroToggleHotkey> MacroToggles { get; init; } = [];

    /// <param name="systemHotkey">The program's own on/off key, read by the caller.</param>
    /// <param name="menuLauncher">The key that opens the project menu, read by the caller.</param>
    public static GlobalHotkeyPlan From(MainViewModel viewModel, string systemHotkey, string menuLauncher)
    {
        var claimed = new List<string>();
        var groups = new List<InterfaceHotkeyGroup>();
        var toggles = new List<MacroToggleHotkey>();
        if (viewModel.IsRunning)
        {
            foreach (var entry in viewModel.Triggers.Entries)
            {
                if (entry.IsEnabled && entry.Spec is KeySpec key && key.Binding.Trim().Length > 0)
                {
                    claimed.Add(key.Binding.Trim());
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

            // A disarmed macro keeps its key in the plan on purpose: the key is how somebody arms it
            // again, so dropping it while it is off would leave the switch one-way.
            foreach (var macro in viewModel.Macros)
            {
                var key = macro.ToggleKey.Trim();
                var name = macro.Name.Trim();
                if (key.Length > 0 && name.Length > 0)
                {
                    toggles.Add(new MacroToggleHotkey(name, key));
                }
            }
        }

        return new GlobalHotkeyPlan(
            systemHotkey,
            viewModel.IsRunning,
            [.. claimed],
            [.. groups],
            menuLauncher)
        {
            MacroToggles = [.. toggles],
        };
    }

    public GlobalHotkeyMatch Match(string key, IReadOnlyCollection<string> held)
    {
        foreach (var claimed in ClaimedBindings)
        {
            if (TriggerKeys.Binds(claimed, key, held))
            {
                return GlobalHotkeyMatch.None;
            }
        }

        if (SystemHotkey.Length > 0 && TriggerKeys.Binds(SystemHotkey, key, held))
        {
            return new GlobalHotkeyMatch(GlobalHotkeyAction.ToggleSystem);
        }

        if (!AutomationEnabled)
        {
            return GlobalHotkeyMatch.None;
        }

        // After the trigger bindings and the system switch: a key that already runs a macro keeps
        // running it, and a macro's arm key never takes the system switch away from it.
        var toggle = MacroToggles.FirstOrDefault(candidate =>
            TriggerKeys.Binds(candidate.Binding, key, held));
        if (toggle is not null)
        {
            return new GlobalHotkeyMatch(GlobalHotkeyAction.ToggleMacro, MacroName: toggle.MacroName);
        }

        var group = InterfaceGroups.FirstOrDefault(candidate =>
            TriggerKeys.Binds(candidate.Binding, key, held));
        return group is null
            ? GlobalHotkeyMatch.None
            : new GlobalHotkeyMatch(GlobalHotkeyAction.ToggleInterfaceGroup, group.GroupKey);
    }
}
