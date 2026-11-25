using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>One public macro in the library a session can copy from.</summary>
[BridgeContract]
public sealed record EditorPublicMacroSnapshot(string Id, int Version, string Name, string Description);

/// <summary>One page of an action form: its key, its label and whether it carries anything.</summary>
[BridgeContract]
public sealed record EditorActionPageSnapshot(string Key, string Label, bool Present, bool HasDot);

/// <summary>The editor session's snapshot: the macro fields and the step tree.</summary>
[BridgeContract]
public sealed record EditorSnapshot(
    string DocumentId,
    string Name, string Description, string Group,
    IReadOnlyList<string> SubMacros, IReadOnlyList<EditorMacroParameterSnapshot> CallableParameters,
    IReadOnlyList<EditorMacroResultSnapshot> CallableResults,
    bool FollowInputLimit, int InputLimit, string ToggleKey,
    bool Dirty, int StepCount,
    IReadOnlyList<EditorStepSnapshot> Steps, IReadOnlyList<EditorTriggerSnapshot> Triggers,
    IReadOnlyList<EditorVariableSnapshot> Variables, IReadOnlyList<EditorGlobalSnapshot> Globals,
    IReadOnlyList<EditorValueChoiceSnapshot> ValueChoices,
    bool CanUndo, bool CanRedo,
    bool CanSave, string? NameProblem, string ProblemSummary, string DeclarationProblemSummary,
    string CallImpactSummary, string Notice, string TriggerProblemSummary,
    string ToggleKeyProblem, EditorRecordingSnapshot Recording, bool IsSubMacro,
    IReadOnlyList<EditorTabSnapshot> Tabs, string? ActiveDocumentId,
    EditorActionFormSnapshot? ActionForm, int Revision);

/// <summary>One global declaration in the editor.</summary>
[BridgeContract]
public sealed record EditorGlobalSnapshot(
    string Name, string Type, string Default, string Description, bool Persisted);

/// <summary>One callable parameter declaration.</summary>
[BridgeContract]
public sealed record EditorMacroParameterSnapshot(
    string Name, string Type, bool Required, string Default, string Description,
    IReadOnlyList<EditorMacroFieldSnapshot> Fields);

/// <summary>One callable result declaration.</summary>
[BridgeContract]
public sealed record EditorMacroResultSnapshot(
    string Name, string Type, string Description, IReadOnlyList<EditorMacroFieldSnapshot> Fields);

/// <summary>One field of a record declaration.</summary>
[BridgeContract]
public sealed record EditorMacroFieldSnapshot(string Name, string Type);

/// <summary>One value the variable wall offers.</summary>
[BridgeContract]
public sealed record EditorValueChoiceSnapshot(
    string Name, string Type, string Origin, string Source, string Description, string Group,
    string Display, bool Persistent);

/// <summary>One row of the session's value-function table.</summary>
[BridgeContract]
public sealed record EditorValueFunctionSnapshot(
    string Name, string Signature, string Description, string Group);

/// <summary>One trigger kind a new entry may choose.</summary>
[BridgeContract]
public sealed record EditorTriggerKindSnapshot(string Value, string Label);

/// <summary>The recording options of one macro.</summary>
[BridgeContract]
public sealed record EditorRecordingSnapshot(
    int PositionIndex, IReadOnlyList<string> PositionOptions, bool MouseMovement, bool MouseButtons,
    bool KeyboardKeys, bool GamepadButtons, bool Delays, int IgnoreDelaysBelowMs,
    bool IgnoreInjected, double DelayScale, string DelayScaleDisplay, bool HasDelayScale);

/// <summary>One local variable declaration.</summary>
[BridgeContract]
public sealed record EditorVariableSnapshot(
    string Name, string Type, string Initial, string Description, string Source, bool IsDeclared);

/// <summary>One variable type a declaration may choose.</summary>
[BridgeContract]
public sealed record EditorVariableTypeSnapshot(string Value, string Label);

/// <summary>One trigger entry of the macro.</summary>
[BridgeContract]
public sealed record EditorTriggerSnapshot(
    string Id, string Kind, string KindLabel, string Summary,
    bool Enabled, string Binding, string Loop, bool Once, string TimerMode, string Time,
    string Interval, string Unit, string FilePath, string FileChange, string FileFilter,
    bool FileSubfolders, string ProcessName, string ProcessChange, string HotstringText,
    string ColorMatch, string ColorHex, string ColorTolerance, string ColorX, string ColorY,
    bool FollowCursor, string IdleSeconds, string WindowTitle, string WindowProcess,
    string WindowClass, string WindowHow, string WindowChange, string InterfaceTitle,
    string InterfaceProcess, string InterfaceHow, bool WindowCaseSensitive, bool WindowTrim,
    bool WindowMultiline, string HotstringEndChars, bool HotstringCaseSensitive,
    bool HotstringInsideWord, bool HotstringRequireEnd, bool HotstringEraseTyped,
    bool HotstringKeepEnd, int HotstringMaximumLength,
    IReadOnlyList<EditorHotstringEndKeySnapshot> HotstringEndKeys,
    string HotstringEndCaptureProblem, string InterfaceHotkey, string InterfaceHotkeyProblem,
    IReadOnlyList<string> InterfaceGroupMembers);

