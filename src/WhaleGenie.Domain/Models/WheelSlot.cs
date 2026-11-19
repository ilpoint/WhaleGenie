using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// One place on the wheel: the action it carries and how it is named.
/// <summary>What one position runs. An empty macro name means the position is left blank.</summary>
public sealed class WheelSlot
{
    public string Macro { get; set; } = string.Empty;

    /// <summary>A note shown in the centre instead of the macro name when the person wrote one.</summary>
    public string Note { get; set; } = string.Empty;

    public MenuIconReference? Icon { get; set; }

    public bool IsEmpty => Macro.Trim().Length == 0;

    public string Label => Note.Trim().Length > 0 ? Note.Trim() : Macro.Trim();

    public WheelSlot Copy() => new()
    {
        Macro = Macro,
        Note = Note,
        Icon = Icon is null ? null : Icon with { },
    };

    public JsonObject ToJson()
    {
        var node = new JsonObject { ["macro"] = Macro };
        if (Note.Trim().Length > 0)
        {
            node["note"] = Note;
        }

        if (Icon is { IsEmpty: false } icon)
        {
            node["icon"] = icon.ToJson();
        }

        return node;
    }

    public static WheelSlot FromJson(JsonObject? node) => new()
    {
        Macro = node?["macro"]?.GetValue<string>() ?? string.Empty,
        Note = node?["note"]?.GetValue<string>() ?? string.Empty,
        Icon = node?["icon"] is JsonObject icon ? MenuIconReference.FromJson(icon) : null,
    };
}
