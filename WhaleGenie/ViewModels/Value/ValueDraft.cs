using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One seam in the shape: the place before or after a piece. A path alone cannot say which side
/// of a piece the caret is on, and a line-end caret is not the start of the last piece.
/// </summary>
public readonly record struct CaretPlace(NodePath Path, bool After);

/// <summary>
/// The value a field is editing, held as the shape it will keep. There is one authority here:
/// <see cref="Value"/> is what the field will store, and <see cref="Recipe"/> is drawn from it on
/// every change rather than being a second thing somebody has to keep in step. Typing, picking a
/// name, inserting a call and taking a piece away all go through <see cref="Put"/>,
/// <see cref="Set"/>, <see cref="Cut"/>, <see cref="Remove"/> and <see cref="SetCaret"/>, so no
/// line of text is ever read back into a value.
///
/// <para>
/// A place is a <see cref="NodePath"/>, not a count of characters: the same path names the piece
/// under the caret, the piece a drop would move and the piece a cross would take away.
/// </para>
/// </summary>
public sealed class ValueDraft : ViewModelBase
{
    private readonly IReadOnlyDictionary<string, ExpressionFunction> _functions;
    private readonly Stack<Snapshot> _before = new();
    private readonly Stack<Snapshot> _after = new();
    private ExpressionNode _value;
    private CaretPlace _caret = new(NodePath.Root, false);

    /// <summary>One step in the shape's history: the value and where the standing line was in it.</summary>
    private readonly record struct Snapshot(ExpressionNode Value, CaretPlace Caret);

    public ValueDraft(ExpressionNode value,
        IReadOnlyDictionary<string, ExpressionFunction>? functions = null)
    {
        _value = value;
        _functions = functions
            ?? Expression.Functions.ToDictionary(function => function.Name,
                StringComparer.OrdinalIgnoreCase);
        Rebuild();
    }

    /// <summary>The shape the field will store. The only value this draft keeps.</summary>
    public ExpressionNode Value => _value;

    /// <summary>The shapes drawn in the structure band, derived from <see cref="Value"/>.</summary>
    public SlotNode Recipe { get; private set; } = null!;

    /// <summary>One past the last place the band draws, which bounds its caret.</summary>
    public int End { get; private set; }

    /// <summary>Where the next piece goes. A path into the tree, not an offset into a line.</summary>
    public NodePath Caret => _caret.Path;

    /// <summary>True when the standing line is after the piece named by <see cref="Caret"/>.</summary>
    public bool CaretAfter => _caret.After;

    /// <summary>The seam expressed in the band's drawing order, the number the window draws.</summary>
    public int CaretOffset
    {
        get
        {
            if (Recipe is null)
            {
                return 0;
            }

            if (_caret.Path.IsRoot)
            {
                return _caret.After ? End : 0;
            }

            // The ghost at the end of a call is not in the value tree, so it has no piece to match.
            foreach (var call in Recipe.Walk().OfType<SlotCall>())
            {
                if (call.Ghost is { Path: { } ghost, Start: var start } && ghost.Equals(_caret.Path))
                {
                    return start;
                }
            }

            var matches = Recipe.Walk()
                .Where(piece => piece.Path is { } path && path.Equals(_caret.Path))
                .ToList();
            if (matches.Count == 0)
            {
                return _caret.After ? End : 0;
            }

            return _caret.After
                ? matches.Max(piece => piece.Start + piece.Length)
                : matches.Min(piece => piece.Start);
        }
    }

    /// <summary>True when the value says nothing at all.</summary>
    public bool IsEmpty => Formula.IsNothing(_value);

    /// <summary>
    /// The value said for a person to read. This goes to a log or a failure message and is never
    /// read back: the shape is the value.
    /// </summary>
    public string Speak => ExpressionPhrase.Display(_value);

    public bool CanUndo => _before.Count > 0;

    public bool CanRedo => _after.Count > 0;

    public void SetCaret(NodePath path, bool after = false)
    {
        var place = new CaretPlace(path, after);
        if (_caret.Equals(place))
        {
            return;
        }

        _caret = place;
        OnPropertyChanged(nameof(Caret));
        OnPropertyChanged(nameof(CaretAfter));
        OnPropertyChanged(nameof(CaretOffset));
    }

    /// <summary>
    /// Takes a shape that was read somewhere else as the value, without calling it an edit. A line
    /// somebody typed is remembered by the line's own history; marking it here too would put two
    /// steps in the undo stack for one keystroke.
    /// </summary>
    public void Replace(ExpressionNode value, bool remember = false)
    {
        if (Equals(_value, value))
        {
            return;
        }

        if (remember)
        {
            Change(value);
            return;
        }

        _value = value;
        Rebuild();
    }

