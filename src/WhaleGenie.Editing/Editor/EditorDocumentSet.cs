using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Editing;

/// <summary>
/// The documents one web editor has open. Each document has a stable identifier that survives
/// saves and renames; the tab strip, the close questions and the bridge commands address it by
/// that identifier rather than by a macro name that can move underneath them.
/// </summary>
/// <remarks>
/// A document is either a project macro or a child of one. A child's edit lives in the parent's
/// editing model until the parent is saved, so the project is never mutated behind the editor's
/// back. Closing a parent closes its children; closing the active document activates the tab to
/// its left (or the next one to the right when there is no left).
/// </remarks>
internal sealed class EditorDocumentSet(ProjectWorkspace workspace,
    IDeviceLayer? devices = null, IEditorSettings? settings = null)
{
    private readonly IEditorSettings _settings = settings ?? DefaultEditorSettings.Instance;
    private readonly List<Document> _documents = [];
    private readonly Dictionary<string, Document> _byId = new(StringComparer.Ordinal);

    private int _nextDocumentId;
    private Document? _active;

    private sealed class Document(string id, HeadlessEditorSession session, string? ownerId)
    {
        public string Id { get; } = id;

        public HeadlessEditorSession Session { get; } = session;

        public string? OwnerId { get; } = ownerId;
    }

    public HeadlessEditorSession? Active => _active?.Session;

    public string? ActiveId => _active?.Id;

    public bool HasOpenDocuments => _documents.Count > 0;

    public bool HasUnsavedWork => _documents.Any(IsDirty);

    public IReadOnlyList<string> UnsavedDocuments
        => [.. Tabs.Where(tab => tab.Dirty).Select(tab => tab.Name)];

    public IReadOnlyList<EditorTabSnapshot> Tabs
        => [.. _documents.Select(document => new EditorTabSnapshot(
            document.Id,
            document.Session.Name,
            IsDirty(document),
            document.Session.HasProblem,
            document.OwnerId is not null,
            document.OwnerId))];

    /// <summary>The active document's snapshot with the strip that belongs beside it.</summary>
    public EditorSnapshot? Snapshot()
    {
        if (_active is not { } active)
        {
            return null;
        }

        return active.Session.Snapshot() with
        {
            DocumentId = active.Id,
            Tabs = Tabs,
            ActiveDocumentId = active.Id,
        };
    }

    public HeadlessEditorSession New()
    {
        var session = HeadlessEditorSession.Open(workspace, null, devices, _settings,
            RootNamesForNewDocument());
        Add(session, ownerId: null);
        return session;
    }

    /// <summary>
    /// A new child document. It is not in the owner's model yet: only saving it puts the child in
    /// the list, so clicking New twice and abandoning one leaves no half-made submacro behind.
    /// </summary>
    public HeadlessEditorSession CreateSubmacro(string? ownerDocumentId, string? name)
    {
        var owner = ResolveOwner(ownerDocumentId);
        var root = owner.Session.RootDocument;
        var taken = root.TakenMacroNames();
        foreach (var document in ChildrenOf(owner))
        {
            taken.Add(document.Session.Name);
        }

        var session = HeadlessEditorSession.OpenNewSubmacro(workspace, root, taken, name,
            devices, _settings);
        Add(session, owner.Id);
        return session;
    }

    /// <summary>Copies a macro into the owner's child list and opens the new child as a tab.</summary>
    public HeadlessEditorSession ImportSubmacro(string? ownerDocumentId, MacroItem source,
        string? name)
    {
        var owner = ResolveOwner(ownerDocumentId);
        var child = owner.Session.RootDocument.ImportSubmacro(source, name);
        return OpenSubmacro(owner.Id, child.Name);
    }

    /// <summary>
    /// Opens a project macro, or activates it when it is already open. The identity check is the
    /// target instance first and the last bound name second, so a saved new tab and a renamed tab
    /// are found instead of being opened a second time.
    /// </summary>
    public HeadlessEditorSession Open(string? name)
    {
        var wanted = name?.Trim() ?? string.Empty;
        if (wanted.Length == 0)
        {
            return _documents.FirstOrDefault()?.Session ?? New();
        }

        var macro = workspace.Macros.FirstOrDefault(candidate =>
            string.Equals(candidate.Name.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("the project has no macro named " + wanted);

        if (FindRootFor(macro) is { } existing)
        {
            Activate(existing);
            return existing.Session;
        }

        var session = HeadlessEditorSession.Open(workspace, macro.Name, devices, _settings);
        Add(session, ownerId: null);
        return session;
    }

    /// <summary>Opens a child of the named document, or the active document when it is omitted.</summary>
    public HeadlessEditorSession OpenSubmacro(string? ownerDocumentId, string name)
    {
        var owner = ResolveOwner(ownerDocumentId);
        var wanted = name.Trim();
        var existing = _documents.FirstOrDefault(document =>
            document.OwnerId == owner.Id
            && (string.Equals(document.Session.OriginalName.Trim(), wanted,
                    StringComparison.OrdinalIgnoreCase)
                || string.Equals(document.Session.Name.Trim(), wanted,
                    StringComparison.OrdinalIgnoreCase)));
        if (existing is not null)
        {
            Activate(existing);
            return existing.Session;
        }

        var session = HeadlessEditorSession.OpenSubmacro(workspace, owner.Session, wanted, devices,
            _settings);
        Add(session, owner.Id);
        return session;
    }

    /// <summary>Brings a document to the front by the session that answered a command.</summary>
    public void Activate(HeadlessEditorSession session)
    {
        if (FindBySession(session) is { } document)
        {
            _active = document;
        }
    }

    public HeadlessEditorSession Activate(string documentId)
    {
        var document = Find(documentId)
            ?? throw new InvalidOperationException("the editor has no document " + documentId);
        _active = document;
        return document.Session;
    }

    /// <summary>
    /// Saves one document. A project macro saves its dirty children first, because those edits live
    /// in its model; a child saves only into its owner's model. The project is written once, when
    /// the owning macro is saved.
    /// </summary>
    public IReadOnlyList<EditorDocumentSaveResult> Save(string? documentId = null)
    {
        var document = documentId is { Length: > 0 } id
            ? Find(id) ?? throw new InvalidOperationException("the editor has no document " + id)
            : _active ?? throw new InvalidOperationException("the editor has no active document");
        if (document.OwnerId is not null)
        {
            return [SaveOne(document)];
        }

        var results = new List<EditorDocumentSaveResult>();
        foreach (var child in ChildrenOf(document).Where(child => child.Session.IsDirty).ToList())
        {
            results.Add(SaveOne(child));
        }

        if (document.Session.IsDirty)
        {
            results.Add(SaveOne(document));
        }

        return results;
    }

    /// <summary>
    /// Saves every dirty document once: children first, then the macros that own them. A blocked
    /// child does not stop the others, and its result stays in the answer; a parent only becomes
    /// dirty when a child was actually accepted.
    /// </summary>
    public IReadOnlyList<EditorDocumentSaveResult> SaveAll()
    {
        var results = new List<EditorDocumentSaveResult>();
        foreach (var child in _documents.Where(document => document.OwnerId is not null).ToList())
        {
            if (child.Session.IsDirty)
            {
                results.Add(SaveOne(child));
            }
        }

        foreach (var root in _documents.Where(document => document.OwnerId is null).ToList())
        {
            if (root.Session.IsDirty)
            {
                results.Add(SaveOne(root));
            }
        }

        return results;
    }

    /// <summary>Drops the documents that still hold edits, children with their owner.</summary>
    public void DiscardAll()
    {
        foreach (var document in _documents.Where(IsDirty).ToList())
        {
            if (_byId.ContainsKey(document.Id))
            {
                Close(document.Id, discard: true);
            }
        }
    }

    /// <summary>
    /// Closes a document. A dirty one is refused unless <paramref name="discard"/> says the changes
    /// are not wanted. Closing a macro also closes its child documents; a dirty child refuses the
    /// close of its parent for the same reason.
    /// </summary>
    public void Close(string documentId, bool discard = false)
    {
        if (Find(documentId) is not { } document)
        {
            return;
        }

        var subtree = new List<Document> { document };
        subtree.AddRange(ChildrenOf(document));
        if (!discard && subtree.Any(IsDirty))
        {
            throw new InvalidOperationException(Strings.Get("Editor.CloseDirty"));
        }

        var closeIndex = _documents.IndexOf(document);
        var activeClosed = _active is not null
            && subtree.Any(candidate => ReferenceEquals(candidate, _active));
        foreach (var candidate in subtree)
        {
            _documents.Remove(candidate);
            _byId.Remove(candidate.Id);
        }

        if (activeClosed)
        {
            var neighbour = closeIndex - 1;
            while (neighbour >= 0 && subtree.Any(candidate =>
                ReferenceEquals(candidate, _documents.ElementAtOrDefault(neighbour))))
            {
                neighbour--;
            }

            _active = neighbour >= 0
                ? _documents[neighbour]
                : _documents.ElementAtOrDefault(closeIndex);
        }
    }

    public void CloseActive(bool discard = false)
    {
        if (_active is { } active)
        {
            Close(active.Id, discard);
        }
    }

    public void CloseAll(bool discard = false)
    {
        if (!discard && _documents.Any(IsDirty))
        {
            throw new InvalidOperationException(Strings.Get("Editor.CloseDirty"));
        }

        _documents.Clear();
        _byId.Clear();
        _active = null;
    }

    /// <summary>Ends the transient page work of the active document without closing it.</summary>
    public void Deactivate() => _active?.Session.Deactivate();

    private void Add(HeadlessEditorSession session, string? ownerId)
    {
        var document = new Document(NextId(), session, ownerId);
        _documents.Add(document);
        _byId.Add(document.Id, document);
        _active = document;
    }

    private string NextId()
    {
        _nextDocumentId++;
        return "doc-" + _nextDocumentId.ToString(CultureInfo.InvariantCulture);
    }

    private Document? Find(string documentId)
        => _byId.GetValueOrDefault(documentId);

    private Document? FindRootFor(MacroItem macro)
        => _documents.FirstOrDefault(document =>
            document.OwnerId is null && document.Session.Matches(macro));

    /// <summary>
    /// The names a new project macro may not take: the project's macros and every root document
    /// already open, saved or not. Without the second half every click of the add button asked for
    /// the same default name while an earlier unsaved tab still held it.
    /// </summary>
    private HashSet<string> RootNamesForNewDocument()
    {
        var names = new HashSet<string>(
            workspace.Macros.Select(macro => macro.Name),
            StringComparer.OrdinalIgnoreCase);
        foreach (var document in _documents.Where(document => document.OwnerId is null))
        {
            names.Add(document.Session.Name);
        }

        return names;
    }

    private void Activate(Document document) => _active = document;

    private Document? FindBySession(HeadlessEditorSession session)
        => _documents.FirstOrDefault(document => ReferenceEquals(document.Session, session));

    private Document ResolveOwner(string? ownerDocumentId)
    {
        var owner = ownerDocumentId is { Length: > 0 } id
            ? Find(id) ?? throw new InvalidOperationException("the editor has no document " + id)
            : _active ?? throw new InvalidOperationException("the editor has no active document");
        if (owner.OwnerId is null)
        {
            return owner;
        }

        var root = owner.Session.RootDocument;
        return _documents.FirstOrDefault(document => ReferenceEquals(document.Session, root))
            ?? throw new InvalidOperationException("the owner document is no longer open");
    }

    private IEnumerable<Document> ChildrenOf(Document document)
        => _documents.Where(candidate => candidate.OwnerId == document.Id);

    private EditorDocumentSaveResult SaveOne(Document document)
    {
        var outcome = document.Session.Save();
        return new EditorDocumentSaveResult(
            document.Id,
            document.Session.Name,
            outcome.Outcome,
            outcome.Problem);
    }

    /// <summary>
    /// A parent's tab is dirty when its own model or any child still holds edits. A child that
    /// saved into the parent model marks the parent dirty through its session, so both routes are
    /// covered.
    /// </summary>
    private bool IsDirty(Document document)
        => document.Session.IsDirty || ChildrenOf(document).Any(IsDirty);
}
