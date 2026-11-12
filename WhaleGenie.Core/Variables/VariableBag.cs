using System;
using System.Collections.Generic;
using System.Globalization;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Variables;

/// <summary>
/// One scope's worth of values. A name is a binding, not a slot: the scope keeps which identity
/// each name stands for, and the values themselves are kept by identity. That is what makes a
/// rename a label change, a loop variable a shadow for the length of a block, and a write from a
/// step output overwrite the one before it — all without a second table to keep in step.
///
/// <para>
/// The two halves are a bijection: one name stands for one identity, and one identity is written
/// down under one name. A binding made through either half puts the other half right.
/// </para>
///
/// <para>
/// A global bag is read and written by every run at once, so each operation takes the bag's own
/// lock. What the lock does not make atomic is a read followed by a write, which is why the one
/// operation that has to be a single step — adding to a number, changing one item of a list — is
/// offered as a change under the lock.
/// </para>
/// </summary>
public sealed class VariableBag : IVariableResolver
{
    /// <summary>The name a name stands for, and the identity it is written under.</summary>
    private readonly Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The other half of the same bijection, so an identity can be asked its name.</summary>
    private readonly Dictionary<string, string> _bound = new(StringComparer.Ordinal);

    private readonly Dictionary<string, Value> _values = new(StringComparer.Ordinal);

    /// <summary>What each declared identity holds. An identity nothing declared holds anything.</summary>
    private readonly Dictionary<string, ValueKind> _kinds = new(StringComparer.Ordinal);

    private readonly object _gate = new();

    /// <summary>Where the next identity a dynamic name is given comes from.</summary>
    private long _next;

    /// <summary>Raised after a value is written or removed, with the name that moved.</summary>
    public event Action<string>? Changed;

    /// <summary>
    /// How many names this scope may hold, or <see cref="int.MaxValue"/> for no ceiling. Set on the
    /// table a run owns: a name built while the macro runs is kept until the run ends, and a loop
    /// that writes a new one every round would otherwise grow without a bound.
    /// </summary>
    public int Capacity { get; set; } = int.MaxValue;

