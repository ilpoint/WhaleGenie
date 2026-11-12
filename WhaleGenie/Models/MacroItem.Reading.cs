using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;


namespace WhaleGenie.Models;

// Reading a macro back from JSON, and the small readers it uses.
public partial class MacroItem : ObservableObject, ILanguageAware
{
    /// <summary>Rebuilds a macro from the JSON document written by <see cref="ToJson"/>.</summary>
    public static MacroItem FromJson(JsonObject node)
    {
        var position = node["colorPosition"] as JsonObject;
        var record = node["recorder"] as JsonObject;
        var schedule = node["schedule"] as JsonObject;
        var watch = node["watch"] as JsonObject;
        var program = node["process"] as JsonObject;
        var window = node["window"] as JsonObject;

        var macro = new MacroItem
        {
            Name = node["name"]?.GetValue<string>() ?? string.Empty,
            Description = node[FieldNames.Description]?.GetValue<string>() ?? string.Empty,
            Group = node["group"]?.GetValue<string>() ?? string.Empty,
            TriggerMode = ReadEnum(node, "triggerMode", MacroTrigger.KeystrokesButtonInputs),
            LoopMode = ReadEnum(node, "loopMode", MacroLoop.Press),
            BindKey = node["bindKey"]?.GetValue<string>() ?? string.Empty,
            ScheduleMode = ReadEnum(schedule, "mode", ScheduleMode.Interval),
            ScheduleInterval = ReadInt(schedule?["interval"], 5),
            ScheduleUnit = ReadEnum(schedule, "unit", ScheduleUnit.Seconds),
            ScheduleTime = schedule?["time"]?.GetValue<string>() ?? "08:00",
            WatchPath = watch?["path"]?.GetValue<string>() ?? string.Empty,
            WatchChange = ReadEnum(watch, "change", FileChangeKind.Any),
            WatchFilter = watch?["filter"]?.GetValue<string>() ?? "*",
            WatchSubfolders = ReadBool(watch?["subfolders"]),
            ProcessName = program?["name"]?.GetValue<string>() ?? string.Empty,
            ProcessChange = ReadEnum(program, "change", ProcessChangeKind.Started),
            WindowTarget = window is null ? WindowRule.Empty : WindowRule.FromJson(window),
            WindowChange = ReadEnum(node, "windowChange", WindowChangeKind.Appeared),
            IdleSeconds = ReadInt(node["idleSeconds"], 60),
            PositionCapture = ReadEnum(node, "positionCapture", MousePositionMode.SaveCurrentPosition),
            ColorMatch = ReadEnum(node, "colorMatch", ColorMatchCondition.ColorMatches),
            ColorPositionX = ReadInt(position?["x"]),
            ColorPositionY = ReadInt(position?["y"]),
            UseCursorPosition = ReadFlag(node, "useCursorPosition"),
            TriggerOnce = ReadFlag(node, "triggerOnce"),
            HexColor = node["hexColor"]?.GetValue<string>() ?? "#000000",
            ColorTolerance = ReadInt(node["colorTolerance"]),
            RecordMouseMovement = ReadBool(record?["mouseMovement"], true),
            RecordMouseButtons = ReadBool(record?["mouseButtons"], true),
            RecordKeyboardKeys = ReadBool(record?["keyboardKeys"], true),
            RecordGamepadButtons = ReadBool(record?["gamepadButtons"], true),
            RecordDelays = ReadBool(record?["delays"], true),
            IgnoreDelaysBelowMs = ReadInt(record?["ignoreDelaysBelowMs"], 50),
            DelayScale = ReadDouble(node["delayScale"], 1),
            InputRate = ReadOptionalInt(node["inputRate"]),
            ToggleKey = node["toggleKey"]?.GetValue<string>()?.Trim() ?? string.Empty,
            Origin = MacroOrigin.FromJson(node[FieldNames.Origin] as JsonObject),
        };

        if (node["arguments"] is JsonArray arguments)
        {
            foreach (var declared in arguments.OfType<JsonObject>())
            {
                macro.Parameters.Add(new MacroParameter
                {
                    Id = declared["id"]?.GetValue<string>()?.Trim() ?? string.Empty,
                    Name = declared["name"]?.GetValue<string>()?.Trim() ?? string.Empty,
                    Default = declared["default"]?.GetValue<string>() ?? string.Empty,
                    Required = ReadBool(declared["required"], true),
                    Description = declared["description"]?.GetValue<string>() ?? string.Empty,
                    Type = ReadKind(declared),
                    Fields = ReadFields(declared),
                });
            }
        }

        if (node["returns"] is JsonArray results)
        {
            foreach (var declared in results.OfType<JsonObject>())
            {
                macro.Results.Add(new MacroResult
                {
                    Id = declared["id"]?.GetValue<string>()?.Trim() ?? string.Empty,
                    Name = declared["name"]?.GetValue<string>()?.Trim() ?? string.Empty,
                    Description = declared["description"]?.GetValue<string>() ?? string.Empty,
                    Type = ReadKind(declared),
                    Fields = ReadFields(declared),
                });
            }
        }

        if (node[FieldNames.Locals] is JsonArray locals)
        {
            foreach (var declared in locals.OfType<JsonObject>())
            {
                macro.Locals.Add(new MacroLocalVariable
                {
                    Id = declared["id"]?.GetValue<string>()?.Trim() ?? string.Empty,
                    Name = declared["name"]?.GetValue<string>()?.Trim() ?? string.Empty,
                    Type = ReadKind(declared),
                    Initial = declared["initial"]?.GetValue<string>() ?? string.Empty,
                    Description = declared[FieldNames.Description]?.GetValue<string>()
                        ?? string.Empty,
                });
            }
        }

        if (node["steps"] is JsonArray steps)
        {
            foreach (var step in steps)
            {
                if (step is JsonObject child)
                {
                    macro.Steps.Add(MacroStep.FromJson(child));
                }
            }
        }

        if (node[FieldNames.SubMacros] is JsonArray subMacros)
        {
            foreach (var child in subMacros.OfType<JsonObject>())
            {
                macro.SubMacros.Add(FromJson(child));
            }
        }

        if (node["triggers"] is JsonArray triggers)
        {
            foreach (var trigger in triggers.OfType<JsonObject>())
            {
                var entry = TriggerEntry.FromJson(trigger);
                entry.MacroName = macro.Name;
                macro.Triggers.Add(entry);
            }
        }

        if (node["interfaceLayouts"] is JsonArray layouts)
        {
            foreach (var layout in layouts.OfType<JsonObject>())
            {
                macro.InterfaceLayouts.Add(InterfaceLayout.FromJson(layout));
            }
        }

        VariableIds.Settle(macro);
        return macro;
    }