    /// <summary>The seam after normalizing a path inside a binary/unary to that structure's edge.</summary>
    public CaretPlace WritePlace => Normalize(_caret);

    /// <summary>
    /// Puts a shape at the current seam. A hole is filled; otherwise the piece is inserted on the
    /// side the seam names, and the caret moves after the new piece so the next material follows it.
    /// </summary>
    public void Put(ExpressionNode piece)
    {
        var standing = ShapeTree.At(_value, _caret.Path);
        if (standing is HoleNode || (_caret.Path.IsRoot && Formula.IsNothing(_value)))
        {
            Change(ShapeTree.Set(_value, _caret.Path, piece));
            SetCaretToPiece(piece);
            return;
        }

        Change(Insert(piece));
        SetCaretToPiece(piece);
    }

    /// <summary>Puts a sign around the token at a place, which is what a picked token means.</summary>
    public bool PutOperatorAt(NodePath path, string op)
    {
        if (op == "not")
        {
            return PutUnary(path, op);
        }

        var left = ShapeTree.At(_value, path);
        if (left is null or HoleNode)
        {
            return false;
        }

        Change(ShapeTree.Surround(_value, path, op));
        SetCaret(path.Append(1), after: false);
        return true;
    }

    /// <summary>Wraps the expression at a place in its one unary sign, which takes no right side.</summary>
    private bool PutUnary(NodePath path, string sign)
    {
        var operand = ShapeTree.At(_value, path);
        if (operand is null or HoleNode)
        {
            return false;
        }

        var unary = new UnaryNode(sign, operand);
        Change(ShapeTree.Set(_value, path, unary));
        SetCaretToPiece(unary);
        return true;
    }

    /// <summary>Puts a name where the caret stands.</summary>
    public void PutName(string name) => Put(Formula.Name(name));

    /// <summary>Puts one of the words that stand for a value, such as true, false or null.</summary>
    public void PutConstant(string word) => Put(Formula.Constant(word));

    /// <summary>Puts a number or a piece of text as the shape it is, not as a line to read back.</summary>
    public void PutNumber(double number) => Put(Formula.Number(number));

    public void PutText(string text) => Put(Formula.Text(text));

    /// <summary>
    /// Puts a sign after what stands at the caret and leaves the caret on the empty right side, so
    /// the next piece typed completes the operation. A line editor would append "+" and let a
    /// parser guess; here the tree grows by one binary node and the hole says what is still owed.
    /// </summary>
    public bool PutOperator(string op)
    {
        if (!CanPutOperator)
        {
            return false;
        }

        var place = WritePlace;
        if (op == "not")
        {
            return PutUnary(place.Path, op);
        }

        Change(ShapeTree.Surround(_value, place.Path, op));
        SetCaret(place.Path.Append(1), after: false);
        return true;
    }

    /// <summary>
    /// True when an operator would have a complete expression on its left. An empty value, a hole
    /// or a seam before a piece has nothing to wrap, so an operator must not grow another hole there.
    /// </summary>
    public bool CanPutOperator
    {
        get
        {
            if (Formula.IsNothing(_value) || ShapeTree.At(_value, _caret.Path) is HoleNode)
            {
                return false;
            }

            var place = WritePlace;
            var left = place.Path.IsRoot ? _value : ShapeTree.At(_value, place.Path);
            return left is not null and not HoleNode
                && !Formula.IsNothing(left)
                && (place.Path.IsRoot || place.After);
        }
    }

    /// <summary>
    /// Puts a call at the caret with one empty argument (or as many as asked for) and leaves the
    /// caret in the first one. The function table decides later how many arguments are required;
    /// the draft only needs a place for each one to be filled.
    /// </summary>
    public void PutCall(string name, int arguments = 1)
    {
        var holes = Enumerable.Range(0, Math.Max(1, arguments))
            .Select(_ => (ExpressionNode)Formula.Hole).ToArray();
        Put(Formula.Call(name, holes));
        SetCaret(_caret.Path.Append(0), after: false);
    }

    /// <summary>Replaces the piece at a place with a shape.</summary>
    public void Set(NodePath path, ExpressionNode piece) => Change(ShapeTree.Set(_value, path, piece));

    /// <summary>
    /// Puts a call around the piece at a place: the piece becomes the first argument and the rest
    /// of the call's places wait to be filled. A picked value is what a function card applies to.
    /// </summary>
    public bool PutCallOn(NodePath path, string name, int arguments)
    {
        var selected = ShapeTree.At(_value, path);
        if (selected is null or HoleNode)
        {
            return false;
        }

        var holes = Enumerable.Range(0, Math.Max(0, arguments - 1))
            .Select(_ => (ExpressionNode)Formula.Hole);
        var call = Formula.Call(name, [selected, .. holes]);
        Change(ShapeTree.Set(_value, path, call));
        SetCaret(path.Append(1), after: false);
        return true;
    }

