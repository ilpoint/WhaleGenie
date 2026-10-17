using System;
using System.Collections.Generic;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Variables;

/// <summary>
/// One scope's worth of values, looked up without caring about letter case. A global bag is read
/// and written by every run at once, so each operation takes the bag's own lock; what the lock
/// does not make atomic is a read followed by a write, which is why the one operation that has to
/// be a single step — adding to a number — is offered as <see cref="TryAddNumber"/>.
/// </summary>
public sealed class VariableBag : IVariableResolver
{
    private readonly Dictionary<string, Value> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

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
                return new Dictionary<string, Value>(_values, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public bool TryGet(string name, out Value value)
    {
        lock (_gate)
        {
            return _values.TryGetValue(name, out value!);
        }
    }

    public bool Contains(string name)
    {
        lock (_gate)
        {
            return _values.ContainsKey(name);
        }
    }

    public void Set(string name, Value value)
    {
        lock (_gate)
        {
            _values[name] = value;
        }
    }

    /// <summary>
    /// Writes a value only when the name is not there yet. Seeding a run uses this so that a value
    /// another run has already written is not put back to its declared starting value.
    /// </summary>
    public bool SetIfAbsent(string name, Value value)
    {
        lock (_gate)
        {
            return _values.TryAdd(name, value);
        }
    }

    public void SetText(string name, string? text) => Set(name, Value.FromText(text));

    public void SetNumber(string name, double number) => Set(name, Value.FromNumber(number));

    public void SetList(string name, IEnumerable<Value> items) => Set(name, Value.FromList(items));

    public bool Remove(string name)
    {
        lock (_gate)
        {
            return _values.Remove(name);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _values.Clear();
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
            if (!_values.TryGetValue(name, out var value))
            {
                sum = 0;
                return false;
            }

            sum = value.AsNumber() + amount;
            _values[name] = Value.FromNumber(sum);
            return true;
        }
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
        lock (_gate)
        {
            if (!_values.TryGetValue(name, out var current))
            {
                updated = Value.Null;
                return false;
            }

            updated = change(current);
            _values[name] = updated;
            return true;
        }
    }
}
