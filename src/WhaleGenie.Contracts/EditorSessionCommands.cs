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
[JsonDerivedType(typeof(EditorSessionDeactivateCommand), "deactivate")]
[JsonDerivedType(typeof(EditorSessionCloseCommand), "close")]
[JsonDerivedType(typeof(EditorSessionPublicMacroAddCommand), "publicMacroAdd")]
[JsonDerivedType(typeof(EditorSessionSubMacroCreateCommand), "subMacroCreate")]
[JsonDerivedType(typeof(EditorSessionSubMacroCopyCommand), "subMacroCopy")]
[JsonDerivedType(typeof(EditorSessionSubMacroImportCommand), "subMacroImport")]
[JsonDerivedType(typeof(EditorSessionSubMacroDeleteCommand), "subMacroDelete")]
public abstract record EditorSessionCommand;

/// <summary>Open a macro by name; a missing name opens the first document.</summary>
[BridgeContract]
public sealed record EditorSessionOpenCommand(string? Name = null) : EditorSessionCommand;

/// <summary>A fresh untitled macro.</summary>
[BridgeContract]
public sealed record EditorSessionNewCommand : EditorSessionCommand;

/// <summary>Open a sub-macro below the document named, or the active one when the key is omitted.</summary>
[BridgeContract]
public sealed record EditorSessionSubOpenCommand(string Name, string? OwnerDocumentId = null)
    : EditorSessionCommand;

/// <summary>Write one macro setting.</summary>
[BridgeContract]
public sealed record EditorSessionMacroSettingCommand(string Field, string Value)
    : EditorSessionCommand;

/// <summary>Activate an open document tab by its stable identifier.</summary>
[BridgeContract]
public sealed record EditorSessionActivateCommand(string DocumentId) : EditorSessionCommand;

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

/// <summary>
/// Finish work on the page without closing its documents: the action or value draft it had open
/// is dropped, and an active recording stops. Used when the page is left.
/// </summary>
[BridgeContract]
public sealed record EditorSessionDeactivateCommand : EditorSessionCommand;

/// <summary>
/// Close a tab by its stable identifier, or the active one when the identifier is omitted.
/// <paramref name="Discard"/> lets the person say the changes are not wanted — the same answer
/// the close question offers.
/// </summary>
[BridgeContract]
public sealed record EditorSessionCloseCommand(string? DocumentId = null, bool? Discard = null)
    : EditorSessionCommand;

/// <summary>Add a public macro call step; the source names the project shelf or machine library.</summary>
[BridgeContract]
public sealed record EditorSessionPublicMacroAddCommand(string Id, int Version = 0,
    string Source = "local") : EditorSessionCommand;

/// <summary>Make an empty sub-macro; without a name it is given the next free one.</summary>
[BridgeContract]
public sealed record EditorSessionSubMacroCreateCommand(string? Name = null) : EditorSessionCommand;

/// <summary>Copy an existing sub-macro or project macro into the current macro.</summary>
[BridgeContract]
public sealed record EditorSessionSubMacroCopyCommand(string From, string? Name = null)
    : EditorSessionCommand;

/// <summary>
/// Bring a macro into the current macro as a child. <paramref name="Source"/> is
/// <c>public</c> (a library entry id), <c>project</c> (a project macro name) or <c>file</c>
/// (a package path and macro name in <paramref name="Reference"/>). The child is not called yet;
/// importing only gives the macro a child to call when the person adds the call step.
/// </summary>
[BridgeContract]
public sealed record EditorSessionSubMacroImportCommand(
    string Source, string? Reference = null, string? Path = null, string? Name = null,
    int Version = 0, string? LibrarySource = null)
    : EditorSessionCommand;

/// <summary>Remove a sub-macro this macro no longer calls.</summary>
[BridgeContract]
public sealed record EditorSessionSubMacroDeleteCommand(string Name) : EditorSessionCommand;
