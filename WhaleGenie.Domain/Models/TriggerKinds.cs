using System.Collections.Generic;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;

namespace WhaleGenie.Models;

/// <summary>
/// The trigger kinds there are, grouped by what they do: the eight a macro waits on, the two that
/// only place it on a surface, and the ones the editor offers.
/// </summary>
/// <remarks>
/// The interface text of a kind lives here and nowhere else. A list that spelled its own labels
/// would drift from the entry's summary the moment one of them gained a kind.
/// </remarks>
public static class TriggerKinds
{
    /// <summary>Every kind, in the order the enum declares them.</summary>
    public static IReadOnlyList<TriggerKind> All { get; } =
    [
        TriggerKind.Key,
        TriggerKind.Color,
        TriggerKind.Interface,
        TriggerKind.Hotstring,
        TriggerKind.Menu,
        TriggerKind.Timer,
        TriggerKind.File,
        TriggerKind.Process,
        TriggerKind.Window,
        TriggerKind.Idle,
    ];

    /// <summary>The kinds the runtime watches. The other two only say where a macro is shown.</summary>
    public static IReadOnlyList<TriggerKind> Monitored { get; } =
    [
        TriggerKind.Key,
        TriggerKind.Color,
        TriggerKind.Hotstring,
        TriggerKind.Timer,
        TriggerKind.File,
        TriggerKind.Process,
        TriggerKind.Window,
        TriggerKind.Idle,
    ];

    /// <summary>The kinds that place a macro on a surface rather than start it.</summary>
    public static IReadOnlyList<TriggerKind> Placement { get; } =
    [
        TriggerKind.Interface,
        TriggerKind.Menu,
    ];

    /// <summary>The kinds the trigger drawer offers. A menu cell is placed on the menu page itself.</summary>
    public static IReadOnlyList<TriggerKind> Editable { get; } =
    [
        TriggerKind.Key,
        TriggerKind.Color,
        TriggerKind.Interface,
        TriggerKind.Hotstring,
        TriggerKind.Timer,
        TriggerKind.File,
        TriggerKind.Process,
        TriggerKind.Window,
        TriggerKind.Idle,
    ];

    public static bool IsPlacement(TriggerKind kind) => kind is TriggerKind.Interface or TriggerKind.Menu;

    public static bool IsMonitored(TriggerKind kind) => !IsPlacement(kind);

    /// <summary>Whether this kind can be spent after one run. A typed abbreviation cannot.</summary>
    public static bool RunsOnce(TriggerSpec spec) => spec switch
    {
        KeySpec key => key.Once,
        ColorSpec colour => colour.Once,
        TimerSpec timer => timer.Once,
        FileSpec file => file.Once,
        ProcessSpec process => process.Once,
        WindowSpec window => window.Once,
        IdleSpec idle => idle.Once,
        _ => false,
    };

    /// <summary>The catalogue key naming this kind; the one place the mapping is written.</summary>
    public static string TextKey(TriggerKind kind) => kind switch
    {
        TriggerKind.Key => "Trigger.Keys",
        TriggerKind.Color => "Trigger.Color",
        TriggerKind.Interface => "Trigger.Interface",
        TriggerKind.Hotstring => "Trigger.Hotstring",
        TriggerKind.Menu => "Trigger.Menu",
        TriggerKind.Timer => "Trigger.Timer",
        TriggerKind.File => "Trigger.File",
        TriggerKind.Process => "Trigger.Process",
        TriggerKind.Window => "Trigger.Window",
        _ => "Trigger.Idle",
    };

    public static string Text(TriggerKind kind) => Strings.Get(TextKey(kind));

    /// <summary>A fresh entry of this kind, with every setting at the value a new macro starts with.</summary>
    public static TriggerSpec Default(TriggerKind kind) => kind switch
    {
        TriggerKind.Key => new KeySpec(),
        TriggerKind.Color => new ColorSpec(),
        TriggerKind.Interface => new InterfaceSpec(WindowRule.Empty),
        TriggerKind.Hotstring => new HotstringSpec(),
        TriggerKind.Menu => new MenuSpec(),
        TriggerKind.Timer => new TimerSpec(),
        TriggerKind.File => new FileSpec(),
        TriggerKind.Process => new ProcessSpec(),
        TriggerKind.Window => new WindowSpec(WindowRule.Empty),
        _ => new IdleSpec(),
    };
}