    /// <summary>
    /// The values in this scope, by the names they are read by. This is a copy: a caller that walks
    /// it while another run writes must not see the dictionary change under it.
    /// </summary>
    public IReadOnlyDictionary<string, Value> Values
    {
        get
        {
            lock (_gate)
            {
                var copy = new Dictionary<string, Value>(_names.Count,
                    StringComparer.OrdinalIgnoreCase);
                foreach (var (name, id) in _names)
                {
                    if (_values.TryGetValue(id, out var value))
                    {
                        copy[name] = value;
                    }
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
                return _names.Count;
            }
        }
    }

    public bool TryGet(string name, out Value value)
    {
        lock (_gate)
        {
            if (_names.TryGetValue(name, out var id) && _values.TryGetValue(id, out var found))
            {
                value = found;
                return true;
            }
        }

        value = Value.Null;
        return false;
    }

    /// <summary>
    /// Reads a bound name by identity first and by spelling second. An identity that is gone — a
    /// value written by a script, a declaration removed since — falls back to the name, because a
    /// run must still be able to read what a line says.
    /// </summary>
    public bool TryGet(VariableKey key, out Value value)
    {
        if (key.HasId)
        {
            lock (_gate)
            {
                if (_values.TryGetValue(key.Id, out var found))
                {
                    value = found;
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
            return _names.ContainsKey(name);
        }
    }

    public bool Contains(VariableKey key)
    {
        if (key.HasId)
        {
            lock (_gate)
            {
                if (_values.ContainsKey(key.Id))
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
            changed = WriteLocked(name, value);
        }

        if (changed)
        {
            Changed?.Invoke(name);
        }
    }

    /// <summary>
    /// Writes a value under an identity. An identity this table already holds is written where it
    /// is; a name nothing has claimed is given this identity. A name that a declaration of this run
    /// has already claimed is refused when a different identity tries to take it — that is two
    /// declarations sharing one name, and the value one wrote would be read by the other.
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
            if (_values.TryGetValue(key.Id, out var existing))
            {
                EnsureFits(key.Id, key.Name, value);
                _values[key.Id] = value;
                BindLocked(key.Name, key.Id);
                changed = key.Name;
                if (existing.Equals(value))
                {
                    return;
                }
            }
            else
            {
                var kind = _kinds.GetValueOrDefault(key.Id, ValueKind.Text);
                EnsureFits(kind, key.Name, value);
                if (_names.TryGetValue(key.Name, out var bound)
                    && bound.Length > 0
                    && bound != key.Id
                    && _kinds.ContainsKey(bound))
                {
                    throw new VariableConflictException(key.Name, bound);
                }

                // A step output writing over another step's output takes the name with it; the
                // value nobody can name any more goes with the binding. A declared name is never
                // treated this way: writing it through another identity is refused above.
                if (_names.TryGetValue(key.Name, out var replaced)
                    && replaced.Length > 0
                    && replaced != key.Id
                    && !_kinds.ContainsKey(replaced))
                {
                    _values.Remove(replaced);
                }

                BindLocked(key.Name, key.Id);
                _values[key.Id] = value;
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
            if (_names.ContainsKey(name))
            {
                return false;
            }

            added = WriteLocked(name, value);
        }

        if (added)
        {
            Changed?.Invoke(name);
        }

        return added;
    }

    public bool SetIfAbsent(VariableKey key, Value value)
    {
        bool added;
        lock (_gate)
        {
            if ((key.HasId && _values.ContainsKey(key.Id)) || _names.ContainsKey(key.Name))
            {
                return false;
            }

            added = key.HasId
                ? WriteUnderLocked(key.Name, key.Id, value)
                : WriteLocked(key.Name, value);
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

    /// <summary>
    /// Says an identity belongs to a declaration of this run, under a name and a kind. A second
    /// declaration of the same name is refused: one name holds one value, so the second one would
    /// silently read what the first wrote.
    /// </summary>
    public void Declare(string id, string name, ValueKind kind)
    {
        var written = name.Trim();
        if (id.Length == 0 || written.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_names.TryGetValue(written, out var bound)
                && bound.Length > 0
                && bound != id
                && _kinds.ContainsKey(bound))
            {
                throw new VariableConflictException(written, bound);
            }

            _kinds[id] = kind;
            BindLocked(written, id);
            _values.TryAdd(id, Value.Null);
        }
    }

    /// <summary>
    /// Binds a name to an identity for the length of a block, answering the identity it replaced —
    /// empty when it replaced nothing. This is how a loop gives its counter a name without taking
    /// away whatever that name meant before.
    /// </summary>
    public string Bind(string name, string id)
    {
        lock (_gate)
        {
            var previous = _names.TryGetValue(name, out var bound) ? bound : string.Empty;
            BindLocked(name, id);
            _values.TryAdd(id, Value.Null);
            return previous;
        }
    }

    /// <summary>
    /// Puts a binding back the way it was before <see cref="Bind"/>. The block's own value is let go
    /// of with it: nothing reads that identity afterwards, and keeping it would count towards the
    /// ceiling for a name the macro can no longer say.
    /// </summary>
    public void Unbind(string name, string id, string previous)
    {
        lock (_gate)
        {
            if (_names.TryGetValue(name, out var bound) && bound == id)
            {
                _names.Remove(name);
                _bound.Remove(id);
            }

            if (!_kinds.ContainsKey(id))
            {
                _values.Remove(id);
            }

            if (previous.Length > 0 && _values.ContainsKey(previous))
            {
                BindLocked(name, previous);
            }
        }

        Changed?.Invoke(name);
    }

    public bool Remove(string name)
    {
        bool removed;
        lock (_gate)
        {
            removed = RemoveLocked(name);
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
        lock (_gate)
        {
            if (key.HasId && _bound.TryGetValue(key.Id, out var bound))
            {
                name = bound;
            }
        }

        return Remove(name);
    }

    /// <summary>
    /// Moves one name to another. The identity stays where it is, so a reference that was bound to
    /// it goes on reading the same value under the new name.
    /// </summary>
    public bool Rename(string oldName, string newName)
    {
        lock (_gate)
        {
            if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)
                || !_names.Remove(oldName, out var id)
                || _names.ContainsKey(newName))
            {
                return false;
            }

            _names[newName] = id;
            _bound[id] = newName;
        }

        Changed?.Invoke(newName);
        Changed?.Invoke(oldName);
        return true;
    }

    /// <summary>True when this identity is held here, whether or not a name stands for it.</summary>
    public bool ContainsId(string id)
    {
        lock (_gate)
        {
            return id.Length > 0 && _values.ContainsKey(id);
        }
    }

    /// <summary>The name an identity is written down under, or empty when it has none.</summary>
    public string NameOf(string id)
    {
        lock (_gate)
        {
            return _bound.GetValueOrDefault(id, string.Empty);
        }
    }

    /// <summary>Empties the scope. A value nobody can name is not worth keeping.</summary>
    public void Clear()
    {
        string[] names;
        lock (_gate)
        {
            names = [.. _names.Keys];
            _names.Clear();
            _bound.Clear();
            _values.Clear();
            _kinds.Clear();
        }

        foreach (var name in names)
        {
            Changed?.Invoke(name);
        }
    }

    /// <summary>Reads one identity without falling back to a name.</summary>
    public bool TryGetById(string id, out Value value)
    {
        if (id.Length > 0)
        {
            lock (_gate)
            {
                if (_values.TryGetValue(id, out var found))
                {
                    value = found;
                    return true;
                }
            }
        }

        value = Value.Null;
        return false;
    }

    /// <summary>
    /// Forgets which identities were declared. A declaration belongs to the macro that wrote it, so
    /// a table the debug window keeps between runs does not go on enforcing last run's kinds.
    /// </summary>
    public void ResetDeclarations()
    {
        lock (_gate)
        {
            _kinds.Clear();
        }
    }

    /// <summary>A fresh identity for a name this scope is about to hold without a declaration.</summary>
    public string NewId()
    {
        lock (_gate)
        {
            return NewIdLocked();
        }
    }

    /// <summary>The identity a name stands for, or empty when nothing is bound to it.</summary>
    public string IdOf(string name)
    {
        lock (_gate)
        {
            return _names.TryGetValue(name, out var id) ? id : string.Empty;
        }
    }

    /// <summary>True when this identity belongs to a declaration of this run.</summary>
    public bool IsDeclared(string id)
    {
        if (id.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            return _kinds.ContainsKey(id);
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
            if (!_names.TryGetValue(name, out var id) || !_values.TryGetValue(id, out var value))
            {
                sum = 0;
                return false;
            }

            sum = value.AsNumber() + amount;
            EnsureFits(id, name, Value.FromNumber(sum));
            _values[id] = Value.FromNumber(sum);
        }

        Changed?.Invoke(name);
        return true;
    }

    public bool TryAddNumber(VariableKey key, double amount, out double sum)
    {
        string name;
        lock (_gate)
        {
            if (!ResolveLocked(key, out name, out var id))
            {
                sum = 0;
                return false;
            }

            sum = _values[id].AsNumber() + amount;
            EnsureFits(id, name, Value.FromNumber(sum));
            _values[id] = Value.FromNumber(sum);
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
            if (!_names.TryGetValue(name, out var id) || !_values.TryGetValue(id, out var value))
            {
                updated = Value.Null;
                return false;
            }

            updated = change(value);
            EnsureFits(id, name, updated);
            _values[id] = updated;
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
            if (!ResolveLocked(key, out name, out var id))
            {
                updated = Value.Null;
                return false;
            }

            updated = change(_values[id]);
            EnsureFits(id, name, updated);
            _values[id] = updated;
        }

        Changed?.Invoke(name);
        return true;
    }

    /// <summary>
    /// Writes a name, making it a binding first when it had none. Answers whether the value moved.
    /// </summary>
    private bool WriteLocked(string name, Value value)
    {
        if (!_names.TryGetValue(name, out var id))
        {
            return WriteUnderLocked(name, NewIdLocked(), value);
        }

        var changed = !_values.TryGetValue(id, out var existing) || !existing.Equals(value);
        EnsureFits(id, name, value);
        _values[id] = value;
        return changed;
    }

    private bool WriteUnderLocked(string name, string id, Value value)
    {
        if (!_values.ContainsKey(id) && _names.Count >= Capacity)
        {
            throw new VariableTableFullException(Capacity);
        }

        var changed = !_values.TryGetValue(id, out var existing) || !existing.Equals(value);
        EnsureFits(id, name, value);
        BindLocked(name, id);
        _values[id] = value;
        return changed;
    }

    /// <summary>
    /// The one place a name and an identity are written down together. Every caller holds the lock;
    /// keeping both halves in step here is what makes the rest of the class safe to read.
    /// </summary>
    private void BindLocked(string name, string id)
    {
        if (_bound.TryGetValue(id, out var previous) && !string.Equals(previous, name,
                StringComparison.OrdinalIgnoreCase) && _names.TryGetValue(previous, out var held)
            && held == id)
        {
            _names.Remove(previous);
        }

        if (_names.TryGetValue(name, out var replaced) && replaced != id)
        {
            _bound.Remove(replaced);
        }

        _names[name] = id;
        _bound[id] = name;
    }

    private bool RemoveLocked(string name)
    {
        if (!_names.Remove(name, out var id))
        {
            return false;
        }

        _bound.Remove(id);
        _values.Remove(id);
        _kinds.Remove(id);
        return true;
    }

    /// <summary>The name and identity a key reads as, preferring the identity it carries.</summary>
    private bool ResolveLocked(VariableKey key, out string name, out string id)
    {
        if (key.HasId && _values.ContainsKey(key.Id))
        {
            id = key.Id;
            name = _bound.GetValueOrDefault(id, key.Name);
            return true;
        }

        if (_names.TryGetValue(key.Name, out var bound) && _values.ContainsKey(bound))
        {
            id = bound;
            name = key.Name;
            return true;
        }

        name = key.Name;
        id = string.Empty;
        return false;
    }

    private string NewIdLocked()
    {
        _next++;
        return "~" + _next.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Refuses a value a declared name cannot hold. Nothing declared says nothing; text holds
    /// whatever it is given, because a text variable is how a macro keeps something whose shape it
    /// has not decided. The empty value is always allowed: it is how a result nobody has written
    /// yet reads, and it is not a value of the wrong kind.
    /// </summary>
    private void EnsureFits(string id, string name, Value value)
    {
        if (!_kinds.TryGetValue(id, out var kind))
        {
            return;
        }

        EnsureFits(kind, name, value);
    }

    private static void EnsureFits(ValueKind kind, string name, Value value)
    {
        if (kind is ValueKind.Text || ValueRules.Accepts(kind, value))
        {
            return;
        }

        throw new VariableTypeException(name, kind, value.Kind);
    }
}
