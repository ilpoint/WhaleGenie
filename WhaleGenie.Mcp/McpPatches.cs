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
internal static partial class McpPatches
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
}
