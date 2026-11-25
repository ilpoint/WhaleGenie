using System.Collections.Generic;
using System.Threading.Tasks;

namespace WhaleGenie.Editor;

/// <summary>One open document that could not be handed back to the project, and why.</summary>
public sealed record EditorBlockedDocument(string Name, string Problem);

/// <summary>The answer to "save every open document", with what stood in the way.</summary>
public sealed record EditorSaveAllResult(bool Saved, IReadOnlyList<EditorBlockedDocument> Blocked);

/// <summary>
/// The editor's unsaved work as the exit path sees it. The editor sessions belong to the bridge,
/// but the question "is there anything to lose?" belongs to the window that is leaving; this is the
/// one way across so the shell does not have to know how a document is made.
/// </summary>
public interface IEditorDocuments
{
    bool HasUnsavedWork { get; }

    /// <summary>Whether any document is open, dirty or not.</summary>
    bool HasOpenDocuments { get; }

    /// <summary>The documents the exit question names, in the order the tab strip shows them.</summary>
    IReadOnlyList<string> UnsavedDocuments { get; }

    Task<EditorSaveAllResult> SaveAllAsync();

    /// <summary>
    /// Closes every dirty document without writing it, which is what "not saved" means: the
    /// project keeps the version it already has and the next open reads that one back.
    /// </summary>
    Task DiscardAllAsync();

    /// <summary>Closes every document; used after they were saved or discarded for a project change.</summary>
    Task CloseAllAsync();
}
