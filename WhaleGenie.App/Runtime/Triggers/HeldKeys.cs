using System;
using System.Collections.Generic;

namespace WhaleGenie.Runtime;

/// <summary>
/// The keys and buttons held down right now. A combination is answered from this, and a typed
/// abbreviation is dropped when a modifier is down, so both read the same set.
/// </summary>
internal sealed class HeldKeys
{
    private readonly object _gate = new();
    private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

    public void Press(string key)
    {
        lock (_gate)
        {
            _keys.Add(key);
        }
    }

    public void Release(string key)
    {
        lock (_gate)
        {
            _keys.Remove(key);
        }
    }

    public IReadOnlyList<string> All()
    {
        lock (_gate)
        {
            return [.. _keys];
        }
    }

    /// <summary>Whether a binding answers to this key with what is held down right now.</summary>
    public bool Binds(string binding, string key)
    {
        lock (_gate)
        {
            return TriggerKeys.Binds(binding, key, _keys);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _keys.Clear();
        }
    }
}
