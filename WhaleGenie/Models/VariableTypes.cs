using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Models;

/// <summary>
/// What a declared type means to a running table. The editor offers words — text, number, bool,
/// colour, date, list — and the engine holds kinds. This is the one place the two are put beside
/// each other, so a variable's dropdown and the write it refuses cannot drift apart.
/// </summary>
public static class VariableTypes
{
    /// <summary>
    /// The kind a declaration of this type holds. Text and colour are text: a colour is a way of
    /// writing one, not a value of its own. An unknown word is text too, which is what the editor
    /// falls back to when a file names a type this build does not know.
    /// </summary>
    public static ValueKind KindOf(string? type) => type switch
    {
        "number" => ValueKind.Number,
        "bool" => ValueKind.Bool,
        "list" => ValueKind.List,
        _ => ValueKind.Text,
    };
}
