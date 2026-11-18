using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// What a run of one macro needs, read off the macro once. It holds no observable object: a hook
/// thread and a run thread both read it, and neither may touch the model the editor is writing.
/// </summary>
public sealed record MacroRef(string Name, IReadOnlyList<MacroStep> Steps, MacroSignature Signature,
    double DelayScale, int? InputRate)
{
    public static MacroRef Of(MacroItem macro) => new(
        macro.Name.Trim(), [.. macro.Steps], macro.Callable.Signature, macro.DelayScale, macro.InputRate);
}

/// <summary>
/// One trigger that is armed: what it watches, which macro it starts, and how it reads. The
/// <paramref name="Looking"/> line is computed when the plan is published, so a worker never asks
/// the catalog for words.
/// </summary>
public sealed record ArmedTrigger(string Id, TriggerSpec Spec, MacroRef Macro, string Looking);

/// <summary>
/// The armed triggers and the macros they start, as one immutable object. The interface publishes
/// a new one; hook and timer threads only read the finished instance.
/// </summary>
/// <remarks>
/// An entry that is switched off, points at a macro that is gone, or points at a macro that is
/// switched off is not in here at all. That decision is made once, when the plan is published,
/// rather than by every one of the eight watches on every pass.
/// </remarks>
public sealed class TriggerPlan
{
    public static TriggerPlan Empty { get; } = new([], [], [], string.Empty);

    private readonly Dictionary<TriggerKind, IReadOnlyList<ArmedTrigger>> _byKind;

    private TriggerPlan(IReadOnlyList<ArmedTrigger> entries, IReadOnlyList<MacroRef> macros,
        IReadOnlyList<string> known, string menuLauncher)
    {
        Entries = entries;
        Macros = macros;
        Known = known;
        MenuLauncher = menuLauncher;
        _byKind = entries
            .GroupBy(entry => entry.Spec.Kind)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ArmedTrigger>)[.. group]);
    }

    public IReadOnlyList<ArmedTrigger> Entries { get; }

    /// <summary>
    /// Every entry the project holds, armed or not. A "run once" trigger that is switched off and on
    /// again is still the same entry, and has still had its turn.
    /// </summary>
    public IReadOnlyList<string> Known { get; }

    /// <summary>Every macro the plan can start, for a call step or a run by name.</summary>
    public IReadOnlyList<MacroRef> Macros { get; }

    public string MenuLauncher { get; }

    public IReadOnlyList<ArmedTrigger> OfKind(TriggerKind kind)
        => _byKind.TryGetValue(kind, out var armed) ? armed : [];

    public MacroRef? Macro(string name)
        => Macros.FirstOrDefault(macro =>
            string.Equals(macro.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <param name="menuLauncher">
    /// The key that opens the project menu, read by the caller. Passing it in keeps this a pure
    /// function of the project: publishing a plan must not touch the disk on the interface thread.
    /// </param>
    public static TriggerPlan From(ProjectTriggers triggers, IReadOnlyList<MacroItem> macros,
        string menuLauncher)
    {
        var byName = new Dictionary<string, MacroItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var macro in macros)
        {
            var name = macro.Name.Trim();
            if (name.Length > 0)
            {
                byName.TryAdd(name, macro);
            }
        }

        var entries = new List<ArmedTrigger>();
        var carried = new Dictionary<string, MacroRef>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in triggers.Entries)
        {
            if (!entry.IsEnabled || !TriggerKinds.IsMonitored(entry.Kind))
            {
                continue;
            }

            var name = entry.MacroName.Trim();
            if (!byName.TryGetValue(name, out var macro) || !macro.IsEnabled)
            {
                continue;
            }

            if (!carried.TryGetValue(name, out var reference))
            {
                reference = MacroRef.Of(macro);
                carried[name] = reference;
            }

            entries.Add(new ArmedTrigger(entry.Id, entry.Spec, reference, entry.Summary));
        }

        return new TriggerPlan(entries, [.. carried.Values],
            [.. triggers.Entries.Select(entry => entry.Id)], menuLauncher);
    }
}
