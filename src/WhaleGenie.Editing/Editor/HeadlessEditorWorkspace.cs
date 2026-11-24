using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.Editing;

/// <summary>
/// The documents one web editor has open. Each macro keeps its own C# session and its own dirty
/// state, so switching tabs never throws away an edit; this is the document-strip half of the
/// editor the native window used to keep in <c>MacroEditorSession</c>.
/// </summary>
internal sealed class HeadlessEditorWorkspace(ProjectWorkspace workspace,
    IDeviceLayer? devices = null, IEditorSettings? settings = null)
{
    private const string NewKey = "\u0000new";

    private readonly IEditorSettings _settings = settings ?? DefaultEditorSettings.Instance;

    private readonly List<string> _order = [];
    private readonly Dictionary<string, HeadlessEditorSession> _sessions = new(StringComparer.Ordinal);

    private string? _active;

    public HeadlessEditorSession? Active
        => _active is not null && _sessions.TryGetValue(_active, out var session) ? session : null;

    public string? ActiveKey => _active;

    public IReadOnlyList<EditorTabSnapshot> Tabs
        => [.. _order.Where(_sessions.ContainsKey).Select(key =>
            new EditorTabSnapshot(key, _sessions[key].Name, _sessions[key].IsDirty,
                _sessions[key].HasProblem, _sessions[key].IsSubMacro))];

    public HeadlessEditorSession New()
    {
        var key = NewKey;
        for (var index = 2; _sessions.ContainsKey(key); index++)
        {
            key = NewKey + ":" + index;
        }

        var session = HeadlessEditorSession.Open(workspace, null, devices, _settings);
        _sessions[key] = session;
        _order.Add(key);
        _active = key;
        return session;
    }

    public HeadlessEditorSession Open(string? name)
    {
        var key = name is { Length: > 0 } ? name.Trim() : NewKey;
        if (!_sessions.TryGetValue(key, out var session))
        {
            session = HeadlessEditorSession.Open(workspace, name is { Length: > 0 } ? key : null,
                devices, _settings);
            _sessions[key] = session;
            _order.Add(key);
        }

        _active = key;
        return session;
    }

    public HeadlessEditorSession OpenSubmacro(string parentKey, string name)
    {
        if (!_sessions.TryGetValue(parentKey, out var parent))
        {
            throw new InvalidOperationException("the editor has no document " + parentKey);
        }

        var key = "\0sub:" + parentKey + ":" + name.Trim();
        if (!_sessions.TryGetValue(key, out var session))
        {
            session = HeadlessEditorSession.OpenSubmacro(workspace, parent, name, devices, _settings);
            _sessions[key] = session;
            _order.Add(key);
        }

        _active = key;
        return session;
    }

    /// <summary>
    /// Brings a document to the front by the session object itself. A command answered by another
    /// document — a child's command is answered by the macro that owns it — leaves the report on
    /// that document, so the strip has to follow it.
    /// </summary>
    public void Activate(HeadlessEditorSession session)
    {
        foreach (var key in _order)
        {
            if (_sessions.TryGetValue(key, out var open) && ReferenceEquals(open, session))
            {
                _active = key;
                return;
            }
        }
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

    public void Close(string key)
    {
        if (!_sessions.TryGetValue(key, out var session))
        {
            return;
        }

        if (session.IsDirty)
        {
            throw new InvalidOperationException(Strings.Get("Editor.CloseDirty"));
        }

        var active = string.Equals(_active, key, StringComparison.Ordinal);
        _sessions.Remove(key);
        _order.Remove(key);
        if (active)
        {
            _active = _order.LastOrDefault();
        }
    }

    public void CloseActive()
    {
        if (_active is not null)
        {
            Close(_active);
        }
    }
}
