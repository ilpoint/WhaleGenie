using System;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Editing;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

// The action form's draft edits: one typed command, answered with the whole form again.
internal sealed partial class EditorSessionBridge
{
    private Task<object?> ActionCommand(JsonElement parameters)
    {
        EditorActionCommand command;
        try
        {
            command = parameters.Deserialize<EditorActionCommand>(CommandOptions)
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
            return Task.FromResult<object?>(ApplyActionCommand(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplyActionCommand(EditorActionCommand command)
    {
        var form = Required().ActionForm;
        return command switch
        {
            EditorActionPageCommand page => form.SetPage(page.Page),
            EditorActionFieldCommand field => form.PatchField(field.Name, field.Field, field.Value),
            EditorActionRowAddCommand row => form.AddRow(row.Name),
            EditorActionRowRemoveCommand row => form.RemoveRow(row.Name, row.Index),
            EditorActionRowMoveCommand row => form.MoveRow(row.Name, row.Index, row.By),
            EditorActionRowCellCommand row => form.PatchRow(row.Name, row.Index, row.Column,
                row.Value, row.Parts),
            EditorActionReturnsCommand returns => form.PatchReturn(returns.Key, returns.Ticked,
                returns.Name),
            EditorActionReturnsFixNameCommand fix => form.FixReturnName(fix.Key),
            EditorActionCallArgumentCommand call => form.PatchCallArgument(call.Name, call.Given,
                call.Value, call.Parts),
            EditorActionMetaCommand meta => form.PatchMeta(meta.Field, meta.Value, meta.Unit),
            EditorActionGlobalCommand global => form.PatchGlobal(global.Field, global.Value,
                global.Parts),
            EditorActionBranchCommand => form.AddBranch(),
            EditorActionWindowCommand window => form.PatchWindow(window.Name, window.Choice,
                window.Value),
            EditorActionWindowRuleCommand window => form.ApplyWindowRule(window.Name, window.Rule),
            EditorActionPadKeyCommand pad => form.ApplyPadKey(pad.Name, pad.Key, pad.Index,
                pad.Mode),
            _ => throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField")),
        };
    }

    private Task<object?> ActionKeyPad(JsonElement parameters)
    {
        try
        {
            var mode = OptionalString(parameters, "mode") ?? "key";
            return Task.FromResult<object?>(Required().ActionForm.KeyPad(mode));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private Task<object?> HotstringKeyPad(JsonElement parameters)
        => Task.FromResult<object?>(EditorCharacterPad.Build());

    private Task<object?> ActionWindowPreview(JsonElement parameters)
    {
        var name = RequiredString(parameters, "name");
        var rule = OptionalString(parameters, "rule") ?? string.Empty;
        try
        {
            return Task.FromResult<object?>(Required().ActionForm.PreviewWindowRule(name, rule));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private static string RequiredString(JsonElement parameters, string name)
        => OptionalString(parameters, name) is { Length: > 0 } value
            ? value
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Format("Bridge.MissingParameter", name));
}
