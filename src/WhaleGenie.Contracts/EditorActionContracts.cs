using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>What the page needs to draw one action being written or edited.</summary>
[BridgeContract]
public sealed record EditorActionFormSnapshot(
    bool Open, bool IsEditing, bool CanConfirm, bool CanRun, bool Running, string RunLabel,
    string ValidationMessage, bool ValidationIsProblem,
    string ActionKey, string Name, string Description, string StepId, string CommitLabel,
    string Page, IReadOnlyList<EditorActionPageSnapshot> Pages,
    IReadOnlyList<EditorActionRowSnapshot> Rows,
    IReadOnlyList<EditorActionRowSnapshot> AdvancedRows,
    IReadOnlyList<EditorReturnsRowSnapshot> Returns,
    IReadOnlyList<EditorCallArgumentSnapshot> CallArguments,
    EditorSteppingMetaSnapshot Meta, EditorGlobalDraftSnapshot? GlobalDraft,
    IReadOnlyList<EditorWindowRuleChoiceSnapshot> WindowRules, string WindowAbove,
    string BranchSummary, string AttemptSummary, bool IsBlock, bool ShowsBranches, bool ShowsAttempt,
    string Json, string Notice, bool NoticeIsWarning, EditorClipboardPreviewSnapshot ClipboardPreview);

/// <summary>One row of an action form: a group of cells and the picker beside them.</summary>
[BridgeContract]
public sealed record EditorActionRowSnapshot(
    string GroupKey, string GroupTitle, string GroupHint, int GroupIndex, bool IsGroupStart,
    bool IsQuad, IReadOnlyList<EditorActionFieldSnapshot> Cells, EditorPickerSnapshot? Picker);

/// <summary>The desktop picker a form row offers.</summary>
[BridgeContract]
public sealed record EditorPickerSnapshot(string Kind, string Label, string Hint, string Mode);

/// <summary>One field of an action form.</summary>
[BridgeContract]
public sealed record EditorActionFieldSnapshot(
    string Name, string Label, string Hint, string Kind, string ValueForm, string Value, string Display,
    IReadOnlyList<EditorValuePartSnapshot> Parts, IReadOnlyList<EditorChoiceSnapshot> Options,
    IReadOnlyList<EditorColumnSnapshot> Columns, EditorConditionSnapshot? Condition,
    bool IsApplicable, bool IsDefault, bool IsMissing, bool Required, bool Advanced,
    string Group, string GroupLabel, bool OffersValue, bool TakesFormula, bool ShowsNameMark,
    bool AllowsNewName, bool IsNumber, bool IsNumberFormula, bool IsDuration, string Unit,
    string UnitLabel, IReadOnlyList<EditorChoiceSnapshot> DurationUnits, bool IsPercent,
    double PercentValue, string ChannelNote, string JitterRange, bool IsMultiline, bool IsCode,
    bool IsColor, string Colour, bool IsPath, string PathIntent, string PathFilter,
    bool IsKey, bool IsKeys, bool IsSelector, bool IsBrowserTarget, bool IsImage, bool IsImageList,
    bool IsRegion, bool IsRegionList, bool IsColourPoints, bool IsColourRows, bool IsKeySequence,
    bool IsNarrow, bool IsWindow, bool IsWindowRule, bool IsMacro, bool IsStepChoice, bool IsRecord,
    bool IsPoint, bool IsRect, string Placeholder, bool HasToBeAnswered, bool IsFollowingWindow,
    string WindowRuleSummary, IReadOnlyList<EditorRegionRowSnapshot> Regions,
    IReadOnlyList<EditorImageRowSnapshot> Images, IReadOnlyList<EditorColourRowSnapshot> Colours,
    IReadOnlyList<EditorColourPointRowSnapshot> ColourPoints, IReadOnlyList<EditorKeyRowSnapshot> Keys,
    IReadOnlyList<EditorNarrowRowSnapshot> NarrowRows, IReadOnlyList<EditorGenericRowSnapshot> Rows,
    IReadOnlyList<EditorChoiceSnapshot> Macros, IReadOnlyList<EditorChoiceSnapshot> Steps,
    IReadOnlyList<EditorBrowserCandidateSnapshot> Candidates,
    EditorWindowPreviewSnapshot? WindowPreview, EditorImagePreviewSnapshot? ImagePreview,
    decimal? Minimum, decimal? Maximum, decimal Increment, decimal? JitterPercent,
    string EmptyMeans);

/// <summary>One cell of a row, with the line it reads as and the parts it is made of.</summary>
[BridgeContract]
public sealed record EditorRowCellSnapshot(
    string Text, IReadOnlyList<EditorValuePartSnapshot> Parts);

