using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WindowRuleModel = WhaleGenie.Core.Devices.WindowRule;

namespace WhaleGenie.ViewModels;

// The editor's commands: keys, recording, saving, and the macro it builds on the way out.
public partial class MacroEditorViewModel
{
    [RelayCommand]
    private void SetKey()
    {
        // The button is disabled while recording; this guards the keyboard path too.
        if (IsRecording)
        {
            return;
        }

        // The trigger's capture takes mouse and pad buttons as well as keys.
        StopCapturingKey();
        IsCapturingKey = true;
    }

    [RelayCommand]
    private void ToggleRecord()
    {
        if (IsRecording)
        {
            StopRecording();
            return;
        }

        // A pending bind-key capture would swallow the shortcut and the first recorded keystroke.
        StopCapturingKey();
        IsRecording = true;
    }

    /// <summary>Ends an active recording without closing the editor.</summary>
    public void StopRecording() => IsRecording = false;

    /// <summary>
    /// Tells the window to put the input hook up or take it down, so the recording is tied to
    /// the button and the shortcut rather than to either of them alone.
    /// </summary>
    partial void OnIsRecordingChanged(bool value)
    {
        RecordedEvents = 0;
        RecordingChanged?.Invoke(value);
    }

    [RelayCommand]
    private void Save()
    {
        if (!CanSave)
        {
            SaveBlocked?.Invoke();
            return;
        }

        StopRecording();
        CloseRequested?.Invoke(BuildMacro());
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Highlighting rows in the list, and starting or stopping a recording, are not edits
        // of the macro itself, and neither is what the design-time check made of it.
        if (!_loading && e.PropertyName is not (nameof(IsDirty) or nameof(HasSelection)
                or nameof(SelectedSteps) or nameof(IsRecording) or nameof(RecordedEvents)
                or nameof(Problems) or nameof(HasProblems) or nameof(HasStepProblems)
                or nameof(DeclarationProblems) or nameof(HasDeclarationProblems)
                or nameof(SelectedTrigger) or nameof(HasSelectedTrigger)
                or nameof(ShowsTriggerSettings) or nameof(ShowsLoopMode) or nameof(ShowsTriggerOnce)
                or nameof(TriggerEntries) or nameof(TriggerKindOptions) or nameof(TriggerKindChoices)
                or nameof(TriggerProblemSummary) or nameof(HasTriggerProblem)
                or nameof(HotstringEndKeys) or nameof(IsCapturingHotstringEnd)
                or nameof(HotstringEndCaptureStatus) or nameof(HotstringEndCaptureButton)
                or nameof(IsCapturingInterfaceHotkey) or nameof(InterfaceToggleHotkey)
                or nameof(InterfaceHotkeyProblem)
                or nameof(InterfaceHotkeyButton) or nameof(InterfaceToggleHotkeyDisplay)
                or nameof(SettingsPage) or nameof(IsGeneralSettingsPage)
                or nameof(IsSpecificSettingsPage) or nameof(IsCapturingToggleKey)
                or nameof(ToggleKeyDisplay) or nameof(ToggleKeyButton) or nameof(ToggleKeyProblem)
                or nameof(HasBlockingDeclarationProblems) or nameof(CanSave)
                or nameof(CallImpactSummary) or nameof(HasCallImpact)
                or nameof(ProblemSummary) or nameof(DeclarationProblemSummary)
                or nameof(NameProblem) or nameof(HasNameProblem)))
        {
            MarkDirty();
        }

        // A window trigger lists the windows its rule recognises right now, so a rule written a
        // little too wide is seen while it is being written. Only while the trigger is a window
        // trigger: a rule nothing reads has no windows to list, and a list of them would say the
        // macro does something it does not.
        if (e.PropertyName is nameof(WindowTarget) or nameof(TriggerMode))
        {
            var asking = IsWindowRuleTrigger;
            WindowPreview.Show(asking);
            if (asking)
            {
                WindowPreview.Follow(WindowRuleNow);
            }
        }
    }

