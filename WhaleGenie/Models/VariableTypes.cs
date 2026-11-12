using System.Collections.Generic;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Models;

/// <summary>
/// The spelling a <see cref="ValueKind"/> goes by in a file, a dropdown and a word for the reader.
/// A kind is the value's own name; this is only how it is written down, and it is the one place
/// that spelling lives.
/// </summary>
public static class VariableTypes
{
    /// <summary>
    /// The kinds a variable can be declared as, in the order the dropdown shows them. A null is not
    /// a declaration: it is what "nothing was written yet" reads as.
    /// </summary>
    public static IReadOnlyList<ValueKind> Declarable { get; } =
    [
        ValueKind.Text,
        ValueKind.Number,
        ValueKind.Bool,
        ValueKind.List,
        ValueKind.Record,
        ValueKind.Date,
        ValueKind.Image,
    ];

    /// <summary>The word a kind is written as.</summary>
    public static string Name(ValueKind kind) => kind switch
    {
        ValueKind.Number => "number",
        ValueKind.Bool => "bool",
        ValueKind.List => "list",
        ValueKind.Record => "record",
        ValueKind.Date => "date",
        ValueKind.Image => "image",
        _ => "text",
    };

    /// <summary>
    /// The kind a word stands for. A colour was a declared type once: it is text with a picker, and
    /// a value kept by that build still opens as the text it always was.
    /// </summary>
    public static ValueKind KindOf(string? name)
    {
        var written = name?.Trim();
        if (string.IsNullOrEmpty(written))
        {
            return ValueKind.Text;
        }

        // The file spells a kind the way the enum does ("Number"); the table and the dropdown
        // spell it in lower case. Both mean the same kind, and a name nothing knows is text.
        return System.Enum.TryParse<ValueKind>(written, ignoreCase: true, out var kind)
            && kind is not ValueKind.Null
            && System.Enum.IsDefined(kind)
                ? kind
                : ValueKind.Text;
    }
}
