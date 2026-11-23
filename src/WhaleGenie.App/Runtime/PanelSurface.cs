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
internal sealed class PanelSurface(ProjectWorkspace workspace)
{
    public SurfacePanelSnapshot Snapshot(string groupKey, bool horizontal, bool collapsed)
    {
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
