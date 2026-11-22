using System;
using System.Text.Json;
using System.Threading.Tasks;
using WhaleGenie.Editing;
using WhaleGenie.Localization;

namespace WhaleGenie.Bridge;

// The action form's commands: one draft field at a time, answered with the whole form again.
internal sealed partial class EditorSessionBridge
{
    private Task<object?> ActionPage(JsonElement parameters)
        => Form(parameters, form => form.SetPage(OptionalString(parameters, "page") ?? "base"));

    private Task<object?> ActionField(JsonElement parameters)
    {
        var name = RequiredString(parameters, "name");
        var field = OptionalString(parameters, "field") ?? "text";
        var value = OptionalString(parameters, "value") ?? string.Empty;
        return Form(parameters, form => form.PatchField(name, field, value));
    }

    private Task<object?> ActionRowAdd(JsonElement parameters)
    {
        var name = RequiredString(parameters, "name");
        return Form(parameters, form => form.AddRow(name));
    }

    private Task<object?> ActionRowRemove(JsonElement parameters)
    {
        var name = RequiredString(parameters, "name");
        var index = OptionalInt(parameters, "index") ?? 0;
        return Form(parameters, form => form.RemoveRow(name, index));
    }

    private Task<object?> ActionRowMove(JsonElement parameters)
    {
        var name = RequiredString(parameters, "name");
        var index = OptionalInt(parameters, "index") ?? 0;
        var by = OptionalInt(parameters, "by") ?? 0;
        return Form(parameters, form => form.MoveRow(name, index, by));
    }

    private Task<object?> ActionRowCell(JsonElement parameters)
    {
        var name = RequiredString(parameters, "name");
        var index = OptionalInt(parameters, "index") ?? 0;
        var column = RequiredString(parameters, "column");
        var value = OptionalString(parameters, "value") ?? string.Empty;
        return Form(parameters, form => form.PatchRow(name, index, column, value));
    }

    private Task<object?> ActionReturns(JsonElement parameters)
    {
        var key = RequiredString(parameters, "key");
        var ticked = OptionalBool(parameters, "ticked");
        var name = OptionalString(parameters, "name");
        return Form(parameters, form => form.PatchReturn(key, ticked, name));
    }

    private Task<object?> ActionReturnsFixName(JsonElement parameters)
    {
        var key = RequiredString(parameters, "key");
        return Form(parameters, form => form.FixReturnName(key));
    }

    private Task<object?> ActionCallArgument(JsonElement parameters)
    {
        var name = RequiredString(parameters, "name");
        var given = OptionalBool(parameters, "given");
        var value = OptionalString(parameters, "value");
        return Form(parameters, form => form.PatchCallArgument(name, given, value));
    }

    private Task<object?> ActionMeta(JsonElement parameters)
    {
        var field = RequiredString(parameters, "field");
        var value = OptionalString(parameters, "value") ?? string.Empty;
        var unit = OptionalString(parameters, "unit") ?? string.Empty;
        return Form(parameters, form => form.PatchMeta(field, value, unit));
    }

    private Task<object?> ActionGlobal(JsonElement parameters)
    {
        var field = RequiredString(parameters, "field");
        var value = OptionalString(parameters, "value") ?? string.Empty;
        return Form(parameters, form => form.PatchGlobal(field, value));
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

    private Task<object?> ActionPadKey(JsonElement parameters)
    {
        var name = RequiredString(parameters, "name");
        var key = RequiredString(parameters, "key");
        var index = OptionalInt(parameters, "index") ?? -1;
        var mode = OptionalString(parameters, "mode") ?? "key";
        return Form(parameters, form => form.ApplyPadKey(name, key, index, mode));
    }

    private Task<object?> ActionBranch(JsonElement parameters)
        => Form(parameters, form => form.AddBranch());

    private Task<object?> ActionWindow(JsonElement parameters)
    {
        var name = RequiredString(parameters, "name");
        var choice = RequiredString(parameters, "choice");
        var value = OptionalString(parameters, "value") ?? string.Empty;
        return Form(parameters, form => form.PatchWindow(name, choice, value));
    }

    private Task<object?> ActionWindowRule(JsonElement parameters)
    {
        var name = RequiredString(parameters, "name");
        var rule = RequiredString(parameters, "rule");
        return Form(parameters, form => form.ApplyWindowRule(name, rule));
    }

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

    private Task<object?> Form(JsonElement parameters, Func<HeadlessActionSession, object?> command)
    {
        try
        {
            return Task.FromResult(command(Required().ActionForm));
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
