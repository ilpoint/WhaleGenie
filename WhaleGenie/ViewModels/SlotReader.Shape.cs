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
    /// stands for, the walk from the root to it, and where it stands in the drawn run. The band's
    /// caret, cross and drop all name a place by that walk; the offsets are only a stable order for
    /// the geometry to measure, which is why they are derived here rather than read out of a line.
    /// Nothing here spells a name with a sign: a name is a word, and the shape already says which
    /// pieces are names.
    /// </summary>
    /// <summary>The drawn shape and one past the last place it stands at.</summary>
    public static (SlotNode Node, int End) Shape(ExpressionNode value,
        IReadOnlyDictionary<string, ExpressionFunction> functions, bool bare = false)
    {
        var at = 0;
        var root = Piece(value, NodePath.Root, functions, bare, ref at);

        foreach (var drawn in root.Walk())
        {
            if (drawn.Source is not null && drawn is not SlotEmpty)
            {
                drawn.CanRemove = true;
            }
        }

        return (root, at);
    }

    private static SlotNode Piece(ExpressionNode value, NodePath path,
        IReadOnlyDictionary<string, ExpressionFunction> functions, bool bare, ref int at)
    {
        var start = at;
        var node = value switch
        {
            HoleNode or TextNode { Text: "" } => Empty(path, Strings.Get("ValuePicker.Hole"), at),
            TextNode text => Leaf(path, value, text.Text, "maths", false, bare, ref at),
            NumberNode number => Leaf(path, value, number.Text, "maths", false, bare, ref at),
            DateNode date => Leaf(path, value, date.Text, "maths", false, bare, ref at),
            ConstantNode constant => Leaf(path, value, constant.Word, "other", false, bare, ref at),
            VariableNode variable => Leaf(path, value, variable.Name, "lists", true, bare, ref at),
            ConcatNode run => Sequence(run.Parts, path, functions, bare, ref at),
            ListNode list => Sequence(list.Items, path, functions, bare, ref at),
            UnaryNode unary => Unary(unary, path, functions, bare, ref at),
            BinaryNode binary => Binary(binary, path, functions, bare, ref at),
            CallNode call => Call(call, path, functions, bare, ref at),
            _ => Empty(path, Strings.Get("ValuePicker.Hole"), at),
        };

        return Containers(node, start, at);
    }

    /// <summary>A container spans the pieces drawn inside it, so a click on its box has an edge.</summary>
    private static SlotNode Containers(SlotNode node, int start, int at)
    {
        if (node is SlotSequence or SlotCall)
        {
            node.Start = start;
            node.Length = at - start;
        }

        return node;
    }

    private static SlotNode Sequence(IReadOnlyList<ExpressionNode> parts, NodePath path,
        IReadOnlyDictionary<string, ExpressionFunction> functions, bool bare, ref int at)
    {
        var start = at;
        var items = new List<SlotNode>(parts.Count);
        for (var index = 0; index < parts.Count; index++)
        {
            items.Add(Piece(parts[index], path.Append(index), functions, bare, ref at));
        }

        return new SlotSequence
        {
            Items = items,
            Path = path,
            Start = start,
            Length = at - start,
        };
    }

    private static SlotNode Unary(UnaryNode unary, NodePath path,
        IReadOnlyDictionary<string, ExpressionFunction> functions, bool bare, ref int at)
    {
        var sign = new SlotOperator
        {
            Text = unary.Operator,
            Path = path,
            Start = at,
            Length = unary.Operator.Length,
        };
        at += unary.Operator.Length;
        var operand = Piece(unary.Operand, path.Append(0), functions, bare, ref at);
        return new SlotSequence { Items = [sign, operand], Path = path, Source = unary };
    }

    private static SlotNode Binary(BinaryNode binary, NodePath path,
        IReadOnlyDictionary<string, ExpressionFunction> functions, bool bare, ref int at)
    {
        var left = Piece(binary.Left, path.Append(0), functions, bare, ref at);
        var sign = new SlotOperator
        {
            Text = binary.Operator,
            Path = path,
            Start = at,
            Length = binary.Operator.Length,
        };
        at += binary.Operator.Length;
        var right = Piece(binary.Right, path.Append(1), functions, bare, ref at);
        return new SlotSequence
        {
            Items = [left, sign, right],
            Path = path,
            Source = binary,
        };
    }

    private static SlotNode Call(CallNode call, NodePath path,
        IReadOnlyDictionary<string, ExpressionFunction> functions, bool bare, ref int at)
    {
        functions.TryGetValue(call.Name, out var entry);

        // The name and the opening bracket are drawn even when the call has no arguments.
        at += call.Name.Length + 1;
        var arguments = new List<SlotSequence>();
        for (var index = 0; index < call.Arguments.Count; index++)
        {
            if (index > 0)
            {
                at += 2;
            }

            var value = Piece(call.Arguments[index], path.Append(index), functions, bare, ref at);
            var argument = new SlotSequence
            {
                Items = [value],
                Path = path.Append(index),
                Start = value.Start,
                Length = value.Length,
                Before = index > 0 ? ", " : string.Empty,
                ArgumentIndex = index + 1,
                ArgumentLabel = entry is not null && index < entry.Parameters.Count
                    ? ExpressionText.ParameterLabel(call.Name, entry.Parameters[index])
                    : Strings.Format("ValuePicker.Arg.Ordinal", index + 1),
                ArgumentRequired = entry is not null && index < entry.MinArguments,
                ArgumentOptional = entry is not null && index >= entry.MinArguments,
            };
            argument.ArgumentFlag = entry is null
                ? string.Empty
                : argument.ArgumentRequired
                    ? Strings.Get("ValuePicker.Arg.Required")
                    : Strings.Get("ValuePicker.Arg.Optional");
            arguments.Add(argument);
        }

        at++;
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
                Start = at,
                Length = 0,
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

    private static SlotNode Leaf(NodePath path, ExpressionNode source, string text, string tone,
        bool isName, bool bare, ref int at)
    {
        var length = Expression.Render(source, bare: bare).Length;
        var piece = new SlotValue
        {
            Text = text,
            Tone = tone,
            IsName = isName,
            Path = path,
            Source = source,
            Start = at,
            Length = length,
        };
        at += length;
        return piece;
    }

    private static SlotNode Empty(NodePath path, string text, int at)
        => new SlotEmpty
        {
            Text = text,
            Hint = text,
            Required = true,
            Path = path,
            Start = at,
            Length = 0,
        };
}
