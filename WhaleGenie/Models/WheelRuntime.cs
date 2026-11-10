using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// The layout a wheel is drawn from, and one place inside it.
/// <summary>One position as the wheel shows it, after the scene and the global scene were combined.</summary>
public sealed record WheelRuntimeSlot(
    WheelSlotRef Reference,
    string Name,
    MacroItem? Macro,
    MenuIconReference? Icon,
    bool FromProgram,
    string Note = "")
{
    /// <summary>The note wins over the macro name wherever a label is shown.</summary>
    public string Label => Note.Trim().Length > 0 ? Note.Trim() : Name.Trim();
}

/// <summary>An immutable snapshot handed to the floating wheel window.</summary>
public sealed record WheelRuntimeLayout(
    IReadOnlyList<WheelRuntimeSlot> Slots,
    string SceneName,
    WheelSource Source,
    bool WideOuter,
    int ThirdCapacity = 0,
    bool HideEmptyOuter = false)
{
    public WheelRuntimeSlot? At(WheelSlotRef reference)
        => Slots.FirstOrDefault(slot => slot.Reference == reference);
}
