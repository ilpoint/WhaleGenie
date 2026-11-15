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

// Writing the macro to JSON: the tree, its submacros, arguments, results and locals.
public partial class MacroItem : ObservableObject, ILanguageAware
{
    /// <summary>Serialises the macro into the JSON document the runtime consumes.</summary>
    public JsonObject ToJson(bool includeTriggers = false)
    {
        var steps = new JsonArray();
        foreach (var step in Steps)
        {
            steps.Add(step.ToJson());
        }

        var node = new JsonObject
        {
            ["name"] = Name,
            [FieldNames.Description] = Description,
            ["triggerMode"] = TriggerMode.ToString(),
            ["loopMode"] = LoopMode.ToString(),
            ["bindKey"] = BindKey,
            ["schedule"] = new JsonObject
            {
                ["mode"] = ScheduleMode.ToString(),
                ["interval"] = ScheduleInterval,
                ["unit"] = ScheduleUnit.ToString(),
                ["time"] = ScheduleTime,
            },
            ["watch"] = new JsonObject
            {
                ["path"] = WatchPath,
                ["change"] = WatchChange.ToString(),
                ["filter"] = WatchFilter,
                ["subfolders"] = WatchSubfolders,
            },
            ["process"] = new JsonObject
            {
                ["name"] = ProcessName,
                ["change"] = ProcessChange.ToString(),
            },
            ["window"] = WindowTarget.ToJson(),
            ["windowChange"] = WindowChange.ToString(),
            ["idleSeconds"] = IdleSeconds,
            ["positionCapture"] = PositionCapture.ToString(),
            ["colorMatch"] = ColorMatch.ToString(),
            ["colorPosition"] = new JsonObject
            {
                ["x"] = ColorPositionX,
                ["y"] = ColorPositionY,
            },
            ["useCursorPosition"] = UseCursorPosition,
            ["triggerOnce"] = TriggerOnce,
            ["hexColor"] = HexColor,
            ["colorTolerance"] = ColorTolerance,
            ["delayScale"] = DelayScale,
            ["recorder"] = new JsonObject
            {
                ["mouseMovement"] = RecordMouseMovement,
                ["mouseButtons"] = RecordMouseButtons,
                ["keyboardKeys"] = RecordKeyboardKeys,
                ["gamepadButtons"] = RecordGamepadButtons,
                ["delays"] = RecordDelays,
                ["ignoreDelaysBelowMs"] = IgnoreDelaysBelowMs,
            },
            ["arguments"] = ArgumentsJson(),
            ["returns"] = ResultsJson(),
            [FieldNames.Locals] = LocalsJson(),
            ["steps"] = steps,
            [FieldNames.SubMacros] = SubMacrosJson(),
        };

        // Only a macro that was actually put in a group carries the field: an empty one would say
        // "in no group" where the file could simply not mention it.
        if (Group.Trim().Length > 0)
        {
            node["group"] = Group.Trim();
        }

        // The same rule for the two settings that belong to this macro alone: an absent field means
        // "follow the program" and "no switch key", so a macro that never touched them stays short.
        if (InputRate is { } rate)
        {
            node["inputRate"] = Math.Clamp(rate, 0, InputLimit.Most);
        }

        if (ToggleKey.Trim().Length > 0)
        {
            node["toggleKey"] = ToggleKey.Trim();
        }

        if (includeTriggers)
        {
            var triggers = new JsonArray();
            foreach (var trigger in Triggers)
            {
                triggers.Add(trigger.ToJson());
            }

            node["triggers"] = triggers;
            node["interfaceLayouts"] = new JsonArray(
                [.. InterfaceLayouts.Select(layout => layout.ToJson())]);
        }

        return node;
    }

    /// <summary>
    /// The shape of a child macro document. A child is only callable, so trigger, recorder and
    /// interface fields are deliberately absent rather than written as irrelevant defaults.
    /// </summary>
    public JsonObject ToSubMacroJson()
    {
        var steps = new JsonArray();
        foreach (var step in Steps)
        {
            steps.Add(step.ToJson());
        }

        var node = new JsonObject
        {
            ["name"] = Name.Trim(),
            [FieldNames.Description] = Description,
            ["arguments"] = ArgumentsJson(),
            ["returns"] = ResultsJson(),
            [FieldNames.Locals] = LocalsJson(),
            ["steps"] = steps,
        };

        if (Origin is { } origin)
        {
            node[FieldNames.Origin] = origin.ToJson();
        }

        if (SubMacros.Count > 0)
        {
            node[FieldNames.SubMacros] = SubMacrosJson();
        }

        return node;
    }

