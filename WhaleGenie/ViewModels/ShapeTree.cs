using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Changing one place in a value's shape. A place is the walk from the root to the piece, so every
/// edit is a copy of the path down to it with that one piece replaced; the rest of the tree is
/// shared and nothing has to be written out as a line and read back. There is no text here — the
/// tree a field keeps is the tree the editor changes.
/// </summary>
internal static class ShapeTree
{
    /// <summary>The shape at this place, or null when the walk does not reach one.</summary>
    public static ExpressionNode? At(ExpressionNode root, NodePath path)
    {
        if (path.IsRoot)
        {
            return root;
        }

        var head = Head(path);
        var rest = Tail(path);
        var child = root switch
        {
            ConcatNode run => Index(run.Parts, head),
            ListNode list => Index(list.Items, head),
            CallNode call => Index(call.Arguments, head),
            BinaryNode binary when head == 0 => binary.Left,
            BinaryNode binary when head == 1 => binary.Right,
            UnaryNode unary when head == 0 => unary.Operand,
            _ => null,
        };

        return child is null ? null : At(child, rest);
    }

    /// <summary>Puts a shape where this place stands, growing a run or a call when asked to.</summary>
    public static ExpressionNode Set(ExpressionNode root, NodePath path, ExpressionNode value)
    {
        if (path.IsRoot)
        {
            return value;
        }

        var head = Head(path);
        var rest = Tail(path);
        return root switch
        {
            ConcatNode run => Formula.Run(Replace(run.Parts, head,
                piece => Set(piece, rest, value), value)),
            ListNode list => new ListNode(Replace(list.Items, head,
                piece => Set(piece, rest, value), value)),
            CallNode call => call with
            {
                Arguments = Replace(call.Arguments, head,
                    piece => Set(piece, rest, value), value),
            },
            BinaryNode binary => head == 1
                ? binary with { Right = Set(binary.Right, rest, value) }
                : binary with { Left = Set(binary.Left, rest, value) },
            UnaryNode unary => unary with { Operand = Set(unary.Operand, rest, value) },
            _ => root,
        };
    }

    /// <summary>Adds a piece beside this place, keeping what was already there.</summary>
    public static ExpressionNode Insert(ExpressionNode root, NodePath path, ExpressionNode value)
    {
        if (path.IsRoot)
        {
            return Formula.Run([root, value]);
        }

        var head = Head(path);
        var rest = Tail(path);
        return root switch
        {
            ConcatNode run => Formula.Run(InsertAt(run.Parts, head,
                piece => Insert(piece, rest, value), value)),
            ListNode list => new ListNode(InsertAt(list.Items, head,
                piece => Insert(piece, rest, value), value)),
            CallNode call => call with
            {
                Arguments = InsertAt(call.Arguments, head,
                    piece => Insert(piece, rest, value), value),
            },
            _ => Formula.Run([root, value]),
        };
    }

    /// <summary>
    /// Leaves a place empty rather than pulling the tree apart: the hole is drawn as a box and
    /// makes the value unreadable until something is put there, which is what an unfilled place is.
    /// </summary>
    public static ExpressionNode Remove(ExpressionNode root, NodePath path)
        => Set(root, path, Formula.Hole);

    private static int Head(NodePath path) => path.Indices[0];

    private static NodePath Tail(NodePath path)
        => new([.. path.Indices.Skip(1)]);

    private static ExpressionNode? Index(IReadOnlyList<ExpressionNode> items, int at)
        => at >= 0 && at < items.Count ? items[at] : null;

    private static IReadOnlyList<ExpressionNode> Replace(IReadOnlyList<ExpressionNode> items,
        int at, System.Func<ExpressionNode, ExpressionNode> change, ExpressionNode value)
    {
        var next = items.ToList();
        if (at >= 0 && at < next.Count)
        {
            next[at] = change(next[at]);
        }
        else
        {
            next.Add(value);
        }

        return next;
    }

    private static IReadOnlyList<ExpressionNode> InsertAt(IReadOnlyList<ExpressionNode> items,
        int at, System.Func<ExpressionNode, ExpressionNode> change, ExpressionNode value)
    {
        var next = items.ToList();
        if (at >= 0 && at < next.Count)
        {
            next.Insert(at, value);
        }
        else if (at > 0 && at - 1 < next.Count)
        {
            next[at - 1] = change(next[at - 1]);
        }
        else
        {
            next.Add(value);
        }

        return next;
    }
}
