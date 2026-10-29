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

/// <summary>
/// Step-level edits: change one parameter, insert a step, remove one, move one. It exists because
/// resending a whole macro to change a single number is how a long macro gets truncated on the way
/// in or edited in the wrong place — the client ends up rewriting what it did not mean to touch.
///
/// A patch is one write or none. Every operation is applied to a copy, each insertion is held to the
/// same child-list rules the editor uses, and a refusal leaves the draft exactly as it was, so a
/// wrong third operation cannot leave the first two behind.
/// </summary>
internal static class McpPatches
{
    private const string TopLevel = "";

    /// <summary>One step a patch touched, named after the ids are settled so the answer is usable.</summary>
    /// <summary>What one operation changed: a step, or a field of the macro itself.</summary>
    internal sealed record Touch(string Op, MacroStep? Step, string? Field = null);

    /// <summary>Why a patch was refused. Nothing has been written when this comes back.</summary>
    internal sealed record Refusal(
        string Code, string Message, string Hint, bool Retryable, object? Details);

    internal sealed record Outcome(MacroItem? Macro, IReadOnlyList<Touch> Touches, Refusal? Refused);

    /// <summary>
    /// Applies every operation or returns the first refusal. The macro handed in is never changed:
    /// the caller commits <see cref="Outcome.Macro"/> itself.
    /// </summary>
    public static Outcome Apply(MacroItem macro, JsonArray operations)
    {
        MacroItem copy;
        try
        {
            // The same deep copy the draft snapshot uses: a patch that is going to be refused must
            // not have half of itself sitting in the draft already.
            copy = MacroItem.FromJson(macro.ToJson());
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException)
        {
            return new Outcome(null, [],
                Refused(McpErrors.BadPatch, $"The macro could not be copied: {error.Message}",
                    "Use whalegenie_get_macro and whalegenie_put_macro for this macro.", true, null));
        }

        var touches = new List<Touch>();
        var index = 0;
        foreach (var node in operations)
        {
            index++;
            if (node is not JsonObject operation)
            {
                return new Outcome(null, touches, Refused(McpErrors.BadPatch,
                    $"Operation {index} is not an object.",
                    "Each operation is {op, ...} with op one of set, insert, remove or move.", true,
                    new { index }));
            }

            var refusal = Run(copy, operation, touches);
            if (refusal is not null)
            {
                return new Outcome(null, touches, refusal with { Details = refusal.Details ?? new { index } });
            }
        }

        // New steps came in without ids, and a patch that inserted one before another step existed
        // could have reused a name. Settling here is what makes every later answer able to point at
        // a step, and what the run reports as step.<id>.
        Settle(copy);
        return new Outcome(copy, touches, null);
    }

    /// <summary>
    /// Gives every step of a macro and its submacros an id of its own. A step written by a client
    /// arrives without one, and a run reports such a step as <c>step..outcome</c>, so the ids are
    /// issued here rather than waiting for someone to open the macro in the editor.
    /// </summary>
    public static IReadOnlyList<StepIds.Rename> Settle(MacroItem macro)
        => StepIds.SettleAcross(Documents(macro));

    private static Refusal? Run(MacroItem macro, JsonObject operation, List<Touch> touches)
        => Scope(macro, operation).Error ?? Text(operation["op"]).Trim().ToLowerInvariant() switch
        {
            "set" => Set(macro, operation, touches),
            // The operation name is read without regard to case, so the labels a person sees
            // (setRow, addRow, removeRow) and the spelling a client sends both land here.
            "setrow" => SetRow(macro, operation, touches),
            "addrow" => AddRow(macro, operation, touches),
            "removerow" => RemoveRow(macro, operation, touches),
            "insert" => Insert(macro, operation, touches),
            "remove" => Remove(macro, operation, touches),
            "move" => Move(macro, operation, touches),
            "meta" => Meta(macro, operation, touches),
            "macro" => Fields(macro, operation, touches),
            "declare" => Declare(macro, operation, touches),
            "" => Refused(McpErrors.BadPatch, "An operation has no op.",
                "op is set, setRow, addRow, removeRow, insert, remove, move, meta, macro or declare.", true, null),
            var other => Refused(McpErrors.BadPatch, $"There is no operation \"{other}\".",
                "op is set, setRow, addRow, removeRow, insert, remove, move, meta, macro or declare.", true,
                new { op = other }),
        };

