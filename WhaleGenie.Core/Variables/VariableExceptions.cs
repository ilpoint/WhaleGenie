using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Core.Variables;

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

/// <summary>
/// The table a run owns has as many names as it may hold. A name built while a macro runs is kept
/// until the run ends, so a loop that writes a new one every round can reach this; the run says so
/// rather than growing until the machine gives up.
/// </summary>
public sealed class VariableTableFullException(int capacity)
    : System.Exception($"the run's variables are already {capacity} names")
{
    public int Capacity { get; } = capacity;
}
