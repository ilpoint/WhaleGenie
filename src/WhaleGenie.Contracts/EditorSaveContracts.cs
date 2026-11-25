using System.Collections.Generic;

namespace WhaleGenie.Contracts;

/// <summary>
/// The outcomes a save can come to. They are strings rather than an enum because the bridge
/// serializes without a string enum converter: a number on the wire would make the page guess
/// what "3" meant.
/// </summary>
public static class EditorSaveOutcomes
{
    /// <summary>The project took the document's draft; its dirty flag was cleared.</summary>
    public const string Saved = "saved";

    /// <summary>The draft said exactly what the project already held; nothing needed writing.</summary>
    public const string Unchanged = "unchanged";

    /// <summary>The macro itself has a problem that has to be fixed before it can be saved.</summary>
    public const string Blocked = "blocked";

    /// <summary>The macro the document was editing has left the project, or was replaced by another.</summary>
    public const string Conflict = "conflict";

    /// <summary>The page named a document that is no longer open.</summary>
    public const string Missing = "missing";
}

/// <summary>What happened to one document during a save.</summary>
[BridgeContract]
public sealed record EditorDocumentSaveResult(
    string DocumentId,
    string Name,
    string Outcome,
    string? Problem);

/// <summary>The answer to a save: the snapshot it left behind and one result per document touched.</summary>
[BridgeContract]
public sealed record EditorSaveResponse(
    EditorSnapshot? Snapshot,
    IReadOnlyList<EditorDocumentSaveResult> Results);

/// <summary>The names of the documents a leave question has to name, in tab order.</summary>
[BridgeContract]
public sealed record EditorUnsavedDocumentsSnapshot(
    IReadOnlyList<string> Names);
