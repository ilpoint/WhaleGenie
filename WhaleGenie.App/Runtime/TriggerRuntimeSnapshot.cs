using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

/// <summary>
/// A worker-safe copy of every trigger entry and macro reference. The editor publishes a new one
/// on the UI thread; hook and timer threads only read the completed instance.
/// </summary>
public sealed class TriggerRuntimeSnapshot
{
    public static TriggerRuntimeSnapshot Empty { get; } = new([], [], string.Empty, []);

    private TriggerRuntimeSnapshot(IReadOnlyList<TriggerEntry> entries,
        IReadOnlyList<MacroItem> macros, string menuLauncher,
        IReadOnlyList<MenuPage> menuPages)
    {
        Entries = entries;
        Macros = macros;
        MenuLauncher = menuLauncher;
        MenuPages = menuPages;
    }

    public IReadOnlyList<TriggerEntry> Entries { get; }

    public IReadOnlyList<MacroItem> Macros { get; }

    public string MenuLauncher { get; }

    public IReadOnlyList<MenuPage> MenuPages { get; }

    public static TriggerRuntimeSnapshot From(ProjectTriggers triggers,
        IReadOnlyList<MacroItem> macros)
        => new(
            [.. triggers.Entries.Select(entry => entry.Copy())],
            [.. macros],
            LocalSettings.LoadMenuLauncher(),
            [.. triggers.MenuPages]);

    /// <summary>
    /// A view over stable live entries. Used by the old check-only service constructor, whose
    /// watcher dictionaries hold the same entry objects between polls.
    /// </summary>
    internal static TriggerRuntimeSnapshot FromLive(ProjectTriggers triggers,
        IReadOnlyList<MacroItem> macros)
        => new([.. triggers.Entries], [.. macros], LocalSettings.LoadMenuLauncher(),
            [.. triggers.MenuPages]);

    public IReadOnlyList<TriggerEntry> ForMacro(string macroName)
        => [.. Entries.Where(entry =>
            string.Equals(entry.MacroName, macroName, StringComparison.OrdinalIgnoreCase))];

    public IReadOnlyList<TriggerEntry> HotstringEntries()
        => [.. Entries.Where(entry => entry.IsEnabled && entry.Kind == TriggerKind.Hotstring
            && entry.Abbreviation.Length > 0)];
}

/// <summary>Atomically replaces the immutable trigger snapshot used by worker callbacks.</summary>
public sealed class TriggerRuntimeSnapshotStore
{
    private TriggerRuntimeSnapshot _current = TriggerRuntimeSnapshot.Empty;

    public TriggerRuntimeSnapshot Current => Volatile.Read(ref _current);

    public void Publish(ProjectTriggers triggers, IReadOnlyList<MacroItem> macros)
        => Volatile.Write(ref _current, TriggerRuntimeSnapshot.From(triggers, macros));
}
