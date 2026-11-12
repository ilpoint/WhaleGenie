using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Variables;

/// <summary>
/// A variable as a run reads it: the name it is written under and the identity it is bound to.
/// The two travel together because passing them as two strings is the one place they could be
/// swapped without the compiler noticing. An empty <paramref name="Id"/> means the name itself is
/// the identity — a system value, a script's own name, or a value a run worked out.
/// </summary>
public readonly record struct VariableKey(string Name, string Id = "")
{
    public bool HasId => Id.Length > 0;

    /// <summary>A key for a name nothing declared.</summary>
    public static VariableKey Of(string name) => new(name);

    /// <summary>The key a shape names, or an empty one when the shape names nothing.</summary>
    public static VariableKey From(ExpressionNode? node) => node switch
    {
        VariableNode variable => new VariableKey(variable.Name, variable.Id),
        TextNode text => new VariableKey(text.Text.Trim()),
        _ => new VariableKey(string.Empty),
    };
}
