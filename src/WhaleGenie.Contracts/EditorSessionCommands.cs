using System.Text.Json.Serialization;

namespace WhaleGenie.Contracts;

/// <summary>One document-lifecycle command. The discriminator is the command's kind.</summary>
[BridgeContract]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(EditorSessionOpenCommand), "open")]
[JsonDerivedType(typeof(EditorSessionNewCommand), "new")]
[JsonDerivedType(typeof(EditorSessionSubOpenCommand), "subOpen")]
[JsonDerivedType(typeof(EditorSessionMacroSettingCommand), "macroSetting")]
[JsonDerivedType(typeof(EditorSessionActivateCommand), "activate")]
[JsonDerivedType(typeof(EditorSessionPatchCommand), "patch")]
[JsonDerivedType(typeof(EditorSessionUndoCommand), "undo")]
[JsonDerivedType(typeof(EditorSessionRedoCommand), "redo")]
[JsonDerivedType(typeof(EditorSessionSaveCommand), "save")]
[JsonDerivedType(typeof(EditorSessionCloseCommand), "close")]
[JsonDerivedType(typeof(EditorSessionPublicMacroAddCommand), "publicMacroAdd")]
[JsonDerivedType(typeof(EditorSessionSubMacroCreateCommand), "subMacroCreate")]
[JsonDerivedType(typeof(EditorSessionSubMacroCopyCommand), "subMacroCopy")]
[JsonDerivedType(typeof(EditorSessionSubMacroDeleteCommand), "subMacroDelete")]
[JsonDerivedType(typeof(EditorSessionExportCommand), "export")]
public abstract record EditorSessionCommand;

/// <summary>Open a macro by name; a missing name opens the first document.</summary>
[BridgeContract]
public sealed record EditorSessionOpenCommand(string? Name = null) : EditorSessionCommand;

/// <summary>A fresh untitled macro.</summary>
[BridgeContract]
public sealed record EditorSessionNewCommand : EditorSessionCommand;

/// <summary>Open a sub-macro below the active document.</summary>
[BridgeContract]
public sealed record EditorSessionSubOpenCommand(string Name) : EditorSessionCommand;

/// <summary>Write one macro setting.</summary>
[BridgeContract]
public sealed record EditorSessionMacroSettingCommand(string Field, string Value)
    : EditorSessionCommand;

/// <summary>Activate an open document tab.</summary>
[BridgeContract]
public sealed record EditorSessionActivateCommand(string Key) : EditorSessionCommand;

/// <summary>Write the macro's name, description or group.</summary>
[BridgeContract]
public sealed record EditorSessionPatchCommand(
    string? Name = null, string? Description = null, string? Group = null)
    : EditorSessionCommand;

/// <summary>Undo the last edit.</summary>
[BridgeContract]
public sealed record EditorSessionUndoCommand : EditorSessionCommand;

/// <summary>Redo the last undone edit.</summary>
[BridgeContract]
public sealed record EditorSessionRedoCommand : EditorSessionCommand;

/// <summary>Save the active document.</summary>
[BridgeContract]
public sealed record EditorSessionSaveCommand : EditorSessionCommand;

/// <summary>Close a tab, or the active one when the key is omitted.</summary>
[BridgeContract]
public sealed record EditorSessionCloseCommand(string? Key = null) : EditorSessionCommand;

/// <summary>Add a public macro call step.</summary>
[BridgeContract]
public sealed record EditorSessionPublicMacroAddCommand(string Id) : EditorSessionCommand;

/// <summary>Make an empty sub-macro; without a name it is given the next free one.</summary>
[BridgeContract]
public sealed record EditorSessionSubMacroCreateCommand(string? Name = null) : EditorSessionCommand;

/// <summary>Copy an existing sub-macro or project macro into the current macro.</summary>
[BridgeContract]
public sealed record EditorSessionSubMacroCopyCommand(string From, string? Name = null)
    : EditorSessionCommand;

/// <summary>Remove a sub-macro this macro no longer calls.</summary>
[BridgeContract]
public sealed record EditorSessionSubMacroDeleteCommand(string Name) : EditorSessionCommand;

/// <summary>Export the document as a package at the chosen path.</summary>
[BridgeContract]
public sealed record EditorSessionExportCommand(string Path) : EditorSessionCommand;