    /// <summary>
    /// Gives an existing binary or unary a different sign. The sign is not a piece of its own, so
    /// this is the one edit a press on it makes; the operands never move.
    /// </summary>
    public bool SetOperator(NodePath path, string sign)
    {
        var node = ShapeTree.At(_value, path);
        var changed = node switch
        {
            BinaryNode binary => (ExpressionNode)(binary with { Operator = sign }),
            UnaryNode unary => unary with { Operator = sign },
            _ => null,
        };
        if (changed is null)
        {
            return false;
        }

        Change(ShapeTree.Set(_value, path, changed));
        return true;
    }

    private ExpressionNode Insert(ExpressionNode piece)
    {
        var place = WritePlace;
        if (place.Path.IsRoot)
        {
            if (_value is ConcatNode run)
            {
                return ShapeTree.Insert(_value,
                    NodePath.Root.Append(place.After ? run.Parts.Count : 0), piece);
            }

            if (_value is ListNode list)
            {
                return ShapeTree.Insert(_value,
                    NodePath.Root.Append(place.After ? list.Items.Count : 0), piece);
            }

            return place.After
                ? Formula.Run([_value, piece])
                : Formula.Run([piece, _value]);
        }

        if (place.Path.Parent is { } parent
            && ShapeTree.At(_value, parent) is ConcatNode or ListNode or CallNode)
        {
            var index = place.Path.Leaf + (place.After ? 1 : 0);
            return ShapeTree.Insert(_value, parent.Append(index), piece);
        }

        // A structural parent should already have been normalized away. Falling back to the root
        // keeps the value whole rather than putting a piece inside an operator it cannot hold.
        return place.After
            ? Formula.Run([_value, piece])
            : Formula.Run([piece, _value]);
    }

    /// <summary>
    /// Moves a seam inside a binary or unary to that structure's edge. The inside of an operator
    /// has no sequence to insert into, so the nearest writable seam is before or after the whole
    /// structure; a hole is left alone because it is filled rather than inserted around.
    /// </summary>
    private CaretPlace Normalize(CaretPlace place)
    {
        if (place.Path.IsRoot || ShapeTree.At(_value, place.Path) is HoleNode)
        {
            return place;
        }

        var indices = place.Path.Indices;
        for (var length = 1; length <= indices.Count; length++)
        {
            var prefix = new NodePath([.. indices.Take(length)]);
            if (ShapeTree.At(_value, prefix) is not (BinaryNode or UnaryNode))
            {
                continue;
            }

            var parent = prefix.Parent;
            var parentNode = parent is null ? null : ShapeTree.At(_value, parent);
            if (parent is null || parentNode is ConcatNode or ListNode or CallNode)
            {
                return new CaretPlace(prefix, place.After);
            }
        }

        return place;
    }

    private void SetCaretToPiece(ExpressionNode piece)
    {
        var match = SlotReader.Shape(_value, _functions).Node.Walk()
            .Where(slot => slot.Path is not null && ReferenceEquals(slot.Source, piece))
            .OrderByDescending(slot => slot.Path!.Indices.Count)
            .FirstOrDefault();
        SetCaret(match?.Path ?? NodePath.Root, after: match is not null);
    }

    /// <summary>
    /// Moves the piece at one place to another, closing the gap it left and inserting it where it
    /// lands. The whole move is one step in the history, so undo puts the value back once.
    /// </summary>
    public bool Move(NodePath from, NodePath to, bool after, bool ontoGhost = false)
    {
        if (from.Equals(to) || IsWithin(to, from))
        {
            return false;
        }

        var source = ShapeTree.At(_value, from);
        var cut = ShapeTree.Cut(_value, from);
        var destination = Destination(cut, to, from, after, ontoGhost);
        if (source is null || destination is null)
        {
            return false;
        }

        var standing = ShapeTree.At(cut, destination);
        var next = standing is HoleNode
            ? ShapeTree.Set(cut, destination, source)
            : ShapeTree.Insert(cut, destination, source);
        if (Equals(next, _value))
        {
            return false;
        }

        Change(next);
        SetCaretToPiece(source);
        return true;
    }

    /// <summary>True when <paramref name="path"/> stands inside the subtree at <paramref name="ancestor"/>.</summary>
    private static bool IsWithin(NodePath path, NodePath ancestor)
        => path.Indices.Count >= ancestor.Indices.Count
            && path.Indices.Take(ancestor.Indices.Count).SequenceEqual(ancestor.Indices);