    /// <summary>Changes one parameter's value, which is the edit that used to cost a whole macro.</summary>
    private static Refusal? Set(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var step = Located(macro, operation);
        if (step is null)
        {
            return NoStep(operation);
        }

        var name = Text(operation["param"]).Trim();
        if (name.Length == 0)
        {
            return Refused(McpErrors.BadArgument, "set needs param.",
                "The parameter name is the one whalegenie_get_action lists.", true, null);
        }

        if (!operation.TryGetPropertyValue("value", out var value))
        {
            return Refused(McpErrors.BadArgument, "set needs value.",
                "Send the new value; null clears the field.", true, null);
        }

        var existing = step.Parameters.FirstOrDefault(parameter => parameter.Name == name);
        var declared = (existing is null ? null : step.ParameterDefinition(existing))
            ?? step.Definition?.Parameters.FirstOrDefault(parameter => parameter.Name == name);
        if (declared is null)
        {
            // An action key that is not in the catalogue is a different mistake from a parameter
            // this action does not have, and a client branches on the code to tell them apart.
            if (step.Definition is null)
            {
                return Refused(McpErrors.UnknownAction,
                    $"There is no action \"{step.Type}\" in the catalogue.",
                    "Action keys come from the catalogue; check it with whalegenie_get_action.",
                    true, new { step = step.Type });
            }

            var names = step.Definition.Parameters.Select(parameter => parameter.Name).ToList();
            return Refused(McpErrors.BadArgument,
                $"\"{step.Type}\" has no parameter named \"{name}\".",
                names.Count == 0
                    ? "That action key is not in the catalogue; check it with whalegenie_get_action."
                    : $"Its parameters are: {string.Join(", ", names)}.",
                true, new { step = step.Type, param = name, parameters = names });
        }

        if (declared.Kind == ActionParameterKind.Steps)
        {
            return Refused(McpErrors.BadPatch, $"\"{name}\" holds steps, not one value.",
                "Use insert or remove with parent set to the path of that list, such as 0." + name + ".",
                true, new { step = step.Type, param = name });
        }

        if (HoldsRows(declared.Kind))
        {
            return Refused(McpErrors.BadPatch, $"\"{name}\" holds a table of rows.",
                "A table is changed one row at a time: setRow changes the columns of a row, addRow "
                + "adds one, removeRow takes one out.",
                true, new { step = step.Type, param = name });
        }

        // A window rule is the one record written as an object in the file, so it is the one that can
        // be sent whole. A point or a rectangle is written as one value instead — a variable holding
        // the record, or a field of it — and an object there is a shape the run reads as something
        // else, which is why it is refused with the way it is written rather than half-accepted.
        var recordFields = ActionKnowledge.FieldsOf(declared.Shape);
        if (recordFields.Count > 0 && declared.Shape is not ActionValueShape.WindowRule)
        {
            if (value is JsonObject)
            {
                return Refused(McpErrors.BadPatch,
                    $"\"{name}\" is a {declared.Shape} written as one value, not as an object.",
                    "Send one value: a variable holding the record, or field($record, \"name\") for one "
                    + $"field of it. Its fields are {string.Join(", ", recordFields)}.",
                    true, new { step = step.Type, param = name, fields = recordFields });
            }
        }

        if (declared.Shape is ActionValueShape.WindowRule && value is JsonObject window)
        {
            var unknown = window.Select(field => field.Key)
                .Where(field => !recordFields.Contains(field, StringComparer.OrdinalIgnoreCase))
                .ToList();
            if (unknown.Count > 0)
            {
                return Refused(McpErrors.BadArgument,
                    $"\"{name}\" has no field {string.Join(", ", unknown)}.",
                    $"A window rule holds: {string.Join(", ", recordFields)}.",
                    true, new { step = step.Type, param = name, unknown, fields = recordFields });
            }
        }

        var text = Written(value);
        if (existing is null)
        {
            step.Parameters.Add(new StepParameter
            {
                Name = name,
                Kind = declared.Kind,
                Value = text,
            });
        }
        else
        {
            existing.Value = text;
        }

        touches.Add(new Touch("set", step));
        return null;
    }

    /// <summary>
    /// Changes some columns of one row of a table parameter: a call's arguments, a step's results,
    /// the places a search looks. The row is named by what it already says rather than by its place,
    /// so inserting a row above it does not make this edit land on a different one.
    /// </summary>
    private static Refusal? SetRow(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var refusal = Table(macro, operation, out var step, out var stored, out var declared);
        if (refusal is not null)
        {
            return refusal;
        }

        if (Columns(operation["values"], declared, out var values) is { } badValues)
        {
            return badValues;
        }

        if (Columns(operation["match"], declared, out var match) is { } badMatch)
        {
            return badMatch;
        }

        if (match.Count == 0)
        {
            return Refused(McpErrors.BadArgument, "setRow needs match.",
                "Say which row to change by one or more of its columns, for example "
                + "{\"name\":\"次数\"}. A row can also be added with addRow.",
                true, new { step = step!.Type, param = declared.Name });
        }

        var (rows, refusal2) = RowsOf(step!, stored, declared);
        if (refusal2 is not null)
        {
            return refusal2;
        }

        var found = Matching(rows, match);
        if (found.Count == 0)
        {
            return Refused(McpErrors.BadPatch, "No row of this table matches.",
                "Read the rows with whalegenie_get_macro, or add one with addRow.",
                true, new { step = step!.Type, param = declared!.Name, match });
        }

        if (found.Count > 1)
        {
            return Refused(McpErrors.BadPatch, $"{found.Count} rows match, so which one to change is not decided.",
                "Match on more columns so exactly one row is named.",
                true, new { step = step!.Type, param = declared!.Name, match, rows = found.Count });
        }

        foreach (var (column, value) in values)
        {
            found[0].Columns[column] = value;
        }

        touches.Add(new Touch("setRow", step!));
        return null;
    }

