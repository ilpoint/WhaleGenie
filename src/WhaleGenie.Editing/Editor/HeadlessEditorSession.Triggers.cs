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

/// <summary>Triggers: the entries a macro starts on, added, removed and enabled.</summary>

internal sealed partial class HeadlessEditorSession
{
    public IReadOnlyList<EditorTriggerSnapshot> Triggers
        => [.. _model.TriggerEntries.Select(entry => new EditorTriggerSnapshot(
            entry.Id,
            entry.Kind.ToString(),
            entry.KindLabel,
            entry.Summary,
            entry.IsEnabled,
            entry.Spec is KeySpec key ? key.Binding : string.Empty,
            entry.Spec is KeySpec pressed ? pressed.Loop.ToString()
                : entry.Spec is ColorSpec colourLoop ? colourLoop.Loop.ToString() : string.Empty,
            WhaleGenie.Models.TriggerKinds.RunsOnce(entry.Spec),
            entry.Spec is TimerSpec timer ? timer.Mode.ToString() : string.Empty,
            entry.Spec is TimerSpec timed ? timed.Time : string.Empty,
            entry.Spec is TimerSpec interval
                ? interval.Interval.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is TimerSpec unit ? unit.Unit.ToString() : string.Empty,
            entry.Spec is FileSpec file ? file.Path : string.Empty,
            entry.Spec is FileSpec change ? change.Change.ToString() : string.Empty,
            entry.Spec is FileSpec filter ? filter.Filter : string.Empty,
            entry.Spec is FileSpec folders && folders.Subfolders,
            entry.Spec is ProcessSpec process ? process.Name : string.Empty,
            entry.Spec is ProcessSpec processChange ? processChange.Change.ToString() : string.Empty,
            entry.Spec is HotstringSpec hotstring ? hotstring.Abbreviation : string.Empty,
            entry.Spec is ColorSpec colour ? colour.Match.ToString() : string.Empty,
            entry.Spec is ColorSpec hex ? hex.Hex : string.Empty,
            entry.Spec is ColorSpec tolerance
                ? tolerance.Tolerance.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is ColorSpec point
                ? point.X.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is ColorSpec pointY
                ? pointY.Y.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is ColorSpec follow && follow.FollowCursor,
            entry.Spec is IdleSpec idle
                ? idle.Seconds.ToString(CultureInfo.InvariantCulture)
                : string.Empty,
            entry.Spec is WindowSpec window ? window.Target.Title : string.Empty,
            entry.Spec is WindowSpec processWindow ? processWindow.Target.Process : string.Empty,
            entry.Spec is WindowSpec classWindow ? classWindow.Target.ClassName
                : entry.Spec is InterfaceSpec interfaceClass ? interfaceClass.Window.ClassName
                : string.Empty,
            entry.Spec is WindowSpec howWindow ? howWindow.Target.How.ToString() : string.Empty,
            entry.Spec is WindowSpec changeWindow ? changeWindow.Change.ToString() : string.Empty,
            entry.Spec is InterfaceSpec face ? face.Window.Title : string.Empty,
            entry.Spec is InterfaceSpec faceProcess ? faceProcess.Window.Process : string.Empty,
            entry.Spec is InterfaceSpec faceHow ? faceHow.Window.How.ToString() : string.Empty,
            entry.Spec is WindowSpec caseWindow && caseWindow.Target.CaseSensitive,
            entry.Spec is WindowSpec trimWindow && trimWindow.Target.Trim,
            entry.Spec is WindowSpec multiWindow && multiWindow.Target.Multiline,
            entry.Spec is HotstringSpec ends ? ends.EndChars : string.Empty,
            entry.Spec is HotstringSpec casing && casing.CaseSensitive,
            entry.Spec is HotstringSpec inside && inside.InsideWord,
            entry.Spec is HotstringSpec requiring && requiring.RequireEndChar,
            entry.Spec is HotstringSpec erasing && erasing.EraseTyped,
            entry.Spec is HotstringSpec keeping && keeping.KeepEndChar,
            HotstringCharacters.MaximumLength,
            _model.SelectedTrigger?.Id == entry.Id
                ? [.. _model.HotstringEndKeys.Select(option => new EditorHotstringEndKeySnapshot(
                    option.Value, option.Label, option.IsChecked, option.IsCustom))]
                : [],
            _model.SelectedTrigger?.Id == entry.Id ? _model.HotstringEndCaptureStatus : string.Empty,
            entry.Spec is InterfaceSpec && _model.SelectedTrigger?.Id == entry.Id
                ? _model.InterfaceToggleHotkey
                : string.Empty,
            _model.SelectedTrigger?.Id == entry.Id ? _model.InterfaceHotkeyProblem : string.Empty,
            _model.SelectedTrigger?.Id == entry.Id ? [.. _model.InterfaceGroupMembers] : []))];

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
