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

/// <summary>Steps, conditions and recording: the body of the macro being edited.</summary>

internal sealed partial class EditorSessionBridge
{
    private Task<object?> StepAction(JsonElement parameters)
    {
        EditorStepCommand command;
        try
        {
            command = parameters.Deserialize<EditorStepCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsStep"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        try
        {
            return Task.FromResult<object?>(ApplyStep(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplyStep(EditorStepCommand command)
    {
        var session = Required();
        switch (command.Kind)
        {
            case "add":
                var type = StepText(command.Type, "Bridge.EditorNeedsStep");
                Dictionary<string, string>? presets = command.Parameters is null
                    ? null
                    : new Dictionary<string, string>(command.Parameters, StringComparer.Ordinal);
                try
                {
                    session.AddStep(type, command.ParentId, command.List, presets);
                }
                catch (Exception error) when (error is InvalidOperationException or ArgumentException)
                {
                    throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
                }

                return Snapshot();
            case "meta":
                session.SetStepMeta(StepId(command.StepId),
                    StepText(command.Field, "Bridge.EditorNeedsStep"), command.Value ?? string.Empty);
                return Snapshot();
            case "parameter":
                session.PatchParameter(StepId(command.StepId), StepName(command.Name),
                    command.Value ?? string.Empty);
                return Snapshot();
            case "rowAdd":
                session.AddParameterRow(StepId(command.StepId), StepName(command.Name));
                return Snapshot();
            case "rowRemove":
                session.RemoveParameterRow(StepId(command.StepId), StepName(command.Name),
                    StepIndex(command.Index));
                return Snapshot();
            case "rowCell":
                session.PatchParameterRow(StepId(command.StepId), StepName(command.Name),
                    StepIndex(command.Index), StepText(command.Column, "Bridge.EditorNeedsRow"),
                    command.Value ?? string.Empty);
                return Snapshot();
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsStep"));
        }
    }

    private static string StepId(string? value)
        => StepText(value, "Bridge.EditorNeedsStep");

    private static string StepName(string? value)
        => StepText(value, "Bridge.EditorNeedsField");

    private static int StepIndex(int? value)
        => value is >= 0
            ? value.Value
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsRow"));

    private static string StepText(string? value, string key)
        => value is { Length: > 0 }
            ? value
            : throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get(key));

    private Task<object?> StepsAction(JsonElement parameters)
    {
        EditorStepsCommand command;
        try
        {
            command = parameters.Deserialize<EditorStepsCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsStep"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        try
        {
            return Task.FromResult<object?>(ApplySteps(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplySteps(EditorStepsCommand command)
    {
        var session = Required();
        var steps = command.Steps ?? [];
        if (command.Kind != "paste" && steps.Length == 0)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsStep"));
        }

        switch (command.Kind)
        {
            case "enabled":
                session.SetStepsEnabled(steps, command.Enabled ?? false);
                return Snapshot();
            case "remove":
                session.RemoveSteps(steps);
                return Snapshot();
            case "duplicate":
                session.DuplicateSteps(steps);
                return Snapshot();
            case "group":
                session.GroupSteps(steps);
                return Snapshot();
            case "move":
                session.MoveStepsTo(steps,
                    StepsText(command.Target, "Bridge.EditorNeedsStep"), command.Before ?? false);
                return Snapshot();
            case "moveBy":
                session.MoveStepsBy(steps, command.Delta ?? 0);
                return Snapshot();
            case "lift":
                session.LiftSteps(steps);
                return Snapshot();
            case "copy":
                return session.CopySteps(steps);
            case "cut":
                return session.CutSteps(steps);
            case "paste":
                session.PasteSteps(command.Text, command.After);
                return Snapshot();
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsStep"));
        }
    }

    private static string StepsText(string? value, string key)
        => value is { Length: > 0 }
            ? value
            : throw new BridgeFailureException(BridgeErrors.BadArgument, Strings.Get(key));

    private Task<object?> ConditionAction(JsonElement parameters)
    {
        EditorConditionCommand command;
        try
        {
            command = parameters.Deserialize<EditorConditionCommand>(CommandOptions)
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
            var session = Required();
            var step = command.StepId;
            var name = command.Name;
            if (step.Length == 0 || name.Length == 0)
            {
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Bridge.EditorNeedsField"));
            }

            var path = command.Path ?? (command.Index is { } index ? [index] : []);
            switch (command.Kind)
            {
                case "rowAdd":
                    session.AddConditionRow(step, name, path);
                    break;
                case "groupAdd":
                    session.AddConditionGroup(step, name, path);
                    break;
                case "rowRemove":
                    session.RemoveConditionNode(step, name, path);
                    break;
                case "row":
                    session.SetConditionRow(step, name, path, ConditionText(command.Field),
                        command.Value ?? string.Empty);
                    break;
                case "join":
                    session.SetConditionJoin(step, name, ConditionText(command.Join), path);
                    break;
                default:
                    throw new BridgeFailureException(BridgeErrors.BadArgument,
                        Strings.Get("Bridge.EditorNeedsField"));
            }

            return Task.FromResult<object?>(Snapshot());
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private static string ConditionText(string? value)
        => value is { Length: > 0 }
            ? value
            : throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Bridge.EditorNeedsField"));


    private Task<object?> RecordAction(JsonElement parameters)
    {
        EditorRecordCommand command;
        try
        {
            command = parameters.Deserialize<EditorRecordCommand>(CommandOptions)
                ?? throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Editor.RecordFailed"));
        }
        catch (JsonException)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument,
                Strings.Get("Editor.RecordFailed"));
        }

        try
        {
            return Task.FromResult<object?>(ApplyRecord(command));
        }
        catch (InvalidOperationException error)
        {
            throw new BridgeFailureException(BridgeErrors.BadArgument, error.Message);
        }
    }

    private object? ApplyRecord(EditorRecordCommand command)
    {
        switch (command.Kind)
        {
            case "start":
                return StartRecording();
            case "stop":
                return StopRecording();
            case "state":
                return RecordingState();
            case "option":
                if (command.Field is not { Length: > 0 } || command.Value is null)
                {
                    throw new BridgeFailureException(BridgeErrors.BadArgument,
                        Strings.Get("Editor.RecordFailed"));
                }

                Required().SetRecordOption(command.Field, command.Value);
                return Snapshot();
            case "delayScale":
                Required().SetDelayScale(command.Factor ?? throw new BridgeFailureException(
                    BridgeErrors.BadArgument, Strings.Get("Editor.Problem.CannotSave")));
                return Snapshot();
            default:
                throw new BridgeFailureException(BridgeErrors.BadArgument,
                    Strings.Get("Editor.RecordFailed"));
        }
    }

    private object StartRecording()
    {
        if (_devices is null)
        {
            throw new BridgeFailureException(BridgeErrors.Internal,
                Strings.Get("Editor.RecordFailed"));
        }

        _recording ??= new RecordingSession(_settings);
        _stopRecordingRequested = false;
        var model = Required();
        if (!_recording.Start(_devices, model.RecordIgnoreInjected, model.RecordGamepadButtons,
            () => _stopRecordingRequested = true))
        {
            _recording = null;
            throw new BridgeFailureException(BridgeErrors.Internal,
                Strings.Get("Editor.RecordFailed"));
        }

        return RecordingState();
    }

    private object StopRecording()
    {
        var outcome = _recording?.Stop(Required().RecordingChoices()) ?? new RecordingSession.Outcome([], 0);
        _recording = null;
        if (outcome.Steps.Count > 0)
        {
            Required().AppendSteps(outcome.Steps);
        }

        return new
        {
            captured = outcome.Captured,
            steps = outcome.Steps.Count,
            snapshot = Snapshot(),
        };
    }


    private object RecordingState()
    {
        if (_stopRecordingRequested)
        {
            _stopRecordingRequested = false;
            return StopRecording();
        }

        return new
        {
            recording = _recording?.IsRecording == true,
            captured = _recording?.Captured ?? 0,
        };
    }

}
