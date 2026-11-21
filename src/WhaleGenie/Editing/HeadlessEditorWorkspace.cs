using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Models;

namespace WhaleGenie.Editing;

/// <summary>
/// The documents one web editor has open. Each macro keeps its own C# session and its own dirty
/// state, so switching tabs never throws away an edit; this is the document-strip half of the
/// editor the native window used to keep in <c>MacroEditorSession</c>.
/// </summary>
internal sealed class HeadlessEditorWorkspace(ProjectWorkspace workspace)
{
    private const string NewKey = "\u0000new";

    private readonly List<string> _order = [];
    private readonly Dictionary<string, HeadlessEditorSession> _sessions = new(StringComparer.Ordinal);

    private string? _active;

    public HeadlessEditorSession? Active
        => _active is not null && _sessions.TryGetValue(_active, out var session) ? session : null;

    public string? ActiveKey => _active;

    public IReadOnlyList<EditorTabSnapshot> Tabs
        => [.. _order.Where(_sessions.ContainsKey).Select(key =>
            new EditorTabSnapshot(key, _sessions[key].Name, _sessions[key].IsDirty))];

    public HeadlessEditorSession Open(string? name)
    {
        var key = name is { Length: > 0 } ? name.Trim() : NewKey;
        if (!_sessions.TryGetValue(key, out var session))
        {
            session = HeadlessEditorSession.Open(workspace, name is { Length: > 0 } ? key : null);
            _sessions[key] = session;
            _order.Add(key);
        }

        _active = key;
        return session;
    }

    public HeadlessEditorSession Activate(string key)
    {
        if (!_sessions.TryGetValue(key, out var session))
        {
            throw new InvalidOperationException("the editor has no document " + key);
        }

        _active = key;
        return session;
    }

    public void CloseActive()
    {
        if (_active is not null)
        {
            _sessions.Remove(_active);
            _order.Remove(_active);
            _active = _order.LastOrDefault();
        }
    }
}

internal sealed record EditorTabSnapshot(string Key, string Name, bool Dirty);
