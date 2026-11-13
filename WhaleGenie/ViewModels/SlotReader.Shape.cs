using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

// Reading a shape into those pieces. The other half of this class still reads a line of text;
// this is the way a field that keeps a shape is drawn, and it never writes or reads a line.
internal sealed partial class SlotReader
{
    /// <summary>
    /// Draws a value's shape as the run of pieces the band shows. Each piece knows the shape it
    /// stands for and the walk from the root to it, so the band can put a caret, a cross or a drop
    /// on a place without measuring characters in a line. Nothing here spells a name with a sign:
    /// a name is a word, and the shape already says which pieces are names.
    /// </summary>
    public static SlotNode ReadShape(ExpressionNode value,
        IReadOnlyDictionary<string, ExpressionFunction> functions)
    {
        var root = Piece(value, NodePath.Root, functions);

        foreach (var piece in root.Walk())
        {
            if (piece.Source is not null && piece is not SlotEmpty)
            {
                piece.CanRemove = true;
            }
        }

        return root;
    }

    private static SlotNode Piece(ExpressionNode value, NodePath path,
        IReadOnlyDictionary<string, ExpressionFunction> functions) => value switch
        {
            HoleNode => Empty(path, Strings.Get("ValuePicker.Hole")),
            TextNode { Text: "" } => Empty(path, Strings.Get("ValuePicker.Hole")),
            TextNode text => Leaf(path, value, text.Text, "maths", isName: false),
            NumberNode number => Leaf(path, value, number.Text, "maths", isName: false),
            DateNode date => Leaf(path, value, date.Text, "maths", isName: false),
            ConstantNode constant => Leaf(path, value, constant.Word, "other", isName: false),
            VariableNode variable => Leaf(path, value, variable.Name, "lists", isName: true),
            ConcatNode run => Sequence(run.Parts, path, functions),
            ListNode list => Sequence(list.Items, path, functions),
            UnaryNode unary => new SlotSequence
            {
                Items =
                [
                    new SlotOperator { Text = unary.Operator, Path = path },
                Piece(unary.Operand, path.Append(0), functions),
            ],
                Path = path,
                Source = value,
            },
            BinaryNode binary => new SlotSequence
            {
                Items =
                [
                    Piece(binary.Left, path.Append(0), functions),
                new SlotOperator { Text = binary.Operator, Path = path },
                Piece(binary.Right, path.Append(1), functions),
            ],
                Path = path,
                Source = value,
            },
            CallNode call => Call(call, path, functions),
            _ => Empty(path, Strings.Get("ValuePicker.Hole")),
        };

    private static SlotSequence Sequence(IReadOnlyList<ExpressionNode> parts, NodePath path,
        IReadOnlyDictionary<string, ExpressionFunction> functions)
        => new()
        {
            Items = [.. parts.Select((part, index) => Piece(part, path.Append(index), functions))],
            Path = path,
        };

    private static SlotNode Call(CallNode call, NodePath path,
        IReadOnlyDictionary<string, ExpressionFunction> functions)
    {
        functions.TryGetValue(call.Name, out var entry);
        var arguments = new List<SlotSequence>();
        for (var index = 0; index < call.Arguments.Count; index++)
        {
            var argument = new SlotSequence
            {
                Items = [Piece(call.Arguments[index], path.Append(index), functions)],
                Path = path.Append(index),
            };
            argument.Before = index > 0 ? ", " : string.Empty;
            argument.ArgumentIndex = index + 1;
            argument.ArgumentLabel = entry is not null && index < entry.Parameters.Count
                ? ExpressionText.ParameterLabel(call.Name, entry.Parameters[index])
                : Strings.Format("ValuePicker.Arg.Ordinal", index + 1);
            argument.ArgumentRequired = entry is not null && index < entry.MinArguments;
            argument.ArgumentOptional = entry is not null && index >= entry.MinArguments;
            argument.ArgumentFlag = entry is null
                ? string.Empty
                : argument.ArgumentRequired
                    ? Strings.Get("ValuePicker.Arg.Required")
                    : Strings.Get("ValuePicker.Arg.Optional");
            arguments.Add(argument);
        }

        var ghost = entry is not null && arguments.Count >= entry.MaxArguments
            ? null
            : new SlotEmpty
            {
                Text = Strings.Get("ValuePicker.Slot.Add"),
                Hint = entry is not null && arguments.Count < entry.Parameters.Count
                    ? ExpressionText.ParameterLabel(call.Name, entry.Parameters[arguments.Count])
                    : Strings.Get("ValuePicker.Slot.AddHint"),
                Before = arguments.Count == 0 ? string.Empty : ", ",
                IsGhost = true,
                Required = entry is null ? arguments.Count == 0 : arguments.Count < entry.MinArguments,
                Path = path.Append(arguments.Count),
            };

        return new SlotCall
        {
            Name = call.Name,
            Tone = entry?.Group ?? "other",
            Arguments = arguments,
            Ghost = ghost,
            Path = path,
            Source = call,
        };
    }

    private static SlotValue Leaf(NodePath path, ExpressionNode source, string text, string tone,
        bool isName)
        => new()
        {
            Text = text,
            Tone = tone,
            IsName = isName,
            Path = path,
            Source = source,
        };

    private static SlotEmpty Empty(NodePath path, string text)
        => new() { Text = text, Hint = text, Required = true, Path = path };
}
