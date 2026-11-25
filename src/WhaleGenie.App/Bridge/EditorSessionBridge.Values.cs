using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Recording;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Bridge;

/// <summary>Value editing and the action form: the small editors a step opens.</summary>

internal sealed partial class EditorSessionBridge
{
    private Task<object?> ValueAction(JsonElement parameters)
    {
        EditorValueCommand command;
        try
        {
            command = parameters.Deserialize<EditorValueCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        try
        {
            return Task.FromResult<object?>(command switch
            {
                EditorValueOpenCommand open => OpenValue(open),
                EditorValueKeyCommand key => Required().ValueCommand(new ValueEditorCommand(
                    key.Action,
                    Token: key.Token,
                    Name: key.Name,
                    Sign: key.Sign,
                    Group: key.Group,
                    Text: key.Text,
                    Path: key.Path,
                    Target: key.Target,
                    After: key.After,
                    Number: key.Number,
                    Direction: key.Direction,
                    At: key.At,
                    Width: key.Width,
                    Append: key.Append)),
                EditorValueConfirmCommand => ValueConfirm(),
                EditorValueCancelCommand => Required().CancelValue(),
                _ => throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField")),
            });
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    /// <summary>
    /// A confirmed value changes a step parameter. The outcome's bare snapshot is not a bridge
    /// snapshot — it has no tabs, active document or envelope revision — so the confirmed one is
    /// wrapped the same way every other edit is; otherwise the page's revision gate can drop it
    /// and the new value never reaches the row.
    /// </summary>
    private EditorValueOutcome ValueConfirm()
    {
        var outcome = Required().ConfirmValue();
        if (!outcome.Closed || !outcome.Applied || _documents.Snapshot() is not { } snapshot)
        {
            return outcome;
        }

        return outcome with { State = Envelope(snapshot) };
    }

    private EditorValueSnapshot OpenValue(EditorValueOpenCommand open)
    {
        if (open.Step.Length == 0 || open.Parameter.Length == 0)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));
        }

        if (open.RowIndex is >= 0 && open.Column is { Length: > 0 })
        {
            return Required().OpenRowValue(open.Step, open.Parameter, open.RowIndex.Value,
                open.Column);
        }

        if (open.ConditionPath is not null && open.ConditionField is { Length: > 0 })
        {
            return Required().OpenConditionValue(open.Step, open.Parameter,
                open.ConditionPath, open.ConditionField);
        }

        return Required().OpenValue(open.Step, open.Parameter);
    }

    private Task<object?> ActionOpen(JsonElement parameters)
    {
        var type = OptionalString(parameters, "type");
        var stepId = OptionalString(parameters, "stepId");
        var parentId = OptionalString(parameters, "parentId") ?? string.Empty;
        var list = OptionalString(parameters, "list") ?? string.Empty;
        var target = OptionalString(parameters, "targetStepId") ?? string.Empty;
        var before = parameters.TryGetProperty("before", out var direction) && direction.GetBoolean();
        try
        {
            return Task.FromResult<object?>(stepId is { Length: > 0 }
                ? Required().OpenStepDraft(stepId)
                : type is { Length: > 0 }
                    ? Required().OpenActionDraft(type, parentId, list, target, before)
                    : throw new BridgeFailureException(BridgeErrors.BadArgument,
                        Strings.Get("Bridge.EditorNeedsStep")));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

}
