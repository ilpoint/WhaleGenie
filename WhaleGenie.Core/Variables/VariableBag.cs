using System;
using System.Collections.Generic;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Variables;

/// <summary>
/// One scope's worth of values, looked up without caring about letter case. A global bag is read
/// and written by every run at once, so each operation takes the bag's own lock; what the lock
/// does not make atomic is a read followed by a write, which is why the one operation that has to
/// be a single step — adding to a number — is offered as <see cref="TryAddNumber"/>.
///
/// <para>
/// A value is still stored under the name it is read by, because that is what a watcher, a script
/// and a file of saved values speak. An identity is kept beside it as an index: a bound reference
/// is answered from the index first, so renaming the declaration changes the label without losing
/// the value. A name nothing declared has no identity, and the name remains the whole answer.
/// </para>
/// </summary>
public sealed class VariableBag : IVariableResolver
{
    private sealed record Entry(Value Value, string Id);

    private readonly Dictionary<string, Entry> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _byId = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>Raised after a value is written or removed, with the name that moved.</summary>
    public event Action<string>? Changed;

    /// <summary>
    /// The values in this scope, for the debugger and for tests. This is a copy: a caller that
    /// walks it while another run writes must not see the dictionary change under it.
    /// </summary>
    public IReadOnlyDictionary<string, Value> Values
    {
        get
        {
            lock (_gate)
            {
                var copy = new Dictionary<string, Value>(_byName.Count, StringComparer.OrdinalIgnoreCase);
                foreach (var (name, entry) in _byName)
                {
                    copy[name] = entry.Value;
                }

                return copy;
            }
        }
    }

    public bool TryGet(string name, out Value value)
    {
        lock (_gate)
        {
            if (_byName.TryGetValue(name, out var entry))
            {
                value = entry.Value;
                return true;
            }
        }

        value = Value.Null;
        return false;
    }

    /// <summary>
    /// Reads a bound name by identity first and by spelling second. An identity that is gone — a
    /// declaration removed, a value written by a script — falls back to the name, because a run
    /// must still be able to read what a line says.
    /// </summary>
    public bool TryGet(VariableKey key, out Value value)
    {
        if (key.HasId)
        {
            lock (_gate)
            {
                if (_byId.TryGetValue(key.Id, out var name)
                    && _byName.TryGetValue(name, out var entry))
                {
                    value = entry.Value;
                    return true;
                }
            }
        }

        return TryGet(key.Name, out value);
    }

    public bool Contains(string name)
    {
        lock (_gate)
        {
            return _byName.ContainsKey(name);
        }
    }

    /// <summary>Reads one identity without falling back to the name it happens to carry.</summary>
    public bool TryGetById(string id, out Value value)
    {
        lock (_gate)
        {
            if (_byId.TryGetValue(id, out var name) && _byName.TryGetValue(name, out var entry))
            {
                value = entry.Value;
                return true;
            }
        }

        value = Value.Null;
        return false;
    }

    public bool ContainsId(string id)
    {
        lock (_gate)
        {
            return _byId.ContainsKey(id);
        }
    }

    public bool Contains(VariableKey key)
    {
        if (key.HasId)
        {
            lock (_gate)
            {
                if (_byId.ContainsKey(key.Id))
                {
                    return true;
                }
            }
        }

        return Contains(key.Name);
    }

    public void Set(string name, Value value)
    {
        lock (_gate)
        {
            // A name that is already there keeps the identity it has: a script writing the name a
            // declaration reads updates that declaration rather than growing a second entry.
            var entry = _byName.TryGetValue(name, out var existing)
                ? existing with { Value = value }
                : new Entry(value, string.Empty);
            _byName[name] = entry;
        }

        Changed?.Invoke(name);
    }

    /// <summary>
    /// Writes a value under an identity. A known identity writes the entry it names; an identity
    /// that is new to a name already here is claimed by that entry; anything else makes a new one.
    /// </summary>
    public void Set(VariableKey key, Value value)
    {
        if (!key.HasId)
        {
            Set(key.Name, value);
            return;
        }

        string changed;
        lock (_gate)
        {
            if (_byId.TryGetValue(key.Id, out var name))
            {
                _byName[name] = _byName[name] with { Value = value };
                changed = name;
            }
            else if (_byName.TryGetValue(key.Name, out var existing))
            {
                if (existing.Id.Length > 0 && !string.Equals(existing.Id, key.Id, StringComparison.Ordinal))
                {
                    _byId.Remove(existing.Id);
                }

                _byName[key.Name] = existing with { Value = value, Id = key.Id };
                _byId[key.Id] = key.Name;
                changed = key.Name;
            }
            else
            {
                _byName[key.Name] = new Entry(value, key.Id);
                _byId[key.Id] = key.Name;
                changed = key.Name;
            }
        }

        Changed?.Invoke(changed);
    }

    /// <summary>
    /// Writes a value only when the name is not there yet. Seeding a run uses this so that a value
    /// another run has already written is not put back to its declared starting value.
    /// </summary>
    public bool SetIfAbsent(string name, Value value)
    {
        bool added;
        lock (_gate)
        {
            added = _byName.TryAdd(name, new Entry(value, string.Empty));
        }

        if (added)
        {
            Changed?.Invoke(name);
        }

        return added;
    }

