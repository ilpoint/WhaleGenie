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

/// <summary>Triggers and global variables: how a macro is called and what it reads.</summary>

internal sealed partial class EditorSessionBridge
{
    private Task<object?> TriggerAction(JsonElement parameters)
    {
        EditorTriggerCommand command;
        try
        {
            command = parameters.Deserialize<EditorTriggerCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsTrigger"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        try
        {
            return Task.FromResult<object?>(TriggerCommand(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? TriggerCommand(EditorTriggerCommand command)
    {
        var session = Required();
        switch (command)
        {
            case EditorTriggerAddCommand add:
                session.AddTrigger(add.TriggerKind.Length > 0
                    ? add.TriggerKind
                    : nameof(TriggerKind.Key));
                break;
            case EditorTriggerRemoveCommand remove:
                session.RemoveTrigger(Trigger(remove.TriggerId));
                break;
            case EditorTriggerBindingCommand binding:
                session.SetTriggerBinding(Trigger(binding.TriggerId), binding.Binding);
                break;
            case EditorTriggerLoopCommand loop:
                session.SetTriggerLoop(Trigger(loop.TriggerId), loop.Loop);
                break;
            case EditorTriggerOnceCommand once:
                session.SetTriggerOnce(Trigger(once.TriggerId), once.Once);
                break;
            case EditorTriggerEnabledCommand enabled:
                session.SetTriggerEnabled(Trigger(enabled.TriggerId), enabled.Enabled);
                break;
            case EditorTriggerFieldCommand field:
                SetTriggerField(session, field);
                break;
            case EditorTriggerHotstringCommand hotstring:
                session.SetTriggerHotstring(Trigger(hotstring.TriggerId), hotstring.Value);
                break;
            case EditorTriggerHotstringEndKeyCommand endKey:
                session.SetTriggerHotstringEndKey(Trigger(endKey.TriggerId), endKey.Value,
                    endKey.Checked);
                break;
            case EditorTriggerHotstringEndResetCommand endReset:
                session.ResetTriggerHotstringEndKeys(Trigger(endReset.TriggerId));
                break;
            case EditorTriggerHotstringEndCaptureCommand endCapture:
                session.CaptureTriggerHotstringEnd(Trigger(endCapture.TriggerId),
                    endCapture.Character);
                break;
            case EditorTriggerInterfaceHotkeyCommand hotkey:
                session.SetTriggerInterfaceHotkey(Trigger(hotkey.TriggerId), hotkey.Binding);
                break;
            case EditorTriggerInterfaceHotkeyClearCommand clearHotkey:
                session.ClearTriggerInterfaceHotkey(Trigger(clearHotkey.TriggerId));
                break;
            case EditorTriggerIdleCommand idle:
                session.SetTriggerIdle(Trigger(idle.TriggerId), idle.Value);
                break;
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        return Snapshot();
    }

    private static void SetTriggerField(HeadlessEditorSession session,
        EditorTriggerFieldCommand field)
    {
        var id = Trigger(field.TriggerId);
        if (field.Field.Length == 0)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));
        }

        switch (field.Family)
        {
            case "timer":
                session.SetTriggerTimer(id, field.Field, field.Value);
                break;
            case "file":
                session.SetTriggerFile(id, field.Field, field.Value);
                break;
            case "process":
                session.SetTriggerProcess(id, field.Field, field.Value);
                break;
            case "hotstringOption":
                session.SetTriggerHotstringOption(id, field.Field, field.Value);
                break;
            case "color":
                session.SetTriggerColor(id, field.Field, field.Value);
                break;
            case "window":
                session.SetTriggerWindow(id, field.Field, field.Value);
                break;
            case "interface":
                session.SetTriggerInterface(id, field.Field, field.Value);
                break;
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsTrigger"));
        }
    }

    private static string Trigger(string triggerId)
        => triggerId is { Length: > 0 }
            ? triggerId
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsTrigger"));

    private Task<object?> VariableAction(JsonElement parameters)
    {
        EditorVariableCommand command;
        try
        {
            command = parameters.Deserialize<EditorVariableCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsVariable"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsVariable"));
        }

        try
        {
            return Task.FromResult<object?>(ApplyVariable(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplyVariable(EditorVariableCommand command)
    {
        var session = Required();
        switch (command.Family, command.Kind)
        {
            case ("local", "add"):
                session.AddVariable();
                break;
            case ("local", "patch"):
                session.PatchVariable(VariableName(command.OriginalName),
                    VariableField(command.Field), command.Value ?? string.Empty, command.Parts);
                break;
            case ("local", "remove"):
                session.RemoveVariable(VariableName(command.Name));
                break;
            case ("global", "add"):
                session.AddGlobal();
                break;
            case ("global", "patch"):
                session.PatchGlobal(VariableName(command.OriginalName),
                    VariableField(command.Field), command.Value ?? string.Empty, command.Parts);
                break;
            case ("global", "remove"):
                session.RemoveGlobal(VariableName(command.Name));
                break;
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsVariable"));
        }

        return Snapshot();
    }

    private static string VariableName(string? name)
        => name is { Length: > 0 }
            ? name
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsVariable"));

    private static string VariableField(string? field)
        => field is { Length: > 0 }
            ? field
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsVariable"));




}