/// <summary>One region row of a form.</summary>
[BridgeContract]
public sealed record EditorRegionRowSnapshot(
    EditorRowCellSnapshot X, EditorRowCellSnapshot Y, EditorRowCellSnapshot Width,
    EditorRowCellSnapshot Height, EditorRowCellSnapshot Variable);

/// <summary>One image row of a form, with its thumbnail.</summary>
[BridgeContract]
public sealed record EditorImageRowSnapshot(
    string Text, bool CanMoveUp, bool CanMoveDown, EditorImagePreviewSnapshot? Preview,
    IReadOnlyList<EditorValuePartSnapshot> Parts);

/// <summary>A picture read into a data URL, or the reason it could not be.</summary>
[BridgeContract]
public sealed record EditorImagePreviewSnapshot(string DataUrl, string FileName, string Problem);

/// <summary>One colour row of a form.</summary>
[BridgeContract]
public sealed record EditorColourRowSnapshot(string Colour, string Tolerance);

/// <summary>One colour point row of a form.</summary>
[BridgeContract]
public sealed record EditorColourPointRowSnapshot(
    string Dx, string Dy, string Colour, bool MustNot, string Tolerance);

/// <summary>One key row of a form.</summary>
[BridgeContract]
public sealed record EditorKeyRowSnapshot(int Number, string Keys, string Hold, string Gap);

/// <summary>One "still looks like" row of a form.</summary>
[BridgeContract]
public sealed record EditorNarrowRowSnapshot(
    string How, string Value, string Count, bool NeedsValue, bool WantsWords, bool OnPage,
    IReadOnlyList<EditorChoiceSnapshot> Ways);

/// <summary>A generic stored row of a list parameter.</summary>
[BridgeContract]
public sealed record EditorGenericRowSnapshot(IReadOnlyList<string> Cells);

/// <summary>One row of the returns page.</summary>
[BridgeContract]
public sealed record EditorReturnsRowSnapshot(
    string Key, string Label, string Hint, string Structure, string StructureTip, bool Ticked,
    string Name, bool IsNameTaken, string NameWarning, bool IsPrimary);

/// <summary>One argument of a macro call.</summary>
[BridgeContract]
public sealed record EditorCallArgumentSnapshot(
    string Name, string Hint, bool Required, bool Declared, bool Given, string Value,
    IReadOnlyList<EditorValuePartSnapshot> ValueParts, string ValueForm, bool OffersValue);

/// <summary>The execution settings of the step being written.</summary>
[BridgeContract]
public sealed record EditorSteppingMetaSnapshot(
    bool Enabled, string Comment, EditorDurationSnapshot Timeout, decimal? RetryCount,
    EditorDurationSnapshot RetryDelay, EditorDurationSnapshot DelayBefore,
    EditorDurationSnapshot DelayAfter, string RetryBackoff, string OnError, string RetryPlan,
    bool Tuned);

/// <summary>A duration and the units it may be written in.</summary>
[BridgeContract]
public sealed record EditorDurationSnapshot(
    int Milliseconds, string Written, string Unit, IReadOnlyList<EditorChoiceSnapshot> Units);

/// <summary>A global variable declared from an action form.</summary>
[BridgeContract]
public sealed record EditorGlobalDraftSnapshot(
    string Name, string Type, string Default, string DefaultPreview, string Description,
    bool PersistValue, string Problem, bool HasError, IReadOnlyList<EditorChoiceSnapshot> Types,
    string ValueForm, IReadOnlyList<EditorValuePartSnapshot> DefaultParts);

/// <summary>One window rule a form can copy into the step.</summary>
[BridgeContract]
public sealed record EditorWindowRuleChoiceSnapshot(string Summary, string Rule, int Count);

/// <summary>The windows a rule would match right now.</summary>
[BridgeContract]
public sealed record EditorWindowPreviewSnapshot(
    string Summary, string Note, string More, IReadOnlyList<EditorWindowPreviewRowSnapshot> Rows);

/// <summary>One row of the window preview.</summary>
[BridgeContract]
public sealed record EditorWindowPreviewRowSnapshot(string Title, string PartLabel, string Part);

/// <summary>One browser element candidate.</summary>
[BridgeContract]
public sealed record EditorBrowserCandidateSnapshot(string Selector, string Text);

/// <summary>The clipboard picture a form shows, if any.</summary>
[BridgeContract]
public sealed record EditorClipboardPreviewSnapshot(bool Shown, string Note);
