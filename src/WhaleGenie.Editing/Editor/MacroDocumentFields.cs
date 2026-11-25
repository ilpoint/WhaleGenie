using System;
using System.Collections.Generic;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Which properties of <see cref="MacroEditorViewModel"/> are the macro itself, and which are only
/// what the editor is doing with it. A property that carries document state must be registered
/// here; a new property that is not is deliberately view state. <c>MacroDocumentFieldsTests</c>
/// reflects over the public settable properties and fails when a new one is in neither list.
/// </summary>
internal static class MacroDocumentFields
{
    /// <summary>Properties whose change makes the macro different and therefore dirty.</summary>
    public static readonly IReadOnlySet<string> Document = Set(
        nameof(MacroEditorViewModel.Origin),
        nameof(MacroEditorViewModel.Name),
        nameof(MacroEditorViewModel.Description),
        nameof(MacroEditorViewModel.Group),
        nameof(MacroEditorViewModel.FollowProgramInputRate),
        nameof(MacroEditorViewModel.InputLimitValue),
        nameof(MacroEditorViewModel.ToggleKey),
        nameof(MacroEditorViewModel.TriggerMode),
        nameof(MacroEditorViewModel.LoopMode),
        nameof(MacroEditorViewModel.IdleSeconds),
        nameof(MacroEditorViewModel.WindowTarget),
        nameof(MacroEditorViewModel.HotstringText),
        nameof(MacroEditorViewModel.HotstringEndChars),
        nameof(MacroEditorViewModel.HotstringCaseSensitive),
        nameof(MacroEditorViewModel.HotstringInsideWord),
        nameof(MacroEditorViewModel.HotstringRequireEnd),
        nameof(MacroEditorViewModel.HotstringEraseTyped),
        nameof(MacroEditorViewModel.HotstringKeepEnd),
        nameof(MacroEditorViewModel.WindowChange),
        nameof(MacroEditorViewModel.ProcessName),
        nameof(MacroEditorViewModel.ProcessChange),
        nameof(MacroEditorViewModel.WatchPath),
        nameof(MacroEditorViewModel.WatchChange),
        nameof(MacroEditorViewModel.WatchFilter),
        nameof(MacroEditorViewModel.WatchSubfolders),
        nameof(MacroEditorViewModel.ScheduleMode),
        nameof(MacroEditorViewModel.ScheduleInterval),
        nameof(MacroEditorViewModel.ScheduleUnit),
        nameof(MacroEditorViewModel.ScheduleTime),
        nameof(MacroEditorViewModel.BindKey),
        nameof(MacroEditorViewModel.PositionCapture),
        nameof(MacroEditorViewModel.ColorMatch),
        nameof(MacroEditorViewModel.ColorPositionX),
        nameof(MacroEditorViewModel.ColorPositionY),
        nameof(MacroEditorViewModel.UseCursorPosition),
        nameof(MacroEditorViewModel.TriggerOnce),
        nameof(MacroEditorViewModel.HexColor),
        nameof(MacroEditorViewModel.ColorTolerance),
        nameof(MacroEditorViewModel.RecordMouseMovement),
        nameof(MacroEditorViewModel.RecordMouseButtons),
        nameof(MacroEditorViewModel.RecordKeyboardKeys),
        nameof(MacroEditorViewModel.RecordGamepadButtons),
        nameof(MacroEditorViewModel.RecordDelays),
        nameof(MacroEditorViewModel.IgnoreDelaysBelowMs),
        nameof(MacroEditorViewModel.RecordIgnoreInjected),
        nameof(MacroEditorViewModel.DelayScale),
        nameof(MacroEditorViewModel.InterfaceToggleHotkey));

    /// <summary>Properties the editor presents, selects, captures or reports with; never a macro field.</summary>
    public static readonly IReadOnlySet<string> View = Set(
        nameof(MacroEditorViewModel.Notice),
        nameof(MacroEditorViewModel.IsDirty),
        nameof(MacroEditorViewModel.SettingsPage),
        nameof(MacroEditorViewModel.IsCapturingToggleKey),
        nameof(MacroEditorViewModel.ToggleKeyProblem),
        nameof(MacroEditorViewModel.IsCapturingHotstringEnd),
        nameof(MacroEditorViewModel.HotstringEndCaptureStatus),
        nameof(MacroEditorViewModel.IsCapturingKey),
        nameof(MacroEditorViewModel.IsRecording),
        nameof(MacroEditorViewModel.RecordedEvents),
        nameof(MacroEditorViewModel.IsCapturingInterfaceHotkey),
        nameof(MacroEditorViewModel.InterfaceHotkeyProblem));

    /// <summary>Whether a property change should be treated as an edit of the macro.</summary>
    public static bool IsDocument(string? propertyName)
        => propertyName is not null && Document.Contains(propertyName);

    private static IReadOnlySet<string> Set(params string[] names)
        => new HashSet<string>(names, StringComparer.Ordinal);
}
