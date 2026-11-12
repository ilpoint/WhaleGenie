using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Variables;

/// <summary>
/// Two declarations of this run want one name. A name holds one value, so one of them would read
/// what the other wrote; the write is refused instead of letting that happen in silence.
/// </summary>
public sealed class VariableConflictException(string name, string declaredId)
    : System.Exception($"\"{name}\" is already held by another declaration")
{
    /// <summary>The name both declarations want.</summary>
    public string Name { get; } = name;

    /// <summary>The identity already holding it, for a message that can be followed up.</summary>
    public string DeclaredId { get; } = declaredId;
}

/// <summary>
/// A value that a declared name cannot hold: a sentence written into a number, a list into a flag.
/// The write is refused where it happens, so the failure names the step that made it rather than
/// surfacing later at whatever tried to read a number and found words.
/// </summary>
public sealed class VariableTypeException(string name, ValueKind expected, ValueKind actual)
    : System.Exception($"\"{name}\" holds {expected}, not {actual}")
{
    public string Name { get; } = name;

    public ValueKind Expected { get; } = expected;

    public ValueKind Actual { get; } = actual;
}