    /// <summary>
    /// Adds one row to a table parameter. Every column the catalogue calls required has to be
    /// filled in: a row missing one is a row the action would skip in silence, which reads as the
    /// step ignoring what was written.
    /// </summary>
    private static Refusal? AddRow(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var refusal = Table(macro, operation, out var step, out var stored, out var declared);
        if (refusal is not null)
        {
            return refusal;
        }

        if (Columns(operation["values"], declared, out var values) is { } badValues)
        {
            return badValues;
        }

        var missing = declared!.Semantics.Columns
            .Where(column => column.Required && !values.ContainsKey(column.Name))
            .Select(column => column.Name)
            .ToList();
        if (missing.Count > 0)
        {
            return Refused(McpErrors.BadArgument,
                $"A row of \"{declared.Name}\" has to fill in {string.Join(", ", missing)}.",
                "Every column the catalogue calls required is part of what a row is.",
                true, new { step = step!.Type, param = declared.Name, missing });
        }

        // The first row of a table that has never had one creates the table. Telling a client to
        // "add the first one with addRow" and then refusing that very call was a loop with no exit.
        if (stored is null)
        {
            stored = new StepParameter { Name = declared.Name, Kind = declared.Kind };
            step!.Parameters.Add(stored);
        }

        var (rows, refusal2) = RowsOf(step!, stored, declared);
        if (refusal2 is not null)
        {
            return refusal2;
        }

        // Two rows that say the same thing in every required column name the same thing twice, and
        // which of them applies would be a guess — the same guess the run refuses to make.
        if (Matching(rows, values).Count > 0)
        {
            return Refused(McpErrors.BadPatch, "A row like this is already in the table.",
                "Change that row with setRow instead of adding a second one.",
                true, new { step = step!.Type, param = declared!.Name, values });
        }

        rows.Add(new StepParameterRow
        {
            Columns = new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase),
        });

