using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

public enum InterfaceMemberState
{
    Visible,
    Disabled,
    Missing,
}

/// <summary>One interface-trigger reference after resolving its project macro.</summary>
public sealed record InterfaceGroupMember(
    TriggerEntry Entry,
    MacroItem? Macro,
    string Name,
    InterfaceMemberState State)
{
    public bool IsVisibleOnSurface => State == InterfaceMemberState.Visible;

    public string Display => State switch
    {
        InterfaceMemberState.Disabled => $"{Name} · {Strings.Get("Interface.Member.Disabled")}",
        InterfaceMemberState.Missing => $"{Name} · {Strings.Get("Interface.Member.Missing")}",
        _ => Name,
    };
}

public static class InterfaceMemberResolver
{
    public static IReadOnlyList<InterfaceGroupMember> Resolve(
        InterfaceGroupView group, IReadOnlyList<MacroItem> macros)
        => Resolve(group.Entries, macros);

    public static IReadOnlyList<InterfaceGroupMember> Resolve(
        IEnumerable<TriggerEntry> entries, IReadOnlyList<MacroItem> macros)
    {
        var byName = macros
            .GroupBy(macro => macro.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(),
                StringComparer.OrdinalIgnoreCase);
        return
        [
            .. entries
                .GroupBy(entry => entry.MacroName.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var entry = group.First();
                    var name = entry.MacroName.Trim();
                    byName.TryGetValue(name, out var macro);
                    var state = macro is null
                        ? InterfaceMemberState.Missing
                        : macro.IsEnabled
                            ? InterfaceMemberState.Visible
                            : InterfaceMemberState.Disabled;
                    return new InterfaceGroupMember(entry, macro, name, state);
                }),
        ];
    }
}
