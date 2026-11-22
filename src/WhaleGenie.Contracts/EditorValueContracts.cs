using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>A serialisable picture of the restored value picker.</summary>
[BridgeContract]
public sealed record EditorValueSnapshot(
    string Mode,
    string Title,
    string Hint,
    string Target,
    string TargetLine,
    bool ShowsBand,
    bool ShowsFormulaMaterials,
    int PanelSpan,
    bool IsTextMode,
    bool IsRunMode,
    bool IsValueMode,
    bool IsNameMode,
    EditorValueNode? Recipe,
    int Caret,
    int SpanEnd,
    int[] CaretPath,
    bool CaretAfter,
    int[]? SelectedPath,
    bool IsValueEmpty,
    string EmptyBandHint,
    string StructureHint,
    string Text,
    string Note,
    bool NoteIsProblem,
    string Saying,
    bool Reads,
    bool HasSaying,
    bool CanConfirm,
    bool CanUndo,
    bool CanRedo,
    bool CanClear,
    string TextBody,
    bool IsTypingNumber,
    string TypingText,
    string TypingPlaceholder,
    bool CanInsertTyped,
    string TypingNote,
    bool HasTypingNote,
    string FunctionQuery,
    string? SelectedFunctionGroup,
    bool IsAllFunctions,
    bool HasChosenFunction,
    bool HasChosenVariable,
    bool AllowNewName,
    string NewName,
    bool CanCreateName,
    string NewNameLine,
    IReadOnlyList<EditorValueOperator> Operators,
    IReadOnlyList<EditorValueToken> Values,
    IReadOnlyList<EditorValueFunctionGroup> FunctionGroups,
    IReadOnlyList<EditorValueFunction> Functions,
    EditorValueWall Wall);

/// <summary>One token of the structure band, with its position and what it came to.</summary>
[BridgeContract]
public sealed record EditorValueNode(
    string Kind,
    int[]? Path,
    int Start,
    int Length,
    string Text,
    string Operator,
    string Tone,
    string Name,
    string Before,
    bool IsName,
    bool IsGhost,
    bool IsRequired,
    bool IsEmpty,
    bool IsSelected,
    bool HasProblem,
    string ProblemHint,
    string? HoverHint,
    bool CanRemove,
    bool HasNoArguments,
    bool HasNoItems,
    bool Stacked,
    bool Compact,
    string ArgumentLabel,
    string ArgumentFlag,
    int ArgumentIndex,
    bool ArgumentOptional,
    bool ArgumentRequired,
    IReadOnlyList<EditorValueNode> Children,
    EditorValueNode? Ghost);

/// <summary>One operator in the signs panel.</summary>
[BridgeContract]
public sealed record EditorValueOperator(
    string Token, string Insert, string Hint, string Tone, bool Enabled);

/// <summary>One constant the value picker can place.</summary>
[BridgeContract]
public sealed record EditorValueToken(string Text, string Insert);

/// <summary>One function group in the value picker's panel.</summary>
[BridgeContract]
public sealed record EditorValueFunctionGroup(string Group, string Label, bool Selected);

/// <summary>One function the value picker can place.</summary>
[BridgeContract]
public sealed record EditorValueFunction(
    string Name,
    string Tag,
    string Heading,
    string WrittenAs,
    string ArityNote,
    string Hint,
    string Group,
    string Insert,
    IReadOnlyList<EditorValueFunctionArgument> Arguments);

/// <summary>One argument of a function in the value picker.</summary>
[BridgeContract]
public sealed record EditorValueFunctionArgument(string Label, string Hint);

/// <summary>One filter chip of the value picker's variable wall.</summary>
[BridgeContract]
public sealed record EditorValueVariableFilter(
    string Group, string Label, string Hint, bool Selected);

/// <summary>One variable the value picker's wall shows.</summary>
[BridgeContract]
public sealed record EditorValueVariable(
    string Name,
    string Display,
    string? Type,
    string Group,
    string Origin,
    string Source,
    string Description,
    bool Persistent,
    string Tone,
    string Details);

/// <summary>The variable wall of the value picker.</summary>
[BridgeContract]
public sealed record EditorValueWall(
    IReadOnlyList<EditorValueVariableFilter> Filters,
    string SelectedFilter,
    string Search,
    string Looking,
    string Details,
    bool IsEmpty,
    IReadOnlyList<EditorValueVariable> Shown);
