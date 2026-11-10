using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;


namespace WhaleGenie.Mcp;

// What a macro declares and hands back, changed through one operation.
internal static partial class McpPatches
{
    /// <summary>
    /// Changes the macro itself rather than a step: what it is called, how it starts, how it loops,
    /// when it runs, what it watches and how fast it plays. A whole macro sent again was how these
    /// used to change, which also reset every one of them that was not mentioned.
    /// </summary>
    private static Refusal? Fields(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var changed = 0;

        // A macro does not carry this: keeping input apart is a property of the stretch that must
        // not be interrupted, not of a whole macro, and a caller that writes it here would be
        // setting something nothing reads.
        if (operation.ContainsKey("exclusiveInput"))
        {
            return Refused(McpErrors.BadArgument,
                "exclusiveInput is not a macro field any more.",
                "Wrap the steps that must run without another macro's input between them in a "
                + "control.holdInput block instead; that block is what holds the one input door.",
                false, new { field = "exclusiveInput" });
        }

        if (Said(operation, "description") is { } description)
        {
            macro.Description = description;
            touches.Add(new Touch("macro", null, "description"));
            changed++;
        }

        if (Said(operation, "name") is { } name && name.Trim().Length > 0
            && !string.Equals(name.Trim(), macro.Name, StringComparison.Ordinal))
        {
            macro.Name = name.Trim();
            touches.Add(new Touch("macro", null, "name"));
            changed++;
        }

        if (Flag(operation, "enabled") is { } enabled)
        {
            macro.IsEnabled = enabled;
            touches.Add(new Touch("macro", null, "enabled"));
            changed++;
        }

        if (Said(operation, "bindKey") is { } bind)
        {
            macro.BindKey = bind;
            touches.Add(new Touch("macro", null, "bindKey"));
            changed++;
        }

        if (operation.TryGetPropertyValue("delayScale", out var scale))
        {
            var written = Text(scale).Trim();
            if (!double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture, out var delay)
                || delay is < 0.1 or > 10)
            {
                return Refused(McpErrors.BadArgument, $"\"{written}\" is not a delay factor.",
                    "A delay factor is between 0.1 and 10, where 1 plays the macro as written.",
                    true, new { field = "delayScale", value = written });
            }

            macro.DelayScale = delay;
            touches.Add(new Touch("macro", null, "delayScale"));
            changed++;
        }

        if (operation.TryGetPropertyValue("inputRate", out var limit))
        {
            if (limit is null)
            {
                macro.InputRate = null;
            }
            else
            {
                var written = Text(limit).Trim();
                if (!int.TryParse(written, NumberStyles.Integer, CultureInfo.InvariantCulture,
                        out var rate) || rate < 0 || rate > InputLimit.Most)
                {
                    return Refused(McpErrors.BadArgument, $"\"{written}\" is not an input limit.",
                        $"An input limit is between 0 and {InputLimit.Most} events per second, "
                        + "where 0 means no limit and null means follow the program setting.",
                        true, new { field = "inputRate", value = written });
                }

                macro.InputRate = rate;
            }

            touches.Add(new Touch("macro", null, "inputRate"));
            changed++;
        }

        if (Said(operation, "toggleKey") is { } toggle)
        {
            var key = toggle.Trim();
            if (key.Length > 0 && !HotkeyBinding.IsSupported(key))
            {
                return Refused(McpErrors.BadArgument, $"\"{key}\" cannot be read as a key.",
                    "Use a key or a combination written the way a trigger's bindKey is, or an empty "
                    + "string to remove the switch key.",
                    true, new { field = "toggleKey", value = key });
            }

            macro.ToggleKey = key;
            touches.Add(new Touch("macro", null, "toggleKey"));
            changed++;
        }

        if (Enum<MacroTrigger>(macro.TriggerMode, operation, "triggerMode") is { } trigger)
        {
            if (trigger.Refusal is { } refusal)
            {
                return refusal;
            }

            macro.TriggerMode = trigger.Value;
            touches.Add(new Touch("macro", null, "triggerMode"));
            changed++;
        }

        if (Enum<MacroLoop>(macro.LoopMode, operation, "loopMode") is { } loop)
        {
            if (loop.Refusal is { } refusal)
            {
                return refusal;
            }

            macro.LoopMode = loop.Value;
            touches.Add(new Touch("macro", null, "loopMode"));
            changed++;
        }

        if (Enum<ScheduleMode>(macro.ScheduleMode, operation, "scheduleMode") is { } scheduleMode)
        {
            if (scheduleMode.Refusal is { } refusal)
            {
                return refusal;
            }

            macro.ScheduleMode = scheduleMode.Value;
            touches.Add(new Touch("macro", null, "scheduleMode"));
            changed++;
        }

        if (Enum<ScheduleUnit>(macro.ScheduleUnit, operation, "scheduleUnit") is { } scheduleUnit)
        {
            if (scheduleUnit.Refusal is { } refusal)
            {
                return refusal;
            }

            macro.ScheduleUnit = scheduleUnit.Value;
            touches.Add(new Touch("macro", null, "scheduleUnit"));
            changed++;
        }

        if (Said(operation, "scheduleTime") is { } time)
        {
            macro.ScheduleTime = time;
            touches.Add(new Touch("macro", null, "scheduleTime"));
            changed++;
        }

        if (Number(operation, "scheduleInterval") is { } interval)
        {
            macro.ScheduleInterval = interval;
            touches.Add(new Touch("macro", null, "scheduleInterval"));
            changed++;
        }

        if (Said(operation, "watchPath") is { } watchPath)
        {
            macro.WatchPath = watchPath;
            touches.Add(new Touch("macro", null, "watchPath"));
            changed++;
        }

        if (Said(operation, "watchFilter") is { } watchFilter)
        {
            macro.WatchFilter = watchFilter;
            touches.Add(new Touch("macro", null, "watchFilter"));
            changed++;
        }

        if (Flag(operation, "watchSubfolders") is { } watchFolders)
        {
            macro.WatchSubfolders = watchFolders;
            touches.Add(new Touch("macro", null, "watchSubfolders"));
            changed++;
        }

        if (Said(operation, "processName") is { } process)
        {
            macro.ProcessName = process;
            touches.Add(new Touch("macro", null, "processName"));
            changed++;
        }

        if (Number(operation, "idleSeconds") is { } idle)
        {
            macro.IdleSeconds = idle;
            touches.Add(new Touch("macro", null, "idleSeconds"));
            changed++;
        }

        if (operation["windowTarget"] is JsonObject rule)
        {
            macro.WindowTarget = WindowRule.FromJson(rule);
            touches.Add(new Touch("macro", null, "windowTarget"));
            changed++;
        }

        return changed == 0
            ? Refused(McpErrors.BadArgument, "The operation names no field this macro has.",
                "Fields are description, name, enabled, bindKey, delayScale, inputRate, toggleKey, "
                + "triggerMode, loopMode, scheduleMode, scheduleUnit, scheduleTime, scheduleInterval, "
                + "watchPath, watchFilter, watchSubfolders, processName, idleSeconds and windowTarget.",
                true, null)
            : null;
    }
}
