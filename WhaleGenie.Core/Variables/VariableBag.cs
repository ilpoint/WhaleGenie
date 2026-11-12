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
///
/// <para>
/// One name is one slot. An identity does not make a second slot: it only says which declaration a
/// reference belongs to, so a rename can follow it. What identities do buy is the one thing a name
/// cannot say — whether two writes mean the same variable. A write whose identity is not the one
/// the name is bound to, when either of them is a declaration of this run, is refused rather than
/// quietly taking the slot; a write from one step output into a name another step output already
/// holds is still the ordinary overwrite every macro relies on.
/// </para>
/// </summary>
public sealed class VariableBag : IVariableResolver
{
    private sealed record Entry(Value Value, string Id);

    private readonly Dictionary<string, Entry> _byName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The identity index. Every name in it is one in <see cref="_byName"/>.</summary>
    private readonly Dictionary<string, string> _byId = new(StringComparer.Ordinal);

    /// <summary>Identities this run has declared, so a clash can be told from an ordinary overwrite.</summary>
    private readonly HashSet<string> _declared = new(StringComparer.Ordinal);

    /// <summary>What each declared name holds, for the runs that were told a type.</summary>
    private readonly Dictionary<string, ValueKind> _types = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>How many names this scope holds right now, which is what a run watches for growth.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _byName.Count;
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
        var changed = false;
        lock (_gate)
        {
            EnsureFits(name, value);
            _byName.TryGetValue(name, out var existing);
            // A name that is already there keeps the identity it has: a script writing the name a
            // declaration reads updates that declaration rather than growing a second entry.
            changed = existing is null || !existing.Value.Equals(value);
            Put(name, existing?.Id ?? string.Empty, value);
        }