    /// <summary>
    /// Raised for every change to the macro being written, where <see cref="IsDirty"/> only says
    /// whether there is any unsaved work at all: the snapshot has to hear about the changes that
    /// arrive while the macro is already unsaved, and the dirty flag does not move for those.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Notes that the macro being written has moved on.</summary>
    private void MarkDirty()
    {
        IsDirty = true;
        RefreshVariableCenter();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void RefreshVariableCenter()
        => VariableCenter.Refresh(CollectVariables());

    /// <summary>Refreshes what global declaration changes mean to this macro without marking it edited.</summary>
    public void RefreshSharedVariables()
    {
        RefreshVariableCenter();
        RefreshProblems();
    }

    /// <summary>
    /// Renaming a macro changes which calls point at it, so the check has to be run again —
    /// including when the new name is the one a self-call was waiting for.
    /// </summary>
    partial void OnNameChanged(string value)
    {
        UpdateInterfaceGroupFields();
        if (!_loading)
        {
            NotifyStepsChanged();
        }
    }

    /// <summary>
    /// Reads the editor into a macro. Internal rather than private so the window can keep the
    /// work in the recovery snapshot while it is still being written.
    /// </summary>
    internal MacroItem BuildMacro()
    {
        CommitTriggerFields();
        var macro = new MacroItem
        {
            Name = Name.Trim(),
            Description = Description.Trim(),
            Group = Group.Trim(),
            Origin = Origin,
            PositionCapture = PositionCapture,
            RecordMouseMovement = RecordMouseMovement,
            RecordMouseButtons = RecordMouseButtons,
            RecordKeyboardKeys = RecordKeyboardKeys,
            RecordGamepadButtons = RecordGamepadButtons,
            RecordDelays = RecordDelays,
            IgnoreDelaysBelowMs = (int)(IgnoreDelaysBelowMs ?? 0),
            DelayScale = DelayScale,
            InputRate = FollowProgramInputRate || !double.IsFinite(InputLimitValue)
                ? null
                : Math.Clamp((int)Math.Round(InputLimitValue), 0, InputLimit.Most),
            ToggleKey = ToggleKey.Trim(),
        };
        RememberInterfaceHotkey();
        macro.InterfaceLayouts.AddRange(_interfaceLayouts.Values.Select(layout => layout with { }));

        if (SelectedTrigger is { } current)
        {
            // Keep the legacy projections in step for the macro card and old entry guards while
            // the project-wide trigger collection is becoming the only saved source.
            macro.TriggerMode = current.Kind switch
            {
                TriggerKind.Color => MacroTrigger.ColorPixelChanges,
                TriggerKind.Timer => MacroTrigger.Timer,
                TriggerKind.File => MacroTrigger.FileChanges,
                TriggerKind.Process => MacroTrigger.Process,
                TriggerKind.Window or TriggerKind.Interface => MacroTrigger.Window,
                TriggerKind.Idle => MacroTrigger.Idle,
                _ => MacroTrigger.KeystrokesButtonInputs,
            };
            macro.LoopMode = current.LoopMode;
            macro.BindKey = current.BindKey;
            macro.ScheduleMode = current.ScheduleMode;
            macro.ScheduleInterval = current.ScheduleInterval;
            macro.ScheduleUnit = current.ScheduleUnit;
            macro.ScheduleTime = current.ScheduleTime;
            macro.WatchPath = current.WatchPath;
            macro.WatchChange = current.WatchChange;
            macro.WatchFilter = current.WatchFilter;
            macro.WatchSubfolders = current.WatchSubfolders;
            macro.ProcessName = current.ProcessName;
            macro.ProcessChange = current.ProcessChange;
            macro.WindowTarget = current.WindowTarget;
            macro.WindowChange = current.WindowChange;
            macro.IdleSeconds = current.IdleSeconds;
            macro.ColorMatch = current.ColorMatch;
            macro.ColorPositionX = current.ColorPositionX;
            macro.ColorPositionY = current.ColorPositionY;
            macro.UseCursorPosition = current.UseCursorPosition;
            macro.TriggerOnce = current.TriggerOnce;
            macro.HexColor = current.HexColor;
            macro.ColorTolerance = current.ColorTolerance;
        }

        foreach (var entry in TriggerEntries)
        {
            entry.MacroName = macro.Name;
            macro.Triggers.Add(entry.Copy());
        }

        foreach (var step in Steps)
        {
            macro.Steps.Add(step);
        }

        macro.Parameters.AddRange(DeclaredParameters);
        macro.Results.AddRange(DeclaredResults);
        macro.Locals.AddRange(DeclaredLocals);
        macro.SubMacros.AddRange(SubMacros);

        return macro;
    }

    /// <summary>Reads a numeric parameter typed into a plain input box, falling back to zero.</summary>
    private static int ParseCoordinate(string? value)
        => int.TryParse(value?.Trim(), out var result) ? result : 0;
}