    /// <summary>
    /// The place the piece lands in after its old place has been closed. A target later in the
    /// same run moves up by one; a target in another run keeps its walk.
    /// </summary>
    private static NodePath? Destination(ExpressionNode cutValue, NodePath to, NodePath from, bool after,
        bool ontoGhost)
    {
        if (to.Parent is { } parent && from.Parent is { } oldParent && parent.Equals(oldParent))
        {
            if (from.Leaf == to.Leaf)
            {
                return null;
            }

            var index = to.Leaf - (from.Leaf < to.Leaf ? 1 : 0) + (after ? 1 : 0);
            return parent.Append(index);
        }

        if (after && to.Parent is { } targetParent)
        {
            return targetParent.Append(to.Leaf + 1);
        }

        return ontoGhost || ShapeTree.At(cutValue, to) is not null ? to : null;
    }

    /// <summary>
    /// Takes the piece at a place away and closes the gap. This is what the cross and the delete
    /// keys use; an unfilled place is only left by inserting an operator or a call.
    /// </summary>
    public bool Cut(NodePath path)
    {
        var next = ShapeTree.Cut(_value, path);
        if (Equals(next, _value))
        {
            return false;
        }

        Change(next);
        return true;
    }

    /// <summary>
    /// Lifts the piece at a place for a drag, leaving a hole so the rest of the tree keeps its
    /// paths until the piece lands. A move is not a delete; use <see cref="Cut"/> for that.
    /// </summary>
    public void Remove(NodePath path) => Change(ShapeTree.Remove(_value, path));

    /// <summary>
    /// Puts a pair of brackets around the expression at this place. The group is one token: the
    /// brackets cannot be separated, and what is inside keeps its own shape.
    /// </summary>
    public bool WrapGroup(NodePath path)
    {
        var inner = ShapeTree.At(_value, path);
        if (inner is null)
        {
            return false;
        }

        var group = new GroupNode(Formula.IsNothing(inner) ? Formula.Hole : inner);
        Change(ShapeTree.Set(_value, path, group));
        SetCaretToPiece(group);
        return true;
    }

    /// <summary>
    /// Makes the expression at this place the first item of a list. An empty place makes an empty
    /// list, whose ghost is the place the first item goes.
    /// </summary>
    public bool WrapList(NodePath path)
    {
        var inner = ShapeTree.At(_value, path);
        var list = Formula.IsNothing(inner) ? new ListNode([]) : new ListNode([inner!]);
        Change(ShapeTree.Set(_value, path, list));
        SetCaretToPiece(list);
        return true;
    }

    /// <summary>Empties the field. A deliberate empty is a value too.</summary>
    public void Clear() => Change(Formula.Nothing);

    public void Undo()
    {
        if (_before.Count == 0)
        {
            return;
        }

        _after.Push(new Snapshot(_value, _caret));
        Restore(_before.Pop());
    }

    public void Redo()
    {
        if (_after.Count == 0)
        {
            return;
        }

        _before.Push(new Snapshot(_value, _caret));
        Restore(_after.Pop());
    }

    private void Restore(Snapshot snapshot)
    {
        _value = snapshot.Value;
        _caret = snapshot.Caret;
        Rebuild();
        OnPropertyChanged(nameof(Caret));
        OnPropertyChanged(nameof(CaretAfter));
        OnPropertyChanged(nameof(CaretOffset));
    }

    /// <summary>True while a place the value needs has nothing in it.</summary>
    public bool HasHole() => Recipe.Walk().Any(piece => piece.Source is HoleNode)
        || HoldsHole(_value);

    private static bool HoldsHole(ExpressionNode node) => node switch
    {
        HoleNode => true,
        ConcatNode run => run.Parts.Any(HoldsHole),
        ListNode list => list.Items.Any(HoldsHole),
        CallNode call => call.Arguments.Any(HoldsHole),
        GroupNode group => HoldsHole(group.Inner),
        UnaryNode unary => HoldsHole(unary.Operand),
        BinaryNode binary => HoldsHole(binary.Left) || HoldsHole(binary.Right),
        _ => false,
    };

    private void Change(ExpressionNode next)
    {
        if (Equals(_value, next))
        {
            return;
        }

        _before.Push(new Snapshot(_value, _caret));
        _after.Clear();
        _value = next;
        Rebuild();
    }

    private void Rebuild()
    {
        (Recipe, End) = SlotReader.Shape(_value, _functions);
        OnPropertyChanged(nameof(Recipe));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Speak));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }
}