    public bool SetIfAbsent(VariableKey key, Value value)
    {
        if (!key.HasId)
        {
            return SetIfAbsent(key.Name, value);
        }

        bool added;
        lock (_gate)
        {
            added = !_byName.ContainsKey(key.Name)
                && _byName.TryAdd(key.Name, new Entry(value, key.Id));
            if (added)
            {
                _byId[key.Id] = key.Name;
            }
        }

        if (added)
        {
            Changed?.Invoke(key.Name);
        }

        return added;
    }

    public void SetText(string name, string? text) => Set(name, Value.FromText(text));

    public void SetNumber(string name, double number) => Set(name, Value.FromNumber(number));

    public void SetList(string name, IEnumerable<Value> items) => Set(name, Value.FromList(items));

    public bool Remove(string name)
    {
        bool removed;
        lock (_gate)
        {
            removed = _byName.Remove(name, out var entry);
            if (removed && entry!.Id.Length > 0)
            {
                _byId.Remove(entry.Id);
            }
        }

        if (removed)
        {
            Changed?.Invoke(name);
        }

        return removed;
    }

    public bool Remove(VariableKey key)
    {
        var name = key.Name;
        if (key.HasId)
        {
            lock (_gate)
            {
                if (_byId.TryGetValue(key.Id, out var bound))
                {
                    name = bound;
                }
            }
        }

        return Remove(name);
    }

    /// <summary>Moves one name to another without changing its value or its identity.</summary>
    public bool Rename(string oldName, string newName)
    {
        lock (_gate)
        {
            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)
                || !_byName.Remove(oldName, out var entry)
                || _byName.ContainsKey(newName))
            {
                return false;
            }

            _byName[newName] = entry;
            if (entry.Id.Length > 0)
            {
                _byId[entry.Id] = newName;
            }

            Changed?.Invoke(newName);
            Changed?.Invoke(oldName);
            return true;
        }
    }

    /// <summary>Renames the entry an identity belongs to, which is what a declaration rename does.</summary>
    public bool Rebind(string id, string newName)
    {
        lock (_gate)
        {
            if (!_byId.TryGetValue(id, out var oldName)
                || string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)
                || !_byName.Remove(oldName, out var entry)
                || _byName.ContainsKey(newName))
            {
                return false;
            }

            _byName[newName] = entry;
            _byId[id] = newName;
        }

        Changed?.Invoke(newName);
        return true;
    }

    public void Clear()
    {
        string[] names;
        lock (_gate)
        {
            names = [.. _byName.Keys];
            _byName.Clear();
            _byId.Clear();
        }

        foreach (var name in names)
        {
            Changed?.Invoke(name);
        }
    }

    /// <summary>
    /// Adds to a number under the same lock that reads it, so two runs adding to one global cannot
    /// both read the old value and write the same sum. False means the name is not there; a value
    /// that is not a number is reported by <see cref="Value.AsNumber"/> rather than written.
    /// </summary>
    public bool TryAddNumber(string name, double amount, out double sum)
    {
        lock (_gate)
        {
            if (!_byName.TryGetValue(name, out var entry))
            {
                sum = 0;
                return false;
            }

            sum = entry.Value.AsNumber() + amount;
            _byName[name] = entry with { Value = Value.FromNumber(sum) };
        }

        Changed?.Invoke(name);
        return true;
    }

    public bool TryAddNumber(VariableKey key, double amount, out double sum)
    {
        string name;
        lock (_gate)
        {
            name = key.HasId && _byId.TryGetValue(key.Id, out var bound) ? bound : key.Name;
            if (!_byName.TryGetValue(name, out var entry))
            {
                sum = 0;
                return false;
            }

            sum = entry.Value.AsNumber() + amount;
            _byName[name] = entry with { Value = Value.FromNumber(sum) };
        }

        Changed?.Invoke(name);
        return true;
    }

    /// <summary>
    /// Changes a value under the bag's own lock: what the change is handed is the value that is
    /// there now, and what it returns is what is written. A read and a write that two runs must not
    /// split — appending to one global list — are therefore a single step. A change that throws
    /// writes nothing, which is how a bad index or a value that is not a list leaves the list as it
    /// was. False means the name is not there at all.
    /// </summary>
    public bool TryUpdate(string name, Func<Value, Value> change, out Value updated)
    {
        bool found;
        lock (_gate)
        {
            if (!_byName.TryGetValue(name, out var entry))
            {
                updated = Value.Null;
                return false;
            }

            updated = change(entry.Value);
            _byName[name] = entry with { Value = updated };
            found = true;
        }

        if (found)
        {
            Changed?.Invoke(name);
        }

        return found;
    }

    public bool TryUpdate(VariableKey key, Func<Value, Value> change, out Value updated)
    {
        string name;
        lock (_gate)
        {
            name = key.HasId && _byId.TryGetValue(key.Id, out var bound) ? bound : key.Name;
            if (!_byName.TryGetValue(name, out var entry))
            {
                updated = Value.Null;
                return false;
            }

            updated = change(entry.Value);
            _byName[name] = entry with { Value = updated };
        }

        Changed?.Invoke(name);
        return true;
    }

    /// <summary>The identity a name is bound to, or empty when nothing bound it.</summary>
    public string IdOf(string name)
    {
        lock (_gate)
        {
            return _byName.TryGetValue(name, out var entry) ? entry.Id : string.Empty;
        }
    }

    /// <summary>The name an identity currently reads as, or empty when no entry has it.</summary>
    public string NameOf(string id)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(id, out var name) ? name : string.Empty;
        }
    }
}
