using System.Text.Json.Serialization;

namespace WhaleGenie.Contracts;

/// <summary>One edit of the action form's draft. The discriminator is the command's kind.</summary>
[BridgeContract]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(EditorActionPageCommand), "page")]
[JsonDerivedType(typeof(EditorActionFieldCommand), "field")]
[JsonDerivedType(typeof(EditorActionRowAddCommand), "rowAdd")]
[JsonDerivedType(typeof(EditorActionRowRemoveCommand), "rowRemove")]
[JsonDerivedType(typeof(EditorActionRowMoveCommand), "rowMove")]
[JsonDerivedType(typeof(EditorActionRowCellCommand), "rowCell")]
[JsonDerivedType(typeof(EditorActionReturnsCommand), "returns")]
[JsonDerivedType(typeof(EditorActionReturnsFixNameCommand), "returnsFixName")]
[JsonDerivedType(typeof(EditorActionCallArgumentCommand), "callArgument")]
[JsonDerivedType(typeof(EditorActionMetaCommand), "meta")]
[JsonDerivedType(typeof(EditorActionGlobalCommand), "global")]
[JsonDerivedType(typeof(EditorActionBranchCommand), "branch")]
[JsonDerivedType(typeof(EditorActionWindowCommand), "window")]
[JsonDerivedType(typeof(EditorActionWindowRuleCommand), "windowRule")]
[JsonDerivedType(typeof(EditorActionPadKeyCommand), "padKey")]
public abstract record EditorActionCommand;

/// <summary>Switch the form page.</summary>
[BridgeContract]
public sealed record EditorActionPageCommand(string Page) : EditorActionCommand;

/// <summary>Write one declared field of the draft.</summary>
[BridgeContract]
public sealed record EditorActionFieldCommand(string Name, string Field, string Value)
    : EditorActionCommand;

/// <summary>Append a row to a table parameter.</summary>
[BridgeContract]
public sealed record EditorActionRowAddCommand(string Name) : EditorActionCommand;

/// <summary>Remove one row from a table parameter.</summary>
[BridgeContract]
public sealed record EditorActionRowRemoveCommand(string Name, int Index) : EditorActionCommand;

/// <summary>Move one row within a table parameter.</summary>
[BridgeContract]
public sealed record EditorActionRowMoveCommand(string Name, int Index, int By) : EditorActionCommand;

/// <summary>Write one cell of a table row.</summary>
[BridgeContract]
public sealed record EditorActionRowCellCommand(
    string Name, int Index, string Column, string Value) : EditorActionCommand;

/// <summary>Tick, untick or rename one return value.</summary>
[BridgeContract]
public sealed record EditorActionReturnsCommand(
    string Key, bool? Ticked = null, string? Name = null) : EditorActionCommand;

/// <summary>Fix a return value name to a free name.</summary>
[BridgeContract]
public sealed record EditorActionReturnsFixNameCommand(string Key) : EditorActionCommand;

/// <summary>Give or clear one call argument.</summary>
[BridgeContract]
public sealed record EditorActionCallArgumentCommand(
    string Name, bool? Given = null, string? Value = null) : EditorActionCommand;

/// <summary>Write a comment, enabled flag, timeout or retry field.</summary>
[BridgeContract]
public sealed record EditorActionMetaCommand(string Field, string Value, string Unit)
    : EditorActionCommand;

/// <summary>Write one global-value field.</summary>
[BridgeContract]
public sealed record EditorActionGlobalCommand(string Field, string Value) : EditorActionCommand;

/// <summary>Add a branch to a conditional draft.</summary>
[BridgeContract]
public sealed record EditorActionBranchCommand : EditorActionCommand;

/// <summary>Apply one window choice.</summary>
[BridgeContract]
public sealed record EditorActionWindowCommand(string Name, string Choice, string Value)
    : EditorActionCommand;

/// <summary>Apply one saved window rule.</summary>
[BridgeContract]
public sealed record EditorActionWindowRuleCommand(string Name, string Rule) : EditorActionCommand;

/// <summary>Apply one key from the on-screen key pad.</summary>
[BridgeContract]
public sealed record EditorActionPadKeyCommand(string Name, string Key, int Index, string Mode)
    : EditorActionCommand;
