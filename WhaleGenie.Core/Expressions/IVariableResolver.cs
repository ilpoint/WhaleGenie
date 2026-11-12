using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Expressions;

/// <summary>Looks up the value an expression names with <c>$name</c>.</summary>
public interface IVariableResolver
{
    bool TryGet(string name, out Value value);

    /// <summary>
    /// Reads a name by identity when it has one. A resolver that knows nothing about identities
    /// answers by the name it carries, which is what a test's own resolver wants.
    /// </summary>
    bool TryGet(VariableKey key, out Value value) => TryGet(key.Name, out value);
}

/// <summary>A resolver that knows nothing, used when an expression has no variables.</summary>
public sealed class EmptyVariableResolver : IVariableResolver
{
    public static EmptyVariableResolver Instance { get; } = new();

    public bool TryGet(string name, out Value value)
    {
        value = Value.Null;
        return false;
    }
}
