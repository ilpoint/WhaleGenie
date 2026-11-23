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

/// <summary>Globals, variables and triggers: what a macro reads and how it is called.</summary>

internal sealed partial class HeadlessEditorSession
{
    public void AddGlobal()
    {
        var draft = EnsureGlobalDraft();
        var name = "variable";
        for (var index = 2; draft.Any(entry => string.Equals(entry.Variable.Name, name,
                StringComparison.OrdinalIgnoreCase)); index++)
        {
            name = "variable" + index.ToString(CultureInfo.InvariantCulture);
        }

        draft.Add(new GlobalDraftEntry(new VariableDefinition
        {
            Name = name,
            Scope = VariableScope.Global,
        }, name, true));
        _model.IsDirty = true;
    }

    public void PatchGlobal(string originalName, string field, string value)
    {
        var draft = EnsureGlobalDraft();
        var entry = draft.FirstOrDefault(candidate => string.Equals(
            candidate.Variable.Name, originalName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the project has no global " + originalName);
        var next = value.Trim();
        switch (field)
        {
            case "name":
                if (draft.Any(candidate => !ReferenceEquals(candidate, entry)
                    && string.Equals(candidate.Variable.Name, next, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidOperationException("the project already has a global " + next);
                }

                entry.Variable.Name = next;
                break;
            case "type":
                entry.Variable.Type = WhaleGenie.Models.VariableTypes.KindOf(value);
                break;
            case "default":
                entry.Variable.Default = Formula.Text(value);
                break;
            case "description":
                entry.Variable.Description = value;
                break;
            case "persist":
                entry.Variable.PersistValue = bool.TryParse(value, out var persist) && persist;
                break;
            default:
                throw new InvalidOperationException("unknown global field " + field);
        }

        _model.IsDirty = true;
    }

    public void RemoveGlobal(string name)
    {
        var draft = EnsureGlobalDraft();
        var entry = draft.FirstOrDefault(candidate => string.Equals(
            candidate.Variable.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the project has no global " + name);
        draft.Remove(entry);
        _model.IsDirty = true;
    }

    private List<GlobalDraftEntry> EnsureGlobalDraft()
        => _globalDraft ??= [.. _workspace.Variables.Globals.Select(global =>
            new GlobalDraftEntry(CloneGlobal(global), global.Name, false))];

    private static VariableDefinition CloneGlobal(VariableDefinition global)
        => new()
        {
            Name = global.Name,
            Scope = VariableScope.Global,
            Type = global.Type,
            Description = global.Description,
            Default = global.Default,
            PersistValue = global.PersistValue,
        };

    private void CommitGlobals()
    {
        if (_globalDraft is null)
        {
            return;
        }

        var draftNames = _globalDraft.Select(entry => entry.OriginalName)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var global in _workspace.Variables.Globals.ToList())
        {
            if (!draftNames.Contains(global.Name))
            {
                _workspace.Persisted.Forget(global.Name);
                _workspace.Variables.Values.Remove(global.Name);
            }
        }

        foreach (var entry in _globalDraft)
        {
            if (!entry.IsNew
                && !string.Equals(entry.OriginalName, entry.Variable.Name, StringComparison.Ordinal))
            {
                VariableRename.RenameGlobal([.. _workspace.Macros], entry.OriginalName,
                    entry.Variable.Name);
                _model.RenameGlobalReferences(entry.OriginalName, entry.Variable.Name);
                _workspace.Persisted.Rename(entry.OriginalName, entry.Variable.Name);
                _workspace.Variables.Values.Rename(entry.OriginalName, entry.Variable.Name);
            }
        }

        _workspace.Variables.ClearGlobals();
        foreach (var entry in _globalDraft)
        {
            _workspace.Variables.Adopt(entry.Variable);
        }

        _workspace.MarkGlobalsChanged();
        _globalDraft = null;
    }

    private sealed record GlobalDraftEntry(VariableDefinition Variable, string OriginalName, bool IsNew);

    public void AddVariable()
    {
        _model.DeclaredLocals.Add(new MacroLocalVariable { Name = string.Empty, Type = ValueKind.Text });
        _model.IsDirty = true;
    }

    public void PatchVariable(string originalName, string field, string value)
    {
        var local = _model.DeclaredLocals.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), originalName.Trim(), StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no local variable " + originalName);
        switch (field)
        {
            case "name":
                if (!_model.RenameLocalVariable(originalName, value, out var problem))
                {
                    throw new InvalidOperationException(problem);
                }

                break;
            case "type":
                local.Type = WhaleGenie.Models.VariableTypes.KindOf(value);
                break;
            case "initial":
                local.Initial = Formula.Text(value);
                break;
            case "description":
                local.Description = value;
                break;
            default:
                throw new InvalidOperationException("unknown variable field " + field);
        }

        _model.IsDirty = true;
    }

    public void RemoveVariable(string name)
    {
        var local = _model.DeclaredLocals.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), name.Trim(), StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no local variable " + name);
        _model.DeclaredLocals.Remove(local);
        _model.IsDirty = true;
    }

    public void AddTrigger(string kind)
    {
        if (!Enum.TryParse<TriggerKind>(kind, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException("unknown trigger kind " + kind);
        }

        _model.AddTriggerCommand.Execute(parsed);
        _model.IsDirty = true;
    }

    public void AddKeyTrigger() => AddTrigger(nameof(TriggerKind.Key));

    public void RemoveTrigger(string triggerId)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal));
        if (trigger is null)
        {
            throw new InvalidOperationException("the macro has no trigger " + triggerId);
        }

        _model.RemoveTriggerCommand.Execute(trigger);
    }

    public void SetTriggerBinding(string triggerId, string binding)
    {
        var trigger = KeyTrigger(triggerId);
        _model.BindKey = binding.Trim();
        _model.IsDirty = true;
    }

    public void SetTriggerLoop(string triggerId, string loop)
    {
        var trigger = AnyTrigger(triggerId);
        if (trigger.Spec is not (KeySpec or ColorSpec))
        {
            throw new InvalidOperationException("trigger " + triggerId + " has no loop mode");
        }

        if (!Enum.TryParse<MacroLoop>(loop, ignoreCase: true, out var parsed))
        {
            throw new InvalidOperationException("unknown trigger loop " + loop);
        }

        _model.SelectedTrigger = trigger;
        _model.LoopMode = parsed;
        _model.CommitTriggerFields();
        _model.IsDirty = true;
    }

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

    public void SetTriggerEnabled(string triggerId, bool enabled)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        trigger.IsEnabled = enabled;
        _model.IsDirty = true;
    }

    public void SetTriggerOnce(string triggerId, bool once)
    {
        var trigger = AnyTrigger(triggerId);
        _model.SelectedTrigger = trigger;
        _model.TriggerOnce = once;
        _model.CommitTriggerFields();
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

    private TriggerEntry AnyTrigger(string triggerId)
        => _model.TriggerEntries.FirstOrDefault(entry =>
               string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
           ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);

    private TriggerEntry KeyTrigger(string triggerId)
    {
        var trigger = _model.TriggerEntries.FirstOrDefault(entry =>
            string.Equals(entry.Id, triggerId, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("the macro has no trigger " + triggerId);
        if (trigger.Spec is not KeySpec)
        {
            throw new InvalidOperationException("trigger " + triggerId + " is not a key trigger");
        }

        // The drawer's fields are what BuildMacro commits; move the selection onto this entry and
        // write the value there rather than changing a spec the next commit would overwrite.
        _model.SelectedTrigger = trigger;
        return trigger;
    }

}
