using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.Editing;

/// <summary>
/// The two directions of one row cell: the parts the page draws, and the shape those parts stand
/// for when a command comes back. A part that names a variable is a name; everything else is the
/// text it shows. Nothing is read back from the line: the parts are the value, and a cell the
/// page did not turn into parts is plain text.
/// </summary>
internal static class EditorValueParts
{
    /// <summary>The parts of a shape, or nothing when it is not made of text and names.</summary>
    public static IReadOnlyList<EditorValuePartSnapshot> Of(ExpressionNode? shape)
    {
        if (shape is ConcatNode run)
        {
            return [.. run.Parts.Select(Part)];
        }

        return shape switch
        {
            TextNode { Text.Length: > 0 } text
                => [new EditorValuePartSnapshot("text", text.Text, string.Empty)],
            VariableNode name when name.Name.Trim().Length > 0
                => [new EditorValuePartSnapshot("name", string.Empty, name.Name)],
            _ => [],
        };
    }

    private static EditorValuePartSnapshot Part(ExpressionNode node) => node switch
    {
        TextNode text => new EditorValuePartSnapshot("text", text.Text, string.Empty),
        VariableNode name => new EditorValuePartSnapshot("name", string.Empty, name.Name),
        _ => new EditorValuePartSnapshot("text", Expression.Render(node), string.Empty),
    };

    /// <summary>
    /// The shape a row cell's parts stand for. The parts are the value the page sent, so a name
    /// stays a name and the pieces of a run stay the run they were; a cell with no parts is the
    /// text the input held.
    /// </summary>
    public static ExpressionNode ShapeOf(IReadOnlyList<EditorValuePartSnapshot>? parts)
        => parts is not { Count: > 0 }
            ? Formula.Nothing
            : Formula.Run(parts.Select(part => part.Kind == "name" && part.Name.Trim().Length > 0
                ? Formula.Name(part.Name.Trim())
                : Formula.Text(part.Text)));
}