        if (changed)
        {
            Changed?.Invoke(name);
        }
    }

    /// <summary>
    /// Writes a value under an identity. A known identity writes the entry it names; a name nothing
    /// has claimed yet takes the identity it is given. A name that belongs to a declaration of this
    /// run is refused when a different identity tries to take it — that is two declarations sharing
    /// one name, and the value one of them wrote would otherwise be read by the other.
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
            if (_byId.TryGetValue(key.Id, out var bound))
            {
                EnsureFits(bound, value);
                Put(bound, key.Id, value);
                changed = bound;
            }
            else
            {
                EnsureFits(key.Name, value);
                if (_byName.TryGetValue(key.Name, out var existing)
                    && existing.Id.Length > 0
                    && !string.Equals(existing.Id, key.Id, StringComparison.Ordinal)
                    && (_declared.Contains(existing.Id) || _declared.Contains(key.Id)))
                {
                    throw new VariableConflictException(key.Name, existing.Id);
                }

                Put(key.Name, key.Id, value);
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
            EnsureFits(name, value);
            added = !_byName.ContainsKey(name);
            if (added)
            {
                Put(name, string.Empty, value);
            }
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
            EnsureFits(key.Name, value);
            // An identity already in the bag already holds a value, wherever it is written down:
            // the declaration it belongs to may have been renamed since. There is nothing to add.
            added = !_byId.ContainsKey(key.Id)
                && !_byName.ContainsKey(key.Name)
                && PutNew(key.Name, key.Id, value);
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
            removed = RemoveEntry(name);
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

            if (entry.Id.Length > 0
                && _byId.TryGetValue(entry.Id, out var bound)
                && string.Equals(bound, oldName, StringComparison.OrdinalIgnoreCase))
            {
                _byId.Remove(entry.Id);
            }

            _byName[newName] = entry;
            if (entry.Id.Length > 0)
            {
                _byId[entry.Id] = newName;
            }

            if (_types.Remove(oldName, out var kind))
            {
                _types[newName] = kind;
            }
        }

        Changed?.Invoke(newName);
        Changed?.Invoke(oldName);
        return true;
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
            if (_types.Remove(oldName, out var kind))
            {
                _types[newName] = kind;
            }
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
    /// Says an identity belongs to a declaration of this run. Only a declared identity is worth
    /// refusing: one left behind by a rename or an import is allowed to take the slot, because the
    /// alternative is a value nobody can write any more.
    /// </summary>
    public void Declare(string id)
    {
        if (id.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _declared.Add(id);
        }
    }

    /// <summary>Forgets every declaration and type, for a run that is starting from its own macro.</summary>
    public void ResetDeclarations()
    {
        lock (_gate)
        {
            _declared.Clear();
            _types.Clear();
        }
    }

    /// <summary>Records what a declared name holds. Text accepts anything, so it need not be said.</summary>
    public void DeclareType(string name, ValueKind kind)
    {
        var written = name.Trim();
        if (written.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            _types[written] = kind;
        }
    }

    /// <summary>The kind a declared name holds, or false when nothing said.</summary>
    public bool TryTypeOf(string name, out ValueKind kind)
    {
        lock (_gate)
        {
            return _types.TryGetValue(name, out kind);
        }
    }

    /// <summary>
    /// Puts a value back the way it was before a block took its name, identity included. This is
    /// the undo of a loop's own variables: the slot may have been holding a declaration, and the
    /// loop's counter wrote over it.
    /// </summary>
    public void Restore(string name, string id, Value value)
    {
        lock (_gate)
        {
            Put(name, id, value);
        }

        Changed?.Invoke(name);
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
            var updated = Value.FromNumber(sum);
            EnsureFits(name, updated);
            Put(name, entry.Id, updated);
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
            var updated = Value.FromNumber(sum);
            EnsureFits(name, updated);
            Put(name, entry.Id, updated);
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
            EnsureFits(name, updated);
            Put(name, entry.Id, updated);
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
            EnsureFits(name, updated);
            Put(name, entry.Id, updated);
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

    /// <summary>
    /// The one place a name's value and its identity are written together. Every caller holds the
    /// lock; keeping the two indexes in step here is what makes the rest of the class safe to read.
    /// </summary>
    private void Put(string name, string id, Value value)
    {
        if (_byName.TryGetValue(name, out var previous)
            && previous.Id.Length > 0
            && !string.Equals(previous.Id, id, StringComparison.Ordinal)
            && _byId.TryGetValue(previous.Id, out var bound)
            && string.Equals(bound, name, StringComparison.OrdinalIgnoreCase))
        {
            _byId.Remove(previous.Id);
        }

        _byName[name] = new Entry(value, id);
        if (id.Length > 0)
        {
            _byId[id] = name;
        }
    }

    /// <summary>Adds a name that is known to be free, and reports whether it was added.</summary>
    private bool PutNew(string name, string id, Value value)
    {
        if (_byName.ContainsKey(name))
        {
            return false;
        }

        _byName[name] = new Entry(value, id);
        if (id.Length > 0)
        {
            _byId[id] = name;
        }

        return true;
    }

    private bool RemoveEntry(string name)
    {
        if (!_byName.Remove(name, out var entry))
        {
            return false;
        }

        if (entry.Id.Length > 0
            && _byId.TryGetValue(entry.Id, out var bound)
            && string.Equals(bound, name, StringComparison.OrdinalIgnoreCase))
        {
            _byId.Remove(entry.Id);
        }

        return true;
    }

    /// <summary>
    /// Refuses a value a declared name cannot hold. Nothing declared says nothing; text holds
    /// whatever it is given, because a text variable is how a macro keeps something whose shape it
    /// has not decided. The empty value is always allowed: it is how a result nobody has written
    /// yet reads, and it is not a value of the wrong kind.
    /// </summary>
    private void EnsureFits(string name, Value value)
    {
        if (value.IsNull
            || !_types.TryGetValue(name, out var kind)
            || kind is ValueKind.Text
            || value.Kind == kind)
        {
            return;
        }

        throw new VariableTypeException(name, kind, value.Kind);
    }
}
