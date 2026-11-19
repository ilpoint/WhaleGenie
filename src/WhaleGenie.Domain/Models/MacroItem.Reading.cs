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
        var record = node["recorder"] as JsonObject;

        var macro = new MacroItem
        {
            Name = node["name"]?.GetValue<string>() ?? string.Empty,
            Description = node[FieldNames.Description]?.GetValue<string>() ?? string.Empty,
            Group = node["group"]?.GetValue<string>() ?? string.Empty,
            PositionCapture = ReadEnum(node, "positionCapture", MousePositionMode.SaveCurrentPosition),
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
                    Name = declared["name"]?.GetValue<string>()?.Trim() ?? string.Empty,
                    Default = FormulaJson.Read(declared["default"]),
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
                    Name = declared["name"]?.GetValue<string>()?.Trim() ?? string.Empty,
                    Type = ReadKind(declared),
                    Initial = FormulaJson.Read(declared["initial"]),
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

        return macro;
    }

    /// <summary>The kind a declaration holds, written the way <see cref="VariableTypes.Name"/> spells it.</summary>
    private static ValueKind ReadKind(JsonObject? node)
        => VariableTypes.KindOf(node?["type"]?.GetValue<string>());

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
