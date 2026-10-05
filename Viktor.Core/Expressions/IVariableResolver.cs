namespace Viktor.Core.Expressions;

/// <summary>Looks up the value an expression names with <c>$name</c>.</summary>
public interface IVariableResolver
{
    bool TryGet(string name, out Value value);
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