/// <summary>One end key of a hotstring trigger.</summary>
[BridgeContract]
public sealed record EditorHotstringEndKeySnapshot(
    string Value, string Label, bool Checked, bool IsCustom);

/// <summary>One step of the tree, with its parameters and child lists.</summary>
[BridgeContract]
public sealed record EditorStepSnapshot(
    string Id, string Type, string Name, bool Enabled, string Comment, int TimeoutMs,
    int RetryCount, int RetryDelayMs, string OnError, bool AcceptsSteps, string Detail,
    string Problem, string InputMark, string MetaSummary,
    IReadOnlyList<EditorParameterSnapshot> Parameters,
    IReadOnlyList<EditorStepSectionSnapshot> Sections);

/// <summary>One child list of a block, such as the "then" list of an if.</summary>
[BridgeContract]
public sealed record EditorStepSectionSnapshot(
    string Name, string Label, IReadOnlyList<EditorStepSnapshot> Steps);

/// <summary>One editable parameter of a step.</summary>
[BridgeContract]
public sealed record EditorParameterSnapshot(
    string Name, string Label, string Kind, string Value,
    IReadOnlyList<EditorChoiceSnapshot> Options, IReadOnlyList<EditorColumnSnapshot> Columns,
    IReadOnlyList<EditorRowSnapshot> Rows, EditorConditionSnapshot? Condition,
    string Display, string ValueForm, bool OffersValue, bool TakesFormula, bool ShowsNameMark,
    bool IsNumber, bool IsNumberFormula, bool IsMultiline, bool IsColor, bool IsPath, bool IsKey,
    string Placeholder, bool Required, IReadOnlyList<EditorValuePartSnapshot> Parts,
    bool Advanced, string Group, string GroupLabel, bool IsReturns);

/// <summary>One piece of a value field: literal text or a variable name.</summary>
[BridgeContract]
public sealed record EditorValuePartSnapshot(string Kind, string Text, string Name);

/// <summary>A condition expression as nested rows and groups.</summary>
[BridgeContract]
public sealed record EditorConditionSnapshot(
    string Join, IReadOnlyList<EditorConditionNodeSnapshot> Nodes, bool Editable);

/// <summary>One row or group of a condition expression.</summary>
[BridgeContract]
public sealed record EditorConditionNodeSnapshot(
    string Kind, string Join, string Left, string Compare, string Right,
    IReadOnlyList<EditorConditionNodeSnapshot> Children);

/// <summary>One comparison a condition row may use.</summary>
[BridgeContract]
public sealed record EditorConditionCompareSnapshot(string Value, string Label);

/// <summary>One column of a row parameter.</summary>
[BridgeContract]
public sealed record EditorColumnSnapshot(string Name, bool Required);

/// <summary>One stored row of a list parameter.</summary>
[BridgeContract]
public sealed record EditorRowSnapshot(IReadOnlyList<string> Cells);

/// <summary>One labelled option.</summary>
[BridgeContract]
public sealed record EditorChoiceSnapshot(string Value, string Label);

/// <summary>One runnable action in the catalogue.</summary>
[BridgeContract]
public sealed record EditorActionSnapshot(
    string Key, string Name, string Category, string CategoryLabel, string CategoryShort,
    string Group, string GroupLabel, int GroupRank, string Description,
    IReadOnlyList<EditorActionParameterSnapshot> Parameters);

/// <summary>One category of the action catalogue.</summary>
[BridgeContract]
public sealed record EditorActionCategorySnapshot(string? Value, string Label, string Short);

/// <summary>One parameter a catalogue action declares.</summary>
[BridgeContract]
public sealed record EditorActionParameterSnapshot(
    string Name, string Label, string Kind, bool Required, string Placeholder,
    IReadOnlyList<EditorChoiceSnapshot> Options);

/// <summary>One open editor document's tab.</summary>
[BridgeContract]
public sealed record EditorTabSnapshot(
    string DocumentId, string Name, bool Dirty, bool Problem, bool IsSubMacro,
    string? OwnerDocumentId);

/// <summary>The keys drawn for a key field or a key sequence.</summary>
[BridgeContract]
public sealed record EditorKeyPadSnapshot(
    string Mode, int MaxLength, IReadOnlyList<IReadOnlyList<EditorKeyCapSnapshot>> Rows);

/// <summary>One key cap of the key pad.</summary>
[BridgeContract]
public sealed record EditorKeyCapSnapshot(string Label, string Value, double Width);

/// <summary>The literal keyboard for a typed-string trigger.</summary>
[BridgeContract]
public sealed record EditorCharacterPadSnapshot(
    int MaximumLength, IReadOnlyList<IReadOnlyList<EditorCharacterCapSnapshot>> Rows);

/// <summary>One character cap of the character pad.</summary>
[BridgeContract]
public sealed record EditorCharacterCapSnapshot(
    string Normal, string Shifted, double Width, string Kind);
