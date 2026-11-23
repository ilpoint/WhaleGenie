using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Recording;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;
using WhaleGenie.ViewModels;

namespace WhaleGenie.Editing;

/// <summary>The settings drawer of a trigger: the fields each kind of entry writes.</summary>

internal sealed partial class HeadlessEditorSession
{
    public void SetTriggerInterface(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not InterfaceSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not an interface trigger");
        }

        _model.SelectedTrigger = trigger;
        _model.WindowTarget = field switch
        {
            "title" => _model.WindowTarget with { Title = value },
            "process" => _model.WindowTarget with { Process = value },
            "class" => _model.WindowTarget with { ClassName = value },
            "how" => _model.WindowTarget with { How = value },
            _ => throw new InvalidOperationException("unknown interface field " + field),
        };
        _model.IsDirty = true;
    }

    public void SetTriggerWindow(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not WindowSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a window trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "title":
                _model.WindowTarget = _model.WindowTarget with { Title = value };
                break;
            case "process":
                _model.WindowTarget = _model.WindowTarget with { Process = value };
                break;
            case "class":
                _model.WindowTarget = _model.WindowTarget with { ClassName = value };
                break;
            case "how":
                _model.WindowTarget = _model.WindowTarget with { How = value };
                break;
            case "change":
                _model.WindowChange = Enum.Parse<WindowChangeKind>(value, ignoreCase: true);
                break;
            default:
                throw new InvalidOperationException("unknown window field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerColor(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not ColorSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a colour trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "match":
                _model.ColorMatch = Enum.Parse<ColorMatchCondition>(value, ignoreCase: true);
                break;
            case "hex":
                _model.HexColor = value.Trim();
                break;
            case "tolerance":
                _model.ColorTolerance = value.Trim();
                break;
            case "x":
                _model.ColorPositionX = value.Trim();
                break;
            case "y":
                _model.ColorPositionY = value.Trim();
                break;
            case "follow":
                _model.UseCursorPosition = bool.TryParse(value, out var follow) && follow;
                break;
            default:
                throw new InvalidOperationException("unknown colour field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerIdle(string triggerId, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not IdleSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not an idle trigger");
        }

        _model.SelectedTrigger = trigger;
        _model.IdleSeconds = int.TryParse(value, out var seconds) ? Math.Max(1, seconds) : 60;
        _model.IsDirty = true;
    }

    public void SetTriggerProcess(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not ProcessSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a process trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "name":
                _model.ProcessName = value.Trim();
                break;
            case "change":
                _model.ProcessChange = Enum.Parse<ProcessChangeKind>(value, ignoreCase: true);
                break;
            default:
                throw new InvalidOperationException("unknown process field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerHotstring(string triggerId, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not HotstringSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a hotstring trigger");
        }

        _model.SelectedTrigger = trigger;
        _model.HotstringText = value.Length > HotstringCharacters.MaximumLength
            ? value[..HotstringCharacters.MaximumLength]
            : value;
        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

    public void SetTriggerHotstringEndKey(string triggerId, string value, bool checkedState)
    {
        var trigger = AnyTrigger(triggerId);
        _model.SelectedTrigger = trigger;
        var option = _model.HotstringEndKeys.FirstOrDefault(candidate =>
            string.Equals(candidate.Value, value, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the hotstring has no end key " + value);
        option.IsChecked = checkedState;
        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

    public void ResetTriggerHotstringEndKeys(string triggerId)
    {
        var trigger = AnyTrigger(triggerId);
        _model.SelectedTrigger = trigger;
        _model.HotstringEndChars = HotstringCharacters.DefaultEndChars;
        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

    public void CaptureTriggerHotstringEnd(string triggerId, string character)
    {
        var trigger = AnyTrigger(triggerId);
        _model.SelectedTrigger = trigger;
        _model.IsCapturingHotstringEnd = true;
        _model.CaptureHotstringEnd(character);
        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

    public void SetTriggerInterfaceHotkey(string triggerId, string binding)
    {
        var trigger = AnyTrigger(triggerId);
        if (trigger.Spec is not InterfaceSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not an interface trigger");
        }

        _model.SelectedTrigger = trigger;
        if (_model.SetInterfaceToggleHotkey(binding))
        {
            _model.IsDirty = true;
        }
    }

    public void ClearTriggerInterfaceHotkey(string triggerId)
    {
        var trigger = AnyTrigger(triggerId);
        _model.SelectedTrigger = trigger;
        _model.ClearInterfaceToggleHotkey();
        _model.IsDirty = true;
    }

    public void SetTriggerFile(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not FileSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a file trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "path":
                _model.WatchPath = value;
                break;
            case "change":
                _model.WatchChange = Enum.Parse<FileChangeKind>(value, ignoreCase: true);
                break;
            case "filter":
                _model.WatchFilter = value;
                break;
            case "subfolders":
                _model.WatchSubfolders = bool.TryParse(value, out var subfolders) && subfolders;
                break;
            default:
                throw new InvalidOperationException("unknown file field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerTimer(string triggerId, string field, string value)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not TimerSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a timer trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "mode":
                _model.ScheduleMode = Enum.Parse<ScheduleMode>(value, ignoreCase: true);
                break;
            case "time":
                _model.ScheduleTime = value.Trim();
                break;
            case "interval":
                _model.ScheduleInterval = int.TryParse(value, out var interval) ? interval : 1;
                break;
            case "unit":
                _model.ScheduleUnit = Enum.Parse<ScheduleUnit>(value, ignoreCase: true);
                break;
            default:
                throw new InvalidOperationException("unknown timer field " + field);
        }

        _model.IsDirty = true;
    }

    public void SetTriggerHotstringOption(string triggerId, string field, string value)
    {
        var trigger = AnyTrigger(triggerId);
        if (trigger.Spec is not HotstringSpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a hotstring trigger");
        }

        _model.SelectedTrigger = trigger;
        switch (field)
        {
            case "endChars":
                _model.HotstringEndChars = value;
                break;
            case "caseSensitive":
                _model.HotstringCaseSensitive = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case "insideWord":
                _model.HotstringInsideWord = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case "requireEnd":
                _model.HotstringRequireEnd = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case "eraseTyped":
                _model.HotstringEraseTyped = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case "keepEnd":
                _model.HotstringKeepEnd = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                break;
            default:
                throw new InvalidOperationException("unknown hotstring option " + field);
        }

        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

}
