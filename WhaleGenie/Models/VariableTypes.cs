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
    /// The kind a word stands for, read from the words this program writes. The list is explicit
    /// rather than a parse of the enum: a numeric spelling must not quietly become a kind, and a
    /// word nothing knows is text — the kind every value can be read as.
    /// </summary>
    public static ValueKind KindOf(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "number" => ValueKind.Number,
        "bool" => ValueKind.Bool,
        "list" => ValueKind.List,
        "record" => ValueKind.Record,
        "date" => ValueKind.Date,
        "image" => ValueKind.Image,
        _ => ValueKind.Text,
    };
}