        touches.Add(new Touch("addRow", step!));
        return null;
    }

    /// <summary>
    /// Takes one row out of a table parameter, named the way setRow names it. A table with one
    /// answer for a name has no way to say which of two identical rows to delete.
    /// </summary>
    private static Refusal? RemoveRow(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var refusal = Table(macro, operation, out var step, out var stored, out var declared);
        if (refusal is not null)
        {
            return refusal;
        }

        if (Columns(operation["match"], declared, out var match) is { } badMatch)
        {
            return badMatch;
        }

        if (match.Count == 0)
        {
            return Refused(McpErrors.BadArgument, "removeRow needs match.",
                "Say which row to remove by one or more of its columns, for example "
                + "{\"name\":\"次数\"}.",
                true, new { step = step!.Type, param = declared.Name });
        }

        var (rows, refusal2) = RowsOf(step!, stored, declared);
        if (refusal2 is not null)
        {
            return refusal2;
        }

        var found = Matching(rows, match);
        if (found.Count != 1)
        {
            return Refused(McpErrors.BadPatch,
                found.Count == 0 ? "No row of this table matches." : $"{found.Count} rows match.",
                found.Count == 0
                    ? "Read the rows with whalegenie_get_macro."
                    : "Match on more columns so exactly one row is named.",
                true, new { step = step!.Type, param = declared!.Name, match, rows = found.Count });
        }

        rows.Remove(found[0]);
        touches.Add(new Touch("removeRow", step!));
        return null;
    }

    /// <summary>
    /// Changes what a step says about itself: its note, whether it is skipped, its timeout, its
    /// retries, the pauses around it and what a failure does. These are the settings a macro author
    /// adjusts on almost every step, and they had no way in short of sending the whole macro.
    /// </summary>
    private static Refusal? Meta(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var step = Located(macro, operation);
        if (step is null)
        {
            return NoStep(operation);
        }

        var before = step.Meta;
        var next = new StepMeta
        {
            Comment = Said(operation, "comment") ?? before.Comment,
            IsEnabled = Flag(operation, "enabled") ?? before.IsEnabled,
            TimeoutMs = Number(operation, "timeoutMs") ?? before.TimeoutMs,
            RetryCount = Number(operation, "retryCount") ?? before.RetryCount,
            RetryDelayMs = Number(operation, "retryDelayMs") ?? before.RetryDelayMs,
            RetryBackoff = Picked<RetryBackoff>(operation, "retryBackoff") ?? before.RetryBackoff,
            DelayBeforeMs = Number(operation, "delayBeforeMs") ?? before.DelayBeforeMs,
            DelayAfterMs = Number(operation, "delayAfterMs") ?? before.DelayAfterMs,
            OnError = Picked<StepErrorAction>(operation, "onError") ?? before.OnError,
        };

        if (next.TimeoutMs is < 0 or > StepMeta.LongestPauseMs
            || next.DelayBeforeMs is < 0 or > StepMeta.LongestPauseMs
            || next.DelayAfterMs is < 0 or > StepMeta.LongestPauseMs)
        {
            return Refused(McpErrors.BadArgument,
                "A pause or timeout is outside what a step may ask for.",
                $"A length of time is between 0 and {StepMeta.LongestPauseMs} milliseconds.",
                true, new { step = step.Type, longest = StepMeta.LongestPauseMs });
        }

        step.Meta = next;
        touches.Add(new Touch("meta", step));
        return null;
    }

    /// <summary>
    /// Changes the macro itself rather than a step: what it is called, how it starts, how it loops,
    /// when it runs, what it watches and how fast it plays. A whole macro sent again was how these
    /// used to change, which also reset every one of them that was not mentioned.
    /// </summary>
    private static Refusal? Fields(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var changed = 0;

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

        if (Flag(operation, "exclusiveInput") is { } exclusive)
        {
            macro.ExclusiveInput = exclusive;
            touches.Add(new Touch("macro", null, "exclusiveInput"));
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
                "Fields are description, name, enabled, exclusiveInput, bindKey, delayScale, "
                + "triggerMode, loopMode, scheduleMode, scheduleUnit, scheduleTime, scheduleInterval, "
                + "watchPath, watchFilter, watchSubfolders, processName, idleSeconds and windowTarget.",
                true, null)
            : null;
    }

    /// <summary>
    /// Changes what the macro asks a call to fill in, or what it offers to hand back: add, update,
    /// remove or reorder one declaration. These are the fields a call site reads, and they had no
    /// way in short of sending the whole macro — which also lost every declaration nobody retyped.
    /// </summary>
    private static Refusal? Declare(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var kind = Text(operation["kind"]).Trim().ToLowerInvariant();
        if (kind is not ("argument" or "result"))
        {
            return Refused(McpErrors.BadArgument, $"\"{kind}\" is not a kind of declaration.",
                "kind is argument (what a call fills in) or result (what the macro hands back).",
                true, new { kind });
        }

        var action = Text(operation["action"]).Trim().ToLowerInvariant();
        if (action is not ("add" or "update" or "remove" or "move"))
        {
            return Refused(McpErrors.BadArgument, $"\"{action}\" is not a declaration change.",
                "action is add, update, remove or move.", true, new { action });
        }

        var name = Text(operation["name"]).Trim();
        if (name.Length == 0)
        {
            return Refused(McpErrors.BadArgument, "A declaration change needs name.",
                "name is the word the macro reads the value by, such as 数量.", true, null);
        }

        if (!VariableText.IsName(name))
        {
            return Refused(McpErrors.BadArgument, $"\"{name}\" cannot be read back as a variable.",
                "A name is letters, digits and underscores, and does not start with a digit.",
                true, new { name });
        }

        return kind == "argument"
            ? DeclareArgument(macro, operation, action, name, touches)
            : DeclareResult(macro, operation, action, name, touches);
    }

    /// <summary>One argument of a macro: what a call fills in.</summary>
    private static Refusal? DeclareArgument(MacroItem macro, JsonObject operation, string action,
        string name, List<Touch> touches)
    {
        var found = macro.Parameters.FindIndex(parameter =>
            string.Equals(parameter.Name, name, StringComparison.Ordinal));

        switch (action)
        {
            case "add":
                if (found >= 0)
                {
                    return Refused(McpErrors.BadArgument, $"\"{name}\" is already an argument.",
                        "Change it with action=update, or pick another name.", true, new { name });
                }

                if (Typed(operation) is { Refusal: { } badType })
                {
                    return badType;
                }

                var added = new MacroParameter { Name = name };
                if (Fill(added, operation) is { } refusal)
                {
                    return refusal;
                }

                macro.Parameters.Add(added);
                touches.Add(new Touch("declare", null, "argument:" + name));
                return null;

            case "update":
                if (found < 0)
                {
                    return Refused(McpErrors.BadArgument, $"There is no argument named \"{name}\".",
                        "Read the declarations with whalegenie_get_macro_signature.", true, new { name });
                }

                if (Typed(operation) is { Refusal: { } badUpdate })
                {
                    return badUpdate;
                }

                if (Fill(macro.Parameters[found], operation) is { } failed)
                {
                    return failed;
                }

                touches.Add(new Touch("declare", null, "argument:" + name));
                return null;

            case "remove":
                if (found < 0)
                {
                    return Refused(McpErrors.BadArgument, $"There is no argument named \"{name}\".",
                        "Read the declarations with whalegenie_get_macro_signature.", true, new { name });
                }

                macro.Parameters.RemoveAt(found);
                touches.Add(new Touch("declare", null, "argument:" + name));
                return null;

            default:
                return Move(macro.Parameters, found, name, operation, "argument", touches);
        }
    }

    /// <summary>One result of a macro: what a call may ask it to hand back.</summary>
    private static Refusal? DeclareResult(MacroItem macro, JsonObject operation, string action,
        string name, List<Touch> touches)
    {
        var found = macro.Results.FindIndex(result =>
            string.Equals(result.Name, name, StringComparison.Ordinal));

        switch (action)
        {
            case "add":
                if (found >= 0)
                {
                    return Refused(McpErrors.BadArgument, $"\"{name}\" is already a result.",
                        "Change it with action=update, or pick another name.", true, new { name });
                }

                if (Typed(operation) is { Refusal: { } badType })
                {
                    return badType;
                }

                var added = new MacroResult { Name = name };
                if (Fill(added, operation) is { } refusal)
                {
                    return refusal;
                }

                macro.Results.Add(added);
                touches.Add(new Touch("declare", null, "result:" + name));
                return null;

            case "update":
                if (found < 0)
                {
                    return Refused(McpErrors.BadArgument, $"There is no result named \"{name}\".",
                        "Read the declarations with whalegenie_get_macro_signature.", true, new { name });
                }

                if (Typed(operation) is { Refusal: { } badUpdate })
                {
                    return badUpdate;
                }

                if (Fill(macro.Results[found], operation) is { } failed)
                {
                    return failed;
                }

                touches.Add(new Touch("declare", null, "result:" + name));
                return null;

            case "remove":
                if (found < 0)
                {
                    return Refused(McpErrors.BadArgument, $"There is no result named \"{name}\".",
                        "Read the declarations with whalegenie_get_macro_signature.", true, new { name });
                }

                macro.Results.RemoveAt(found);
                touches.Add(new Touch("declare", null, "result:" + name));
                return null;

            default:
                return Move(macro.Results, found, name, operation, "result", touches);
        }
    }

    /// <summary>Puts one declaration at another place in the list, which is the order a call form uses.</summary>
    private static Refusal? Move<T>(List<T> declarations, int found, string name, JsonObject operation,
        string kind, List<Touch> touches)
    {
        if (found < 0)
        {
            return Refused(McpErrors.BadArgument, $"There is no {kind} named \"{name}\".",
                "Read the declarations with whalegenie_get_macro_signature.", true, new { name });
        }

        var asked = Number(operation, "index");
        if (asked is not { } at || at < 0 || at >= declarations.Count)
        {
            return Refused(McpErrors.BadArgument, "move needs an index inside the list.",
                $"index is 0 to {declarations.Count - 1}; it is where the declaration lands.",
                true, new { name, count = declarations.Count });
        }

        var moved = declarations[found];
        declarations.RemoveAt(found);
        declarations.Insert(at, moved);
        touches.Add(new Touch("declare", null, $"{kind}:{name}"));
        return null;
    }

    /// <summary>What a declaration can say, whichever kind of declaration it is.</summary>
    private static Refusal? Fill(MacroParameter argument, JsonObject operation)
    {
        if (Said(operation, "default") is { } fallback)
        {
            argument.Default = fallback;
        }

        if (Flag(operation, "required") is { } required)
        {
            argument.Required = required;
        }

        if (Said(operation, "description") is { } description)
        {
            argument.Description = description;
        }

        if (Typed(operation) is { } type && type.Refusal is null)
        {
            argument.Type = type.Value;
        }

        if (Reading(operation) is { } fields)
        {
            if (fields.Refusal is { } refusal)
            {
                return refusal;
            }

            argument.Fields = fields.Value ?? [];
        }

        // A value that is required and has a default is two answers to one question: the call must
        // give it, and the call may leave it out.
        return argument.Required && argument.Default.Length > 0
            ? Refused(McpErrors.BadArgument,
                $"\"{argument.Name}\" cannot be both required and defaulted.",
                "Drop required, or drop default.", true, new { name = argument.Name })
            : null;
    }

    /// <summary>The same, for a result, which has no such thing as required.</summary>
    private static Refusal? Fill(MacroResult result, JsonObject operation)
    {
        if (Said(operation, "description") is { } description)
        {
            result.Description = description;
        }

        if (Typed(operation) is { } type && type.Refusal is null)
        {
            result.Type = type.Value;
        }

        if (Reading(operation) is { } fields)
        {
            if (fields.Refusal is { } refusal)
            {
                return refusal;
            }

            result.Fields = fields.Value ?? [];
        }

        return null;
    }

    /// <summary>The declared type of a declaration, or null when the operation did not name one.</summary>
    private static (ActionReturnType Value, Refusal? Refusal)? Typed(JsonObject operation)
        => Enum<ActionReturnType>(ActionReturnType.Text, operation, "type");

    /// <summary>The fields of a record declaration, or null when the operation did not name any.</summary>
    private static (List<MacroField>? Value, Refusal? Refusal)? Reading(JsonObject operation)
    {
        if (operation["fields"] is not JsonArray fields)
        {
            return null;
        }

        var read = new List<MacroField>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in fields)
        {
            if (entry is not JsonObject field)
            {
                return (null, Refused(McpErrors.BadArgument, "A field has to be an object.",
                    "A field is {name, type}.", true, null));
            }

            var name = Text(field["name"]).Trim();
            if (name.Length == 0 || !seen.Add(name))
            {
                return (null, Refused(McpErrors.BadArgument,
                    $"\"{name}\" is empty or appears twice in the field list.",
                    "Every field of a record needs a name of its own.", true, new { name }));
            }

            var type = System.Enum.TryParse<ActionReturnType>(Text(field["type"]).Trim(),
                ignoreCase: true, out var parsed)
                ? parsed
                : ActionReturnType.Text;
            read.Add(new MacroField { Name = name, Type = type });
        }

        return (read, null);
    }

    /// <summary>A string field as written, or null when the operation did not name it.</summary>
    private static string? Said(JsonObject operation, string field)
        => operation.TryGetPropertyValue(field, out var node) && node is not null
            ? Text(node)
            : null;

    /// <summary>A flag as written, or null when the operation did not name it.</summary>
    private static bool? Flag(JsonObject operation, string field)
    {
        if (!operation.TryGetPropertyValue(field, out var node) || node is null)
        {
            return null;
        }

        return bool.TryParse(Text(node).Trim(), out var flag) ? flag : null;
    }

    /// <summary>A whole number as written, or null when the operation did not name it.</summary>
    private static int? Number(JsonObject operation, string field)
    {
        if (!operation.TryGetPropertyValue(field, out var node) || node is null)
        {
            return null;
        }

        return int.TryParse(Text(node).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var number)
            ? number
            : null;
    }

    /// <summary>One of a closed set of words, or null when the operation did not name it.</summary>
    private static T? Picked<T>(JsonObject operation, string field) where T : struct, Enum
        => Enum<T>(default, operation, field)?.Value;

    /// <summary>One of an enum's words, read without regard to case, with the words named on refusal.</summary>
    private static (T Value, Refusal? Refusal)? Enum<T>(T current, JsonObject operation, string field)
        where T : struct, Enum
    {
        if (!operation.TryGetPropertyValue(field, out var node) || node is null)
        {
            return null;
        }

        var written = Text(node).Trim();
        if (System.Enum.TryParse<T>(written, ignoreCase: true, out var value))
        {
            return (value, null);
        }

        var words = string.Join(", ", System.Enum.GetNames<T>().Select(name => char.ToLowerInvariant(name[0]) + name[1..]));
        return (current, Refused(McpErrors.BadArgument, $"\"{written}\" is not a {field}.",
            $"One of: {words}.", true, new { field, value = written }));
    }

    /// <summary>
    /// The table a row operation works on. Everything that could be wrong with naming it — no step,
    /// no such parameter, a parameter that is not a table — is answered here, so the operations
    /// above only deal with rows.
    /// </summary>
    private static Refusal? Table(MacroItem macro, JsonObject operation, out MacroStep? step,
        out StepParameter? stored, out ActionParameter declared)
    {
        step = Located(macro, operation);
        stored = null;
        declared = null!;

        if (step is null)
        {
            return NoStep(operation);
        }

        var name = Text(operation["param"]).Trim();
        if (name.Length == 0)
        {
            return Refused(McpErrors.BadArgument, "A row operation needs param.",
                "The parameter name is the one whalegenie_get_action lists.", true, null);
        }

        stored = step.Parameters.FirstOrDefault(parameter => parameter.Name == name);
        var found = (stored is null ? null : step.ParameterDefinition(stored))
            ?? step.Definition?.Parameters.FirstOrDefault(parameter => parameter.Name == name);
        if (found is null)
        {
            var names = step.Definition?.Parameters.Select(parameter => parameter.Name).ToList() ?? [];
            return Refused(McpErrors.BadArgument,
                $"\"{step.Type}\" has no parameter named \"{name}\".",
                names.Count == 0
                    ? "That action key is not in the catalogue; check it with whalegenie_get_action."
                    : $"Its parameters are: {string.Join(", ", names)}.",
                true, new { step = step.Type, param = name, parameters = names });
        }

        if (!HoldsRows(found.Kind))
        {
            return Refused(McpErrors.BadPatch, $"\"{name}\" does not hold rows.",
                "setRow, addRow and removeRow are for a table: a call's arguments, a step's results, "
                + "the places a search looks. A single value is changed with set.",
                true, new { step = step.Type, param = name });
        }

        if (found.Semantics.Columns.Count == 0)
        {
            return Refused(McpErrors.BadPatch, $"\"{name}\" declares no columns.",
                "The catalogue has no column list for this table, so a row cannot be written into it.",
                false, new { step = step.Type, param = name });
        }

        declared = found;
        return null;
    }

    /// <summary>
    /// The rows of a table parameter, created empty when the step has never written one. A column
    /// the catalogue does not declare is refused rather than written: nothing would read it, and a
    /// value nobody reads looks like the step ignoring what it was told.
    /// </summary>
    private static (List<StepParameterRow> Rows, Refusal? Refusal) RowsOf(
        MacroStep step, StepParameter? stored, ActionParameter declared)
    {
        if (stored is null)
        {
            return ([], Refused(McpErrors.BadPatch, "This step has no rows of that kind yet.",
                "Add the first one with addRow.", true,
                new { step = step.Type, param = declared.Name }));
        }

        return (stored.Rows, null);
    }

    /// <summary>One row's worth of columns, refusing names the catalogue does not declare.</summary>
    private static Refusal? Columns(JsonNode? node, ActionParameter declared,
        out Dictionary<string, string> columns)
    {
        columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (node is not JsonObject written)
        {
            return Refused(McpErrors.BadArgument, "This operation needs an object of columns.",
                $"A row of \"{declared.Name}\" has these columns: "
                + $"{string.Join(", ", declared.Semantics.Columns.Select(column => column.Name))}.",
                true, new { param = declared.Name });
        }

        var known = declared.Semantics.Columns.Select(column => column.Name).ToList();
        var unknown = written.Select(pair => pair.Key)
            .Where(column => !known.Contains(column, StringComparer.OrdinalIgnoreCase))
            .ToList();
        if (unknown.Count > 0)
        {
            return Refused(McpErrors.BadArgument,
                $"\"{declared.Name}\" has no column named {string.Join(", ", unknown)}.",
                $"Its columns are: {string.Join(", ", known)}.",
                true, new { param = declared.Name, unknown, columns = known });
        }

        foreach (var (column, value) in written)
        {
            columns[column] = Cell(value);
        }

        return null;
    }

    /// <summary>The rows that say exactly what the given columns say, in the order they are written.</summary>
    private static List<StepParameterRow> Matching(List<StepParameterRow> rows,
        Dictionary<string, string> match)
        => [.. rows.Where(row => match.All(column =>
            string.Equals(row.Text(column.Key).Trim(), column.Value.Trim(),
                StringComparison.OrdinalIgnoreCase)))];

    /// <summary>True for the parameters that hold a table of rows rather than one value.</summary>
    private static bool HoldsRows(ActionParameterKind kind)
        => kind is ActionParameterKind.Region or ActionParameterKind.KeySequence
            or ActionParameterKind.Images or ActionParameterKind.Returns
            or ActionParameterKind.Arguments or ActionParameterKind.Narrow;

    /// <summary>Inserts one step into a named list, held to the child-list rules of that list.</summary>
    private static Refusal? Insert(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        if (operation["step"] is not JsonObject body)
        {
            return Refused(McpErrors.BadArgument, "insert needs step.",
                "The step is one action object, the same shape a macro holds in its steps array.",
                true, null);
        }

        MacroStep step;
        try
        {
            step = MacroStep.FromJson(body);
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException
            or ArgumentException or FormatException)
        {
            return Refused(McpErrors.BadJson, $"That step could not be read: {error.Message}",
                "A step is {type, params} with type an action key from the catalogue.", true, null);
        }

        if (step.Type.Trim().Length == 0)
        {
            return Refused(McpErrors.BadArgument, "The step has no type.",
                "Every step names its action key, for example control.delay.", true, null);
        }

        var parent = Text(operation["parent"]);
        if (!Resolve(Scope(macro, operation).Steps, parent, out var list, out var destination,
                out var pathError))
        {
            return Refused(McpErrors.BadArgument, pathError!,
                "A parent is a path such as 0.then or 0.then.2.else; leave it empty for the top level.",
                true, new { parent });
        }

        if (Fits(destination, step, parent) is { } refused)
        {
            return refused;
        }

        var at = Position(operation["index"], list.Count);
        list.Insert(at, step);
        touches.Add(new Touch("insert", step));
        return null;
    }

    private static Refusal? Remove(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var id = Text(operation["stepId"]).Trim();
        if (!Locate(macro.Steps, id, out var list, out var at, out var step))
        {
            return NoStep(operation);
        }

        list.RemoveAt(at);
        touches.Add(new Touch("remove", step));
        return null;
    }

    private static Refusal? Move(MacroItem macro, JsonObject operation, List<Touch> touches)
    {
        var id = Text(operation["stepId"]).Trim();
        var scope = Scope(macro, operation);
        if (!Locate(scope.Steps, id, out var from, out var at, out var step))
        {
            return NoStep(operation);
        }

        var parent = Text(operation["parent"]);
        if (!Resolve(scope.Steps, parent, out var to, out var destination, out var pathError))
        {
            return Refused(McpErrors.BadArgument, pathError!,
                "A parent is a path such as 0.then; leave it empty for the top level.", true, new { parent });
        }

        if (Fits(destination, step, parent) is { } refused)
        {
            return refused;
        }

        from.RemoveAt(at);
        to.Insert(Position(operation["index"], to.Count), step);
        touches.Add(new Touch("move", step));
        return null;
    }

    /// <summary>
    /// The same question the editor asks before it accepts a drop. A list that names its child kinds
    /// takes only those; an ordinary list takes visible actions.
    /// </summary>
    private static Refusal? Fits(ActionParameter? destination, MacroStep step, string parent)
    {
        if (StepPlacement.FirstIssue(destination, [step]) is not { } issue)
        {
            return null;
        }

        // A key that is not in the catalogue is a different mistake from a step in the wrong list:
        // one is a typo to look up, the other is a placement to change.
        if (issue.Problem == StepPlacementProblem.UnknownStep)
        {
            return Refused(McpErrors.UnknownAction,
                $"\"{step.Type}\" is not an action key in the catalogue.",
                "Action keys come from the catalogue; search it with whalegenie_list_actions.",
                true, new { step = step.Type, parent });
        }

        var allowed = destination is { ChildKeys.Count: > 0 } ? destination.ChildKeys.ToList() : [];
        var where = parent.Length == 0 ? "the top level" : $"\"{parent}\"";
        var message = issue.Problem == StepPlacementProblem.HiddenStep
            ? $"\"{step.Type}\" is a hidden action and cannot stand in {where}."
            : $"\"{step.Type}\" does not belong in {where}.";
        var hint = allowed.Count > 0
            ? $"That list only accepts: {string.Join(", ", allowed)}."
            : "An ordinary list takes visible actions, plus the grouping container the editor makes.";

        return Refused(McpErrors.BadPlacement, message, hint, true,
            new { step = step.Type, parent, allowed });
    }

    /// <summary>
    /// The list a parent path names, and the parameter that declares it. An empty path is the
    /// macro's own list; anything else is step indexes and list names in turn: <c>0.then.2.else</c>.
    /// </summary>
    private static bool Resolve(List<MacroStep> root, string path, out List<MacroStep> list,
        out ActionParameter? destination, out string? error)
    {
        list = root;
        destination = null;
        error = null;

        var trimmed = (path ?? string.Empty).Trim();
        if (trimmed.Length == 0 || trimmed == TopLevel)
        {
            return true;
        }

        var tokens = trimmed.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length % 2 != 0)
        {
            error = $"\"{trimmed}\" has to end with the name of a list, such as 0.then.";
            return false;
        }

        var current = root;
        for (var index = 0; index < tokens.Length; index += 2)
        {
            if (!int.TryParse(tokens[index], NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out var at) || at < 0 || at >= current.Count)
            {
                error = $"\"{tokens[index]}\" is not a position in the list before it.";
                return false;
            }

            var holder = current[at];
            var name = tokens[index + 1];
            var parameter = holder.StepLists.FirstOrDefault(candidate => candidate.Name == name);
            if (parameter is null)
            {
                var names = holder.StepLists.Select(candidate => candidate.Name).ToList();
                error = names.Count == 0
                    ? $"\"{holder.Type}\" holds no lists, so it cannot be a parent."
                    : $"\"{name}\" is not a list of \"{holder.Type}\"; its lists are "
                        + $"{string.Join(", ", names)}.";
                return false;
            }

            current = parameter.Steps;
            destination = holder.ParameterDefinition(parameter);
        }

        list = current;
        return true;
    }

    private static MacroStep? Located(MacroItem macro, JsonObject operation)
    {
        var id = Text(operation["stepId"]).Trim();
        return Locate(Scope(macro, operation).Steps, id, out _, out _, out var step) ? step : null;
    }

    /// <summary>
    /// Which step tree an operation works on: the macro's own, or one of the submacros it carries.
    /// A submacro is written <c>sub:名字</c>; nothing at all means the macro itself.
    /// </summary>
    /// <remarks>
    /// The submacros are separate documents inside one package, and until now a patch could only
    /// reach the outer one: a project that used a submacro could only be changed by sending the
    /// whole macro again, which is exactly what a patch exists to avoid.
    /// </remarks>
    private static (List<MacroStep> Steps, string Label, Refusal? Error) Scope(
        MacroItem macro, JsonObject operation)
    {
        var written = Text(operation["macro"]).Trim();
        if (written.Length == 0 || string.Equals(written, TopLevel, StringComparison.OrdinalIgnoreCase))
        {
            return (macro.Steps, "main", null);
        }

        var name = written.StartsWith("sub:", StringComparison.OrdinalIgnoreCase)
            ? written[4..].Trim()
            : written;

        var sub = macro.SubMacros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));
        if (sub is not null)
        {
            return (sub.Steps, "sub:" + sub.Name.Trim(), null);
        }

        return ([], written, Refused(McpErrors.BadArgument,
            $"There is no submacro named \"{name}\" in \"{macro.Name}\".",
            "Call whalegenie_get_macro: every row of its outline says which macro it belongs to.",
            true, new { macro = name, submacros = macro.SubMacros.Select(child => child.Name).ToList() }));
    }

    /// <summary>Where one step sits: the list holding it and its place in that list.</summary>
    private static bool Locate(List<MacroStep> steps, string id, out List<MacroStep> list,
        out int index, out MacroStep step)
    {
        for (var at = 0; at < steps.Count; at++)
        {
            if (id.Length > 0 && string.Equals(steps[at].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                list = steps;
                index = at;
                step = steps[at];
                return true;
            }

            foreach (var parameter in steps[at].StepLists)
            {
                if (Locate(parameter.Steps, id, out list, out index, out step))
                {
                    return true;
                }
            }
        }

        list = steps;
        index = -1;
        step = null!;
        return false;
    }

    /// <summary>Every step list in the macro and in the submacros it carries, for one set of ids.</summary>
    private static IEnumerable<IEnumerable<MacroStep>> Documents(MacroItem macro)
    {
        yield return macro.Steps;
        foreach (var sub in macro.SubMacros)
        {
            foreach (var document in Documents(sub))
            {
                yield return document;
            }
        }
    }

    private static int Position(JsonNode? node, int count)
        => node is JsonValue value && value.TryGetValue<int>(out var at)
            ? Math.Clamp(at, 0, count)
            : count;

    /// <summary>The value as the field holds it: text, or the number the text says.</summary>
    private static string Written(JsonNode? value) => value switch
    {
        null => string.Empty,
        JsonValue number when number.TryGetValue<decimal>(out var written)
            => written.ToString(CultureInfo.InvariantCulture),
        JsonValue flag when flag.TryGetValue<bool>(out var answer) => answer ? "true" : "false",
        JsonValue text when text.TryGetValue<string>(out var written) => written,
        _ => value.ToJsonString(),
    };

    private static Refusal NoStep(JsonObject operation)
    {
        var id = Text(operation["stepId"]).Trim();
        return Refused(McpErrors.BadArgument, $"There is no step with the id \"{id}\".",
            "Step ids come from whalegenie_get_macro; a step written without one gets a new one on the next write.",
            true, new { stepId = id });
    }

    private static Refusal Refused(string code, string message, string hint, bool retryable,
        object? details) => new(code, message, hint, retryable, details);

    private static string Text(JsonNode? node)
        => node switch
        {
            null => string.Empty,
            JsonValue value when value.TryGetValue<string>(out var text) => text,

            // A number or a truth value written as JSON is still what the caller meant: refusing it
            // because it is not a string would make a client quote every count.
            JsonValue value => value.ToJsonString(),
            _ => node.ToJsonString(),
        };

    /// <summary>
    /// One cell of a row, as the file writes it. A number and a truth value are written the way the
    /// editor writes them rather than refused, because a client sending <c>3</c> for a count means
    /// the same thing as one sending <c>"3"</c>.
    /// </summary>
    private static string Cell(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return string.Empty;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        if (value.TryGetValue<decimal>(out var number))
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        return value.TryGetValue<bool>(out var flag) && flag ? "true" : string.Empty;
    }
}
