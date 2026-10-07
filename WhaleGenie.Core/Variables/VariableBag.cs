using System;
using System.Collections.Generic;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Variables;

/// <summary>One scope's worth of values, looked up without caring about letter case.</summary>
public sealed class VariableBag : IVariableResolver
{
    private readonly Dictionary<string, Value> _values = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The values in this scope, for the debugger and for tests.</summary>
    public IReadOnlyDictionary<string, Value> Values => _values;

    public bool TryGet(string name, out Value value) => _values.TryGetValue(name, out value!);

    public bool Contains(string name) => _values.ContainsKey(name);

    public void Set(string name, Value value) => _values[name] = value;

    public void SetText(string name, string? text) => Set(name, Value.FromText(text));

    public void SetNumber(string name, double number) => Set(name, Value.FromNumber(number));

    public void SetList(string name, IEnumerable<Value> items) => Set(name, Value.FromList(items));

    public bool Remove(string name) => _values.Remove(name);

    public void Clear() => _values.Clear();
}
