using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// Projects one interface group into the page's shape. The window rules and the anchor stay in C#;
/// this only lists the members and how each should read.
/// </summary>
internal sealed class PanelSurface(ProjectWorkspace workspace, TriggerRuntime runtime)
{
    /// <summary>How each group was left: folded or turned. Outlives one panel window.</summary>
    private readonly Dictionary<string, (bool Collapsed, bool Horizontal)> _views = new(StringComparer.Ordinal);

    /// <summary>Folds or turns one group and answers with the frame the page should now draw.</summary>
    public SurfacePanelSnapshot View(string groupKey, bool? collapsed, bool? horizontal)
    {
        var current = _views.TryGetValue(groupKey, out var remembered) ? remembered : default;
        current = (collapsed ?? current.Collapsed, horizontal ?? current.Horizontal);
        _views[groupKey] = current;
        return Snapshot(groupKey, current.Horizontal, current.Collapsed);
    }

    /// <summary>Runs one member from the panel, by name, if it is still there and switched on.</summary>
    public bool Run(string name)
    {
        var macro = workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (macro is not { IsEnabled: true })
        {
            return false;
        }

        runtime.Triggers.RunOnce(macro);
        return true;
    }

    public SurfacePanelSnapshot Snapshot(string groupKey, bool horizontal, bool collapsed)
    {
        if (_views.TryGetValue(groupKey, out var remembered))
        {
            (collapsed, horizontal) = (remembered.Collapsed, remembered.Horizontal);
        }

        var groups = workspace.Triggers.InterfaceGroups();
        var group = groupKey.Length == 0
            ? groups.FirstOrDefault(candidate => candidate.Entries.Count > 0) ?? groups.FirstOrDefault()
            : groups.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, groupKey, StringComparison.Ordinal));
        var title = group is null
            ? Strings.Get("Interface.Panel")
            : group.Rule.MatchValue.Length > 0 ? group.Rule.MatchValue : Strings.Get("Interface.Panel");
        var items = group is null
            ? []
            : InterfaceMemberResolver.Resolve(group, [.. workspace.Macros])
                .Where(member => member.IsVisibleOnSurface)
                .Select(Item)
                .ToList();

        return new SurfacePanelSnapshot(groupKey, title, horizontal, collapsed, items);
    }

    private static SurfacePanelItem Item(InterfaceGroupMember member)
    {
        var icon = (member.Entry.Spec as MenuSpec)?.Icon;
        var kind = icon is { IsEmpty: false } ? icon.Kind.ToString().ToLowerInvariant() : null;
        return new SurfacePanelItem(member.Display,
            member.State.ToString().ToLowerInvariant(), kind, icon?.Value);
    }
}
