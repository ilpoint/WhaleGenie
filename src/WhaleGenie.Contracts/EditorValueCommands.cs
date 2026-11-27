using System.Text.Json.Serialization;

namespace WhaleGenie.Contracts;

/// <summary>One command of the value editor. The discriminator is the command's kind.</summary>
[BridgeContract]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(EditorValueOpenCommand), "open")]
[JsonDerivedType(typeof(EditorValueKeyCommand), "command")]
[JsonDerivedType(typeof(EditorValueConfirmCommand), "confirm")]
[JsonDerivedType(typeof(EditorValueCancelCommand), "cancel")]
public abstract record EditorValueCommand;

/// <summary>Open the picker on one field, a condition side or a row cell.</summary>
[BridgeContract]
public sealed record EditorValueOpenCommand(
    string Step,
    string Parameter,
    string? ConditionField = null,
    int[]? ConditionPath = null,
    int? RowIndex = null,
    string? Column = null,
    string? Argument = null,
    int? DeclarationIndex = null,
    bool? GlobalDraft = null,
    string? VariableFamily = null,
    string? VariableName = null) : EditorValueCommand;

/// <summary>One keystroke in the picker: a token, name, sign or typed text.</summary>
[BridgeContract]
public sealed record EditorValueKeyCommand(
    string Action,
    string? Token = null,
    string? Name = null,
    string? Sign = null,
    string? Group = null,
    string? Text = null,
    int[]? Path = null,
    int[]? Target = null,
    bool? After = null,
    bool? Number = null,
    int? Direction = null,
    int? At = null,
    double? Width = null,
    bool? Append = null) : EditorValueCommand;

/// <summary>Write the field and close the picker.</summary>
[BridgeContract]
public sealed record EditorValueConfirmCommand : EditorValueCommand;

/// <summary>Close the picker without writing.</summary>
[BridgeContract]
public sealed record EditorValueCancelCommand : EditorValueCommand;
