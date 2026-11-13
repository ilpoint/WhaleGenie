using System;
using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The value a field is editing, held as the shape it will keep. There is one authority here:
/// <see cref="Value"/> is what the field will store, and <see cref="Recipe"/> is drawn from it on
/// every change rather than being a second thing somebody has to keep in step. Typing, picking a
/// name, inserting a call and taking a piece away all go through <see cref="Put"/>,
/// <see cref="Set"/>, <see cref="Remove"/> and <see cref="SetCaret"/>, so no line of text is ever
/// read back into a value.
///
/// <para>
/// A place is a <see cref="NodePath"/>, not a count of characters: the same path names the piece
/// under the caret, the piece a drop would move and the piece a cross would take away.
/// </para>
/// </summary>
public sealed class ValueDraft : ViewModelBase
{
    private readonly IReadOnlyDictionary<string, ExpressionFunction> _functions;
    private readonly bool _bare;
    private readonly Stack<ExpressionNode> _before = new();
    private readonly Stack<ExpressionNode> _after = new();
    private ExpressionNode _value;
    private NodePath _caret = NodePath.Root;

    public ValueDraft(ExpressionNode value,
        IReadOnlyDictionary<string, ExpressionFunction>? functions = null, bool bare = false)
    {
        _value = value;
        _bare = bare;
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
    public NodePath Caret => _caret;

    /// <summary>True when the value says nothing at all.</summary>
    public bool IsEmpty => Formula.IsNothing(_value);

    /// <summary>
    /// The value said for a person to read. This goes to a log or a failure message and is never
    /// read back: the shape is the value.
    /// </summary>
    public string Speak => Formula.Speak(_value);

    public bool CanUndo => _before.Count > 0;

    public bool CanRedo => _after.Count > 0;

    public void SetCaret(NodePath path)
    {
        if (_caret.Equals(path))
        {
            return;
        }

        _caret = path;
        OnPropertyChanged(nameof(Caret));
    }

    /// <summary>
    /// Takes a shape that was read somewhere else as the value, without calling it an edit. A line
    /// somebody typed is remembered by the line's own history; marking it here too would put two
    /// steps in the undo stack for one keystroke.
    /// </summary>
    public void Replace(ExpressionNode value)
    {
        if (Equals(_value, value))
        {
            return;
        }

        _value = value;
        Rebuild();
    }

    /// <summary>Puts a shape where the caret stands, keeping everything already there.</summary>
    public void Put(ExpressionNode piece) => Change(ShapeTree.Insert(_value, _caret, piece));

    /// <summary>Replaces the piece at a place with a shape.</summary>
    public void Set(NodePath path, ExpressionNode piece) => Change(ShapeTree.Set(_value, path, piece));

    /// <summary>Takes the piece at a place away, leaving a hole that can be filled again.</summary>
    public void Remove(NodePath path) => Change(ShapeTree.Remove(_value, path));

    /// <summary>Empties the field. A deliberate empty is a value too.</summary>
    public void Clear() => Change(Formula.Nothing);

    public void Undo()
    {
        if (_before.Count == 0)
        {
            return;
        }

        _after.Push(_value);
        _value = _before.Pop();
        Rebuild();
    }

    public void Redo()
    {
        if (_after.Count == 0)
        {
            return;
        }

        _before.Push(_value);
        _value = _after.Pop();
        Rebuild();
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

        _before.Push(_value);
        _after.Clear();
        _value = next;
        Rebuild();
    }

    private void Rebuild()
    {
        (Recipe, End) = SlotReader.Shape(_value, _functions, _bare);
        OnPropertyChanged(nameof(Recipe));
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(Speak));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }
}
