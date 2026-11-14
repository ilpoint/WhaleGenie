using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Variables;

/// <summary>
/// One scope's worth of values, held under the names the macro reads them by. A name is the whole
/// key: what a run writes is what the line that reads it says, so renaming a declaration renames
/// every reference and nothing else has to be kept in step.
///
/// <para>
/// A declaration is metadata on the name — what kind the value has to be — rather than a second
/// identity beside it. Declaring a name that already holds a value keeps that value while it still
/// fits the kind, and puts the kind's empty value there when it does not.
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
    /// <summary>What one name holds, and the kind it was declared as when it was declared at all.</summary>
    private sealed class Entry
    {
        public Value Value = Value.Null;

        /// <summary>Null for a name a step wrote without declaring it: anything may be put there.</summary>
        public ValueKind? Kind;

        /// <summary>
        /// True once something has written this name. A declaration on its own is not a value, and
        /// neither is the empty value a kind starts at, so seeding can tell "nothing here yet" from
        /// "a run wrote nothing here" — the second one is an answer and is not put back.
        /// </summary>
        public bool Written;
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _gate = new();

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
                var copy = new Dictionary<string, Value>(_entries.Count,
                    StringComparer.OrdinalIgnoreCase);
                foreach (var (name, entry) in _entries)
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
                return _entries.Count;
            }
        }
    }

    public bool TryGet(string name, out Value value)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(name, out var entry))
            {
                value = entry.Value;
                return true;
            }
        }

        value = Value.Null;
        return false;
    }

    public bool Contains(string name)
    {
        lock (_gate)
        {
            return _entries.ContainsKey(name);
        }
    }

    public void Set(string name, Value value)
    {
        var changed = false;
        lock (_gate)
        {
            var entry = RequireLocked(name);
            EnsureFits(entry.Kind, name, value);
            changed = !entry.Value.Equals(value);
            entry.Value = value;
            entry.Written = true;
        }

        if (changed)
        {
            Changed?.Invoke(name);
        }
    }

    /// <summary>
    /// Writes a value only when nothing has been written here yet. Seeding a run uses this so that
    /// a value another run has already written is not put back to its declared starting value; a
    /// declaration on its own does not count as a value, which is what lets a global be declared
    /// and then filled in.
    /// </summary>
    public bool SetIfAbsent(string name, Value value)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(name, out var existing) && existing.Written)
            {
                return false;
            }

            var entry = existing ?? RequireLocked(name);
            EnsureFits(entry.Kind, name, value);
            entry.Value = value;
            entry.Written = true;
        }

        Changed?.Invoke(name);
        return true;
    }

    public void SetText(string name, string? text) => Set(name, Value.FromText(text));

    public void SetNumber(string name, double number) => Set(name, Value.FromNumber(number));

    public void SetList(string name, IEnumerable<Value> items) => Set(name, Value.FromList(items));

    /// <summary>
    /// Says a name holds a value of this kind for the rest of the run. A value already there is
    /// kept while it can stand for the new kind, and the kind's empty value takes its place when it
    /// cannot, so a declaration that changed cannot leave a number in a list or a line in a flag.
    /// Declaring a name nothing has written yet leaves it empty: the declaration says what may be
    /// written, not what has been.
    /// </summary>
    public void Declare(string name, ValueKind kind)
    {
        var written = name.Trim();
        if (written.Length == 0)
        {
            return;
        }

        var changed = false;
        lock (_gate)
        {
            if (_entries.TryGetValue(written, out var entry))
            {
                if (entry.Kind == kind)
                {
                    return;
                }

                entry.Kind = kind;

                // What the name already holds is kept when it can stand for the new kind — a line
                // that reads as a number is still a number — and replaced when it cannot. A value
                // that was replaced is no value of this name any more, so a declared starting value
                // may be written again.
                if (!ValueRules.Accepts(kind, entry.Value))
                {
                    entry.Value = ValueRules.Empty(kind);
                    entry.Written = false;
                }
            }
            else
            {
                entry = RequireLocked(written);
                entry.Kind = kind;
            }

            changed = true;
        }

        if (changed)
        {
            Changed?.Invoke(written);
        }
    }

    public bool Remove(string name)
    {
        bool removed;
        lock (_gate)
        {
            removed = _entries.Remove(name);
        }

        if (removed)
        {
            Changed?.Invoke(name);
        }

        return removed;
    }

    /// <summary>
    /// Empties a name. A declared one goes back to the empty value of its kind and keeps the
    /// declaration — a name the macro declared is still a name it has; one nothing declared is
    /// taken out of the table, so reading it afterwards fails the way reading a name no run ever
    /// wrote does.
    /// </summary>
    public bool Clear(string name)
    {
        bool emptied;
        lock (_gate)
        {
            if (!_entries.TryGetValue(name, out var entry))
            {
                return false;
            }

            if (entry.Kind is { } kind)
            {
                entry.Value = ValueRules.Empty(kind);
                entry.Written = true;
                emptied = true;
            }
            else
            {
                emptied = _entries.Remove(name);
            }
        }

        if (emptied)
        {
            Changed?.Invoke(name);
        }

        return emptied;
    }

    /// <summary>
    /// Moves one name to another. The value and the declaration travel together, so a reference
    /// that was reading the old name has only to be written as the new one.
    /// </summary>
    public bool Rename(string oldName, string newName)
    {
        var next = newName.Trim();
        if (next.Length == 0 || string.Equals(oldName, next, StringComparison.Ordinal))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_entries.Remove(oldName, out var entry) || _entries.ContainsKey(next))
            {
                if (entry is not null)
                {
                    _entries[oldName] = entry;
                }

                return false;
            }

            _entries[next] = entry;
        }

        Changed?.Invoke(next);
        Changed?.Invoke(oldName);
        return true;
    }

    /// <summary>True when this name was declared — by the macro, this run — as holding one kind.</summary>
    public bool IsDeclared(string name)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(name, out var entry) && entry.Kind is not null;
        }
    }

    /// <summary>Empties the scope. A value nobody can name is not worth keeping.</summary>
    public void Clear()
    {
        string[] names;
        lock (_gate)
        {
            names = [.. _entries.Keys];
            _entries.Clear();
        }

        foreach (var name in names)
        {
            Changed?.Invoke(name);
        }
    }

    /// <summary>
    /// Forgets which names were declared. A declaration belongs to the macro that wrote it, so a
    /// table the debug window keeps between runs does not go on enforcing last run's kinds.
    /// </summary>
    public void ResetDeclarations()
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Kind = null;
            }
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
            if (!_entries.TryGetValue(name, out var entry))
            {
                sum = 0;
                return false;
            }

            sum = entry.Value.AsNumber() + amount;
            EnsureFits(entry.Kind, name, Value.FromNumber(sum));
            entry.Value = Value.FromNumber(sum);
            entry.Written = true;
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
            if (!_entries.TryGetValue(name, out var entry))
            {
                updated = Value.Null;
                return false;
            }

            updated = change(entry.Value);
            EnsureFits(entry.Kind, name, updated);
            entry.Value = updated;
            entry.Written = true;
            found = true;
        }

        if (found)
        {
            Changed?.Invoke(name);
        }

        return found;
    }

    /// <summary>
    /// Takes down what a name holds now so a block can put it back when it is over. This is how a
    /// loop gives its counter a name without taking away whatever that name meant before: the name
    /// is still the key, the block merely borrows it.
    /// </summary>
    public VariableMark Mark(string name)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(name, out var entry))
            {
                return new VariableMark(existed: true, entry.Value, entry.Kind, entry.Written);
            }

            // The block writes this name in every round, so it is made here rather than at the
            // first write: the ceiling counts it from the moment the block starts.
            RequireLocked(name);
            return new VariableMark(existed: false, Value.Null, kind: null, written: false);
        }
    }

    /// <summary>
    /// Puts a name back the way <see cref="Mark"/> found it. A name the block introduced is taken
    /// out again, value and declaration together.
    /// </summary>
    public void Restore(string name, VariableMark mark)
    {
        lock (_gate)
        {
            // Putting back what was already there never needs room: the name held it a moment ago.
            if (!mark.Existed)
            {
                _entries.Remove(name);
            }
            else
            {
                _entries[name] = new Entry
                {
                    Value = mark.Value,
                    Kind = mark.Kind,
                    Written = mark.Written,
                };
            }
        }

        Changed?.Invoke(name);
    }

    /// <summary>
    /// The entry a name has, making one when the scope still has room. Every way of putting a name
    /// into the table comes through here, so the ceiling is one rule rather than one per caller.
    /// </summary>
    private Entry RequireLocked(string name)
    {
        if (_entries.TryGetValue(name, out var entry))
        {
            return entry;
        }

        if (_entries.Count >= Capacity)
        {
            throw new VariableTableFullException(Capacity);
        }

        return _entries[name] = new Entry();
    }

    /// <summary>
    /// Refuses a value a declared name cannot hold. Nothing declared says nothing; text holds
    /// whatever it is given, because a text variable is how a macro keeps something whose shape it
    /// has not decided. The empty value is always allowed: it is how a result nobody has written
    /// yet reads, and it is not a value of the wrong kind.
    /// </summary>
    private static void EnsureFits(ValueKind? kind, string name, Value value)
    {
        if (kind is not { } declared)
        {
            return;
        }

        if (declared is ValueKind.Text || ValueRules.Accepts(declared, value))
        {
            return;
        }

        throw new VariableTypeException(name, declared, value.Kind);
    }
}

/// <summary>
/// What a name held when a block borrowed it. A block that introduces a name leaves it behind when
/// it ends; one that borrowed a name the macro already had puts the value and the declaration back.
/// </summary>
public readonly struct VariableMark
{
    internal VariableMark(bool existed, Value value, ValueKind? kind, bool written)
    {
        Existed = existed;
        Value = value;
        Kind = kind;
        Written = written;
    }

    /// <summary>True when the name held something before the block borrowed it.</summary>
    public bool Existed { get; }

    internal Value Value { get; }

    internal ValueKind? Kind { get; }

    internal bool Written { get; }
}
