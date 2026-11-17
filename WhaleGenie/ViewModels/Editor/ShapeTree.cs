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
            GroupNode group when head == 0 => group.Inner,
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
            GroupNode group when head == 0 => group with { Inner = Set(group.Inner, rest, value) },
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
            ConcatNode run => Formula.Run(InsertAt(run.Parts, head, rest, value)),
            ListNode list => new ListNode(InsertAt(list.Items, head, rest, value)),
            CallNode call => call with
            {
                Arguments = InsertAt(call.Arguments, head, rest, value),
            },
            // A call inside a bracket group, a binary or a unary is still the same call: a place
            // in its arguments must be reached through the container, not treated as the root.
            GroupNode group when !rest.IsRoot && head == 0
                => group with { Inner = Insert(group.Inner, rest, value) },
            BinaryNode binary when !rest.IsRoot && head == 0
                => binary with { Left = Insert(binary.Left, rest, value) },
            BinaryNode binary when !rest.IsRoot && head == 1
                => binary with { Right = Insert(binary.Right, rest, value) },
            UnaryNode unary when !rest.IsRoot && head == 0
                => unary with { Operand = Insert(unary.Operand, rest, value) },
            _ => Formula.Run([root, value]),
        };
    }

    /// <summary>
    /// Leaves a place empty rather than pulling the tree apart: the hole is drawn as a box and
    /// makes the value unreadable until something is put there, which is what an unfilled place is.
    /// Used while a piece is being lifted for a drag, where the rest of the tree has to keep its
    /// paths until the piece lands.
    /// </summary>
    public static ExpressionNode Remove(ExpressionNode root, NodePath path)
        => Set(root, path, Formula.Hole);

    /// <summary>
    /// Takes a piece out and closes the gap it left. This is the operation behind the cross and
    /// the keyboard delete keys: a text run joins the pieces on either side, a list loses one item,
    /// and a parent that cannot stand without its child is simplified. Only an inserted operator
    /// or call leaves a place to fill; deleting something never leaves one.
    /// </summary>
    public static ExpressionNode Cut(ExpressionNode root, NodePath path)
    {
        if (path.IsRoot)
        {
            return Formula.Nothing;
        }

        return CutAt(root, path) ?? root;
    }

    /// <summary>
    /// Puts a sign after the shape at this place, with an empty place on its right. Where a line
    /// editor would append a character and let a parser work out the tree, a shape editor grows the
    /// tree here: what stood at the place becomes the left side, and the right side waits to be
    /// filled. The caller moves its caret to <c>path.Append(1)</c> to fill it.
    /// </summary>
    public static ExpressionNode Surround(ExpressionNode root, NodePath path, string op)
    {
        var left = At(root, path) ?? Formula.Hole;
        return Set(root, path, Formula.Binary(op, left, Formula.Hole));
    }

    private static ExpressionNode? CutAt(ExpressionNode root, NodePath path)
    {
        var head = Head(path);
        var rest = Tail(path);
        switch (root)
        {
            case ConcatNode run when head >= 0 && head < run.Parts.Count:
                {
                    var parts = run.Parts.ToList();
                    if (rest.IsRoot)
                    {
                        parts.RemoveAt(head);
                    }
                    else if (CutAt(parts[head], rest) is { } changed)
                    {
                        parts[head] = changed;
                    }
                    else
                    {
                        return null;
                    }

                    return MergeText(parts);
                }
            case ListNode list when head >= 0 && head < list.Items.Count:
                {
                    var items = list.Items.ToList();
                    if (rest.IsRoot)
                    {
                        items.RemoveAt(head);
                    }
                    else if (CutAt(items[head], rest) is { } changed)
                    {
                        items[head] = changed;
                    }
                    else
                    {
                        return null;
                    }

                    return new ListNode(items);
                }
            case GroupNode group when head == 0:
                {
                    return rest.IsRoot
                        ? group.Inner
                        : CutAt(group.Inner, rest) is { } inner
                            ? group with { Inner = inner }
                            : null;
                }
            case CallNode call when head >= 0 && head < call.Arguments.Count:
                {
                    var arguments = call.Arguments.ToList();
                    if (rest.IsRoot)
                    {
                        arguments.RemoveAt(head);
                    }
                    else if (CutAt(arguments[head], rest) is { } changed)
                    {
                        arguments[head] = changed;
                    }
                    else
                    {
                        return null;
                    }

                    return call with { Arguments = arguments };
                }
            case BinaryNode binary when head == 0:
                {
                    if (rest.IsRoot)
                    {
                        return Simplify(binary.Right);
                    }

                    return CutAt(binary.Left, rest) is { } left
                        ? binary with { Left = left }
                        : null;
                }
            case BinaryNode binary when head == 1:
                {
                    if (rest.IsRoot)
                    {
                        return Simplify(binary.Left);
                    }

                    return CutAt(binary.Right, rest) is { } right
                        ? binary with { Right = right }
                        : null;
                }
            case UnaryNode unary when head == 0:
                {
                    if (rest.IsRoot)
                    {
                        return Formula.Nothing;
                    }

                    return CutAt(unary.Operand, rest) is { } operand
                        ? unary with { Operand = operand }
                        : null;
                }
            default:
                return null;
        }
    }

    /// <summary>A removed operand that was itself empty leaves an empty value, not another hole.</summary>
    private static ExpressionNode Simplify(ExpressionNode remaining)
        => remaining is HoleNode ? Formula.Nothing : remaining;

    /// <summary>Joins the text runs a removed piece stood between, then lets a run of one collapse.</summary>
    private static ExpressionNode MergeText(IReadOnlyList<ExpressionNode> parts)
    {
        var merged = new List<ExpressionNode>();
        foreach (var part in parts)
        {
            if (part is TextNode text)
            {
                if (text.Text.Length == 0)
                {
                    continue;
                }

                if (merged.Count > 0 && merged[^1] is TextNode previous)
                {
                    merged[^1] = Formula.Text(previous.Text + text.Text);
                    continue;
                }
            }

            merged.Add(part);
        }

        return Formula.Run(merged);
    }

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
        int at, NodePath rest, ExpressionNode value)
    {
        var next = items.ToList();
        if (!rest.IsRoot && at >= 0 && at < next.Count)
        {
            // The walk carries on into a piece that is there, so the change belongs inside it.
            next[at] = Insert(next[at], rest, value);
        }
        else if (at >= 0 && at < next.Count)
        {
            next.Insert(at, value);
        }
        else
        {
            // One past the last piece is the ghost at the end of a call, or the end of a run:
            // putting something there adds it, it never rewrites the piece before it.
            next.Add(value);
        }

        return next;
    }
}