    private JsonArray SubMacrosJson()
    {
        var children = new JsonArray();
        foreach (var child in SubMacros)
        {
            children.Add(child.ToSubMacroJson());
        }

        return children;
    }

    /// <summary>
    /// The declarations as the file holds them. A macro that declares neither writes neither, so a
    /// macro that is called for what it does rather than for what it takes stays as short as it was.
    /// </summary>
    private JsonArray ArgumentsJson()
    {
        var array = new JsonArray();
        foreach (var parameter in Parameters)
        {
            // A name left empty is written too: recovery has to bring the unfinished row back so
            // the declaration problem is still visible after a stop, and the package writer refuses
            // to save a macro whose declaration has a fatal problem.
            var written = new JsonObject
            {
                ["name"] = parameter.Name.Trim(),
                ["default"] = FormulaJson.Write(parameter.Default),
                ["required"] = parameter.Required,
                ["description"] = parameter.Description,
                ["type"] = parameter.Type.ToString(),
            };

            // Only a record has fields, and only a record whose author said which ones is worth
            // writing down: an empty list would say "a record with nothing in it" rather than "not
            // said", and the two are different answers.
            if (FieldsJson(parameter.Fields) is { } fields)
            {
                written["fields"] = fields;
            }

            array.Add(written);
        }

        return array;
    }

    /// <summary>The names this macro may hand back, with what each one is.</summary>
    private JsonArray ResultsJson()
    {
        var array = new JsonArray();
        foreach (var result in Results)
        {
            // See ArgumentsJson: an empty row is an unfinished declaration, not something to drop.
            var written = new JsonObject
            {
                ["name"] = result.Name.Trim(),
                ["description"] = result.Description,
                ["type"] = result.Type.ToString(),
            };

            if (FieldsJson(result.Fields) is { } fields)
            {
                written["fields"] = fields;
            }

            array.Add(written);
        }

        return array;
    }

    /// <summary>
    /// The values the macro keeps for itself. Written the way the other declarations are, with
    /// <c>initial</c> rather than <c>default</c>: the runtime writes it at the start of every run
    /// instead of falling back to it when the name is missing.
    /// </summary>
    private JsonArray LocalsJson()
    {
        var array = new JsonArray();
        foreach (var local in Locals)
        {
            // See ArgumentsJson: an empty row is an unfinished declaration, not something to drop.
            var written = new JsonObject
            {
                ["name"] = local.Name.Trim(),
                ["type"] = local.Type.ToString(),
                ["initial"] = FormulaJson.Write(local.Initial),
                [FieldNames.Description] = local.Description,
            };

            array.Add(written);
        }

        return array;
    }

    /// <summary>
    /// The fields a record declaration names, or nothing when it names none. Written only when there
    /// are some: an empty list would say "a record with nothing in it", which is a different answer
    /// from "the author has not said", and only one of the two is worth acting on.
    /// </summary>
    private static JsonArray? FieldsJson(List<MacroField> fields)
    {
        var named = fields.Where(field => field.Name.Trim().Length > 0).ToList();
        return named.Count == 0
            ? null
            : [.. named.Select(field => new JsonObject
            {
                ["name"] = field.Name.Trim(),
                ["type"] = field.Type.ToString(),
            })];
    }

    /// <summary>The fields a declaration row names, read back out of the list it was written as.</summary>
    private static List<MacroField> ReadFields(JsonObject declared)
    {
        if (declared["fields"] is not JsonArray fields)
        {
            return [];
        }

        return
        [
            .. fields.OfType<JsonObject>().Select(field => new MacroField
            {
                Name = field["name"]?.GetValue<string>()?.Trim() ?? string.Empty,
                Type = ReadEnum(field, "type", ValueKind.Text),
            }),
        ];
    }
}
