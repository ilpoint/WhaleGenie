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

/// <summary>Opening sessions, tabs and declarations: what a macro is, apart from its steps.</summary>

internal sealed partial class EditorSessionBridge
{
    private Task<object?> SessionAction(JsonElement parameters)
    {
        EditorSessionCommand command;
        try
        {
            command = parameters.Deserialize<EditorSessionCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsMacro"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsMacro"));
        }

        try
        {
            return Task.FromResult<object?>(ApplySession(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplySession(EditorSessionCommand command)
    {
        switch (command)
        {
            case EditorSessionOpenCommand open:
                _documents.Active?.CloseValueEditor();
                _documents.Active?.CancelActionDraft();
                try
                {
                    _documents.Open(open.Name);
                }
                catch (InvalidOperationException)
                {
                    throw new BridgeFailureException(BridgeErrors.NoSuchMacro,
                        Strings.Format("Bridge.NoSuchMacro", open.Name ?? string.Empty));
                }

                return Snapshot();
            case EditorSessionNewCommand:
                _documents.Active?.CloseValueEditor();
                _documents.Active?.CancelActionDraft();
                _documents.New();
                return Snapshot();
            case EditorSessionSubOpenCommand sub:
                var subName = SessionText(sub.Name, "Bridge.EditorNeedsMacro");
                var parentKey = _documents.ActiveKey;
                if (parentKey is null)
                {
                    throw new BridgeFailureException(BridgeErrors.BadArgument,
                        Strings.Get("Bridge.EditorNeedsMacro"));
                }

                _documents.Active?.CloseValueEditor();
                _documents.Active?.CancelActionDraft();
                _documents.OpenSubmacro(parentKey, subName);
                return Snapshot();
            case EditorSessionMacroSettingCommand setting:
                Required().SetMacroSetting(
                    SessionText(setting.Field, "Bridge.EditorNeedsField"), setting.Value);
                return Snapshot();
            case EditorSessionActivateCommand activate:
                var activeKey = SessionText(activate.Key, "Bridge.EditorNeedsDocument");
                _documents.Active?.CloseValueEditor();
                _documents.Active?.CancelActionDraft();
                _documents.Activate(activeKey);
                return Snapshot();
            case EditorSessionPatchCommand patch:
                Required().Patch(patch.Name, patch.Description, patch.Group);
                return Snapshot();
            case EditorSessionUndoCommand:
                Required().Undo();
                return Snapshot();
            case EditorSessionRedoCommand:
                Required().Redo();
                return Snapshot();
            case EditorSessionSaveCommand:
                var session = Required();
                try
                {
                    if (!session.Save())
                    {
                        throw new BridgeFailureException(BridgeErrors.Internal,
                            Strings.Get("Bridge.EditorSaveFailed"));
                    }
                }
                catch (InvalidOperationException error)
                {
                    throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
                }

                return Snapshot();
            case EditorSessionCloseCommand close:
                _documents.Active?.CloseValueEditor();
                _documents.Active?.CancelActionDraft();
                if (close.Key is { Length: > 0 } closeKey)
                {
                    _documents.Close(closeKey);
                }
                else
                {
                    _documents.CloseActive();
                }

                return Snapshot();
            case EditorSessionPublicMacroAddCommand add:
                return Required().AddPublicMacro(SessionText(add.Id, "Bridge.EditorNeedsStep"));
            case EditorSessionExportCommand export:
                return Required().Export(SessionText(export.Path, "Bridge.EditorNeedsFile"));
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsMacro"));
        }
    }

    private static string SessionText(string? value, string key)
        => value is { Length: > 0 }
            ? value
            : throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get(key));
    private object Tabs() => new
    {
        activeKey = _documents.ActiveKey,
        tabs = _documents.Tabs,
    };




    private Task<object?> DeclarationAction(JsonElement parameters)
    {
        EditorDeclarationCommand command;
        try
        {
            command = parameters.Deserialize<EditorDeclarationCommand>(CommandOptions)
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
            return Task.FromResult<object?>(ApplyDeclaration(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplyDeclaration(EditorDeclarationCommand command)
    {
        var session = Required();
        var index = command.Index ?? -1;
        var fieldIndex = command.FieldIndex ?? -1;
        var offset = command.Offset ?? 0;
        var value = command.Value ?? string.Empty;
        switch (command.Family, command.Kind)
        {
            case ("parameter", "add"):
                session.AddCallableParameter();
                break;
            case ("parameter", "patch"):
                session.PatchCallableParameter(index, DeclarationField(command), value);
                break;
            case ("parameter", "remove"):
                session.RemoveCallableParameter(index);
                break;
            case ("parameter", "move"):
                session.MoveCallableParameter(index, offset);
                break;
            case ("parameter", "fieldAdd"):
                session.AddCallableParameterField(index);
                break;
            case ("parameter", "fieldPatch"):
                session.PatchCallableParameterField(index, fieldIndex, DeclarationField(command),
                    value);
                break;
            case ("parameter", "fieldRemove"):
                session.RemoveCallableParameterField(index, fieldIndex);
                break;
            case ("result", "add"):
                session.AddCallableResult();
                break;
            case ("result", "patch"):
                session.PatchCallableResult(index, DeclarationField(command), value);
                break;
            case ("result", "remove"):
                session.RemoveCallableResult(index);
                break;
            case ("result", "move"):
                session.MoveCallableResult(index, offset);
                break;
            case ("result", "fieldAdd"):
                session.AddCallableResultField(index);
                break;
            case ("result", "fieldPatch"):
                session.PatchCallableResultField(index, fieldIndex, DeclarationField(command),
                    value);
                break;
            case ("result", "fieldRemove"):
                session.RemoveCallableResultField(index, fieldIndex);
                break;
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField"));
        }

        return Snapshot();
    }

    private static string DeclarationField(EditorDeclarationCommand command)
        => command.Field is { Length: > 0 } field
            ? field
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));






}
