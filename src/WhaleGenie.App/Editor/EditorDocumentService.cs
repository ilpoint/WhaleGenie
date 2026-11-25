using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Editing;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.Editor;

/// <summary>
/// The open editor documents as the rest of the application sees them: open, save, close, and
/// what a leave question has to name. The bridge only translates commands; the project bridge,
/// the exit question and MCP ask this service instead of reaching into a document set.
/// </summary>
public sealed class EditorDocumentService : IEditorDocuments
{
    private readonly EditorDocumentSet _documents;

    /// <summary>Raised whenever the open documents or their tabs moved outside a page command.</summary>
    public event Action? Changed;

    public EditorDocumentService(ProjectWorkspace workspace, IDeviceLayer? devices = null,
        IEditorSettings? settings = null, IPublicLibraryStore? publicLibrary = null)
    {
        _documents = new EditorDocumentSet(workspace, devices, settings, publicLibrary);
    }

    /// <summary>The active session, for the action/value command families that edit one document.</summary>
    internal HeadlessEditorSession? Active => _documents.Active;

    internal HeadlessEditorSession Required()
        => _documents.Active ?? throw new System.InvalidOperationException(
            Strings.Get("Bridge.EditorNoSession"));

    internal string? ActiveId => _documents.ActiveId;

    internal IReadOnlyList<EditorTabSnapshot> Tabs => _documents.Tabs;

    internal EditorSnapshot? Snapshot() => _documents.Snapshot();

    internal HeadlessEditorSession New()
    {
        var session = _documents.New();
        Notify();
        return session;
    }

    internal HeadlessEditorSession Open(string? name)
    {
        var session = _documents.Open(name);
        Notify();
        return session;
    }

    internal HeadlessEditorSession OpenSubmacro(string? ownerId, string name)
    {
        var session = _documents.OpenSubmacro(ownerId, name);
        Notify();
        return session;
    }

    internal HeadlessEditorSession CreateSubmacro(string? ownerId, string? name)
    {
        var session = _documents.CreateSubmacro(ownerId, name);
        Notify();
        return session;
    }

    internal HeadlessEditorSession ImportSubmacro(string? ownerId, MacroItem source, string? name)
    {
        var session = _documents.ImportSubmacro(ownerId, source, name);
        Notify();
        return session;
    }

    internal HeadlessEditorSession Activate(string documentId)
    {
        var session = _documents.Activate(documentId);
        Notify();
        return session;
    }

    internal void Activate(HeadlessEditorSession session)
    {
        _documents.Activate(session);
        Notify();
    }

    internal void Close(string? documentId, bool discard)
    {
        if (documentId is { Length: > 0 } id)
        {
            _documents.Close(id, discard);
        }
        else
        {
            _documents.CloseActive(discard);
        }

        Notify();
    }

    internal void CloseActive(bool discard)
    {
        _documents.CloseActive(discard);
        Notify();
    }

    internal void CloseAll(bool discard)
    {
        _documents.CloseAll(discard);
        Notify();
    }

    internal void Deactivate() => _documents.Deactivate();

    public bool HasUnsavedWork => _documents.HasUnsavedWork;

    public bool HasOpenDocuments => _documents.HasOpenDocuments;

    public IReadOnlyList<string> UnsavedDocuments => _documents.UnsavedDocuments;

    /// <summary>Saves one document, or the active one when no id is given.</summary>
    internal IReadOnlyList<EditorDocumentSaveResult> Save(string? documentId = null)
    {
        var results = _documents.Save(documentId);
        Notify();
        return results;
    }

    internal IReadOnlyList<EditorDocumentSaveResult> SaveAll()
    {
        var results = _documents.SaveAll();
        Notify();
        return results;
    }

    internal void DiscardAll()
    {
        _documents.DiscardAll();
        Notify();
    }

    private void Notify() => Changed?.Invoke();

    public Task<EditorSaveAllResult> SaveAllAsync()
    {
        var results = SaveAll();
        var blocked = results
            .Where(result => result.Outcome is not (EditorSaveOutcomes.Saved
                or EditorSaveOutcomes.Unchanged))
            .Select(result => new EditorBlockedDocument(
                result.Name,
                result.Problem is { Length: > 0 } problem
                    ? problem
                    : Strings.Get("Editor.Problem.CannotSave")))
            .ToList();
        return Task.FromResult(new EditorSaveAllResult(
            blocked.Count == 0 && !HasUnsavedWork, blocked));
    }

    public Task DiscardAllAsync()
    {
        DiscardAll();
        return Task.CompletedTask;
    }

    public Task CloseAllAsync()
    {
        CloseAll(discard: false);
        return Task.CompletedTask;
    }
}