    /// <summary>
    /// The kind a declaration holds. A package written before every type was one vocabulary says
    /// "Flag" for a bool; it means the same thing and is read as one.
    /// </summary>
    private static ValueKind ReadKind(JsonObject? node)
    {
        var written = node?["type"]?.GetValue<string>();
        return written is "Flag"
            ? ValueKind.Bool
            : System.Enum.TryParse<ValueKind>(written, out var parsed) ? parsed : ValueKind.Text;
    }

    private static T ReadEnum<T>(JsonObject? node, string name, T fallback) where T : struct, System.Enum
        => System.Enum.TryParse<T>(node?[name]?.GetValue<string>(), out var parsed) ? parsed : fallback;

    private static bool ReadBool(JsonNode? node, bool fallback = false)
        => node is JsonValue value && value.TryGetValue<bool>(out var result) ? result : fallback;

    private static bool ReadFlag(JsonObject node, string name, bool fallback = false)
        => ReadBool(node[name], fallback);

    private static int ReadInt(JsonNode? node, int fallback = 0)
    {
        if (node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed)
            ? parsed
            : fallback;
    }

    /// <summary>
    /// A number that is only there when somebody chose one. Missing or unreadable means "not
    /// chosen", which is why this cannot share <see cref="ReadInt"/>: its fallback of zero is a
    /// real answer here (no limit), not an empty one.
    /// </summary>
    private static int? ReadOptionalInt(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return Math.Clamp(number, 0, InputLimit.Most);
        }

        return value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed)
            ? Math.Clamp(parsed, 0, InputLimit.Most)
            : null;
    }

    private static double ReadDouble(JsonNode? node, double fallback)
    {
        if (node is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<double>(out var number))
        {
            return number;
        }

        return value.TryGetValue<string>(out var text)
               && double.TryParse(text, System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }
}
