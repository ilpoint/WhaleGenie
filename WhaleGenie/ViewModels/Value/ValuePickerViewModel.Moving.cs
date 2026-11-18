using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;

namespace WhaleGenie.ViewModels;

// Editing the value: putting a piece in, taking one away, and moving one.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    /// <summary>One stretch of the band's drawing order, used by the seam arithmetic.</summary>
    public readonly record struct CutSpan(int Start, int End)
    {
        public int Length => End - Start;
    }

    /// <summary>
    /// What a drop would do, which is everything the band needs to draw the cursor. The window draws
    /// from this and applies the same one on release, so the picture and the result cannot disagree.
    /// </summary>
    public readonly record struct DropPreview(
        SlotNode Anchor, bool After, bool Allowed, bool NoOp);

    /// <summary>
    /// Which side of a piece a pointer is on. A run of pieces reads top to bottom when it folds, so
    /// there the vertical half says before or after; a single piece still reads left to right.
    /// </summary>
    public static bool AfterFor(SlotNode? target, bool rightHalf, bool lowerHalf)
        => target is SlotSequence or SlotCall { Stacked: true } ? lowerHalf : rightHalf;

    /// <summary>
    /// Writes one material at the insertion point. A token from a palette is an input from a
    /// person; it becomes a shape here, and the shape editor decides how the tree grows. No line is
    /// spliced and read back.
    /// </summary>
    public bool Place(string token)
    {
        var written = token.Trim();
        if (written.Length == 0)
        {
            return false;
        }

        // A sign may wrap the expression before the caret; a value, a name or a function needs a
        // socket to go into. Without one, appending a sibling is how a readable formula turned into
        // two expressions standing next to each other, so it is refused and the reason is shown.
        var sign = OperatorsOfLanguage.Contains(written);
        if (IsValueMode && !sign && !CanPutValue)
        {
            RejectPlacement();
            return false;
        }

        switch (written)
        {
            case "true" or "false" or "null":
                _draft.PutConstant(written);
                break;

            case "(" or ")" or "[" or "]":
                // Brackets and lists are one token each, inserted by their own actions.
                return false;

            default:
                if (double.TryParse(written, NumberStyles.Float, CultureInfo.InvariantCulture,
                        out var number))
                {
                    _draft.PutNumber(number);
                }
                else if (written.Length >= 2 && written[0] == '"' && written[^1] == '"')
                {
                    _draft.PutText(Unquote(written[1..^1]));
                }
                else if (OperatorsOfLanguage.Contains(written))
                {
                    var op = OperatorOfLanguage(written);
                    var applied = SelectedPath is { } picked
                        ? _draft.PutOperatorAt(picked, op)
                        : _draft.PutOperator(op);
                    if (!applied)
                    {
                        return false;
                    }
                }
                else
                {
                    _draft.PutName(written);
                }

                break;
        }

        RefreshFromDraft();
        return true;
    }

    /// <summary>
    /// True when the next value, name or call has a place to go: the root is empty, the caret is
    /// on a socket, or the value is still a run that a name is being put into.
    /// </summary>
    public bool CanPutValue => IsRunMode
        || IsValueMode && (_draft.IsEmpty || _draft.Value is ConcatNode
            || ShapeTree.At(_draft.Value, _draft.Caret) is HoleNode
            || Recipe?.Walk().Any(piece => piece is SlotEmpty empty
                && empty.Path?.Equals(_draft.Caret) == true) == true);

    /// <summary>Says why a piece was refused, in the one line under the band.</summary>
    private void RejectPlacement()
    {
        Note = Strings.Get("ValuePicker.Note.NoRoom");
        NoteIsProblem = true;
    }

    /// <summary>Puts a pair of brackets around the whole value, or around an empty place.</summary>
    public bool WrapGroup()
    {
        var path = SelectedPath ?? NodePath.Root;
        if (!IsValueMode || !_draft.WrapGroup(path))
        {
            return false;
        }

        SelectedPath = path;
        RefreshFromDraft();
        return true;
    }

    /// <summary>Makes the whole value the first item of a list.</summary>
    public bool WrapList()
    {
        var path = SelectedPath ?? NodePath.Root;
        if (!IsValueMode || !_draft.WrapList(path))
        {
            return false;
        }

        SelectedPath = path;
        RefreshFromDraft();
        return true;
    }

    /// <summary>Gives the binary or unary under this token a different sign, keeping its operands.</summary>
    public bool ChangeOperator(SlotNode piece, string sign)
    {
        if (piece.Path is not { } path || !_draft.SetOperator(path, sign))
        {
            return false;
        }

        RefreshFromDraft();
        return true;
    }

    /// <summary>
    /// Adds a call with one empty place per required argument and leaves the insertion point in the
    /// first one, ready to be filled. Nothing already written is put inside it against the reader's
    /// wish.
    /// </summary>
    public void PlaceCall(string name)
    {
        var places = _table.TryGetValue(name, out var entry)
            ? Math.Max(1, entry.MinArguments)
            : 1;

        // A picked value is what the card applies to: the function wraps it as the first argument.
        if (IsValueMode && SelectedNode is { } picked && picked.Path is { } path
            && picked is not SlotEmpty)
        {
            if (_draft.PutCallOn(path, name, places))
            {
                RefreshFromDraft();
            }

            return;
        }

        if (IsValueMode && !CanPutValue)
        {
            RejectPlacement();
            return;
        }

        _draft.PutCall(name, places);
        RefreshFromDraft();
    }

    /// <summary>
    /// Takes one piece away and closes the gap. The cross and the delete keys both end here, so a
    /// mouse click and a Backspace cannot leave different shapes behind.
    /// </summary>
    public bool RemoveArgument(SlotNode? piece)
    {
        if (piece is not { CanRemove: true, Path: { } path })
        {
            return false;
        }

        var at = piece.Start;
        if (!_draft.Cut(path))
        {
            return false;
        }

        RefreshFromDraft(syncCaret: false);
        SetCaretAfterCut(path, at);
        return true;
    }

    /// <summary>
    /// After a cut, the place to write next is the socket the removed piece left behind: the next
    /// argument of the call or item of the list, not the outside of the container.
    /// </summary>
    private void SetCaretAfterCut(NodePath removed, int at)
    {
        var parent = removed.Parent;
        if (parent is not null)
        {
            var call = Recipe?.Walk().OfType<SlotCall>()
                .FirstOrDefault(piece => piece.Path?.Equals(parent) == true);
            var list = Recipe?.Walk().OfType<SlotList>()
                .FirstOrDefault(piece => piece.Path?.Equals(parent) == true);
            if ((call?.Ghost ?? list?.Ghost) is { } ghost)
            {
                SetCaret(ghost.Start);
                return;
            }
        }

        SetCaret(Math.Min(at, SpanEnd));
    }

    /// <summary>Deletes the piece just before the standing line, if there is one to delete.</summary>
    public bool CutBeforeCaret() => CutAtCaret(before: true);

    /// <summary>Deletes the piece just after the standing line, if there is one to delete.</summary>
    public bool CutAfterCaret() => CutAtCaret(before: false);

    private bool CutAtCaret(bool before)
    {
        if (Recipe is null)
        {
            return false;
        }

        // A call or a run is a container, not a piece the Backspace may take away: deleting a
        // call at its end must remove the last argument, not the whole expression. A ghost is not
        // in the tree at all, so it cannot be cut either.
        var pieces = Recipe.Walk()
            .Where(piece => piece.Path is not null
                && (piece is SlotValue || piece is SlotEmpty { IsGhost: false }))
            .ToList();
        var target = before
            ? pieces.Where(piece => piece.Start + piece.Length == Caret)
                .OrderByDescending(piece => piece.Path!.Indices.Count)
                .ThenByDescending(piece => piece.Start)
                .FirstOrDefault()
              ?? pieces.Where(piece => piece.Start + piece.Length <= Caret)
                .OrderByDescending(piece => piece.Start + piece.Length)
                .ThenByDescending(piece => piece.Path!.Indices.Count)
                .FirstOrDefault()
            : pieces.Where(piece => piece.Start == Caret)
                .OrderByDescending(piece => piece.Path!.Indices.Count)
                .ThenBy(piece => piece.Start)
                .FirstOrDefault()
              ?? pieces.Where(piece => piece.Start >= Caret)
                .OrderBy(piece => piece.Start)
                .ThenByDescending(piece => piece.Path!.Indices.Count)
                .FirstOrDefault();

        var at = Caret;
        if (target?.Path is not { } path || !_draft.Cut(path))
        {
            return false;
        }

        RefreshFromDraft(syncCaret: false);
        SetCaretAfterCut(path, at);
        return true;
    }

    /// <summary>
    /// Where a piece would land if it were dropped here. The band draws one cursor from this and
    /// applies the same thing on release, so what the person sees cannot disagree with what happens.
    /// </summary>
    public DropPreview PreviewMove(SlotNode? piece, SlotNode? target, bool after)
    {
        // A sign is part of a binary token, not a piece that moves on its own; dragging an operand
        // out of an expression would leave a place nothing can fill. Call arguments and the pieces
        // of a run move; that is where a drop says something useful.
        var movable = piece is { CanRemove: true, Path: not null, Source: not null }
            && !InsideSign(piece.Path!)
            && (target is SlotEmpty || target?.Path is null || !InsideSign(target.Path));
        var allowed = movable && target is { Path: not null } && !ReferenceEquals(piece, target);
        var noOp = allowed && piece!.Path!.Equals(target!.Path);
        return new DropPreview(target ?? piece ?? Recipe!, after, allowed, noOp);
    }

    /// <summary>True when this place is inside a binary or unary token rather than a run.</summary>
    private bool InsideSign(NodePath path)
        => path.Parent is { } parent && ShapeTree.At(_draft.Value, parent) is BinaryNode or UnaryNode;

    /// <summary>
    /// Moves one piece to just before (or after) another. Both places are walks into the value, so
    /// the move is a removal and an insert on the shape rather than a cut and paste of characters.
    /// </summary>
    public bool MoveOnto(SlotNode? piece, SlotNode? target, bool after)
    {
        if (piece is not { CanRemove: true, Path: { } from }
            || target is not { Path: { } to }
            || ReferenceEquals(piece, target))
        {
            return false;
        }

        if (!_draft.Move(from, to, after, target is SlotEmpty { IsGhost: true }))
        {
            return false;
        }

        RefreshFromDraft();
        return true;
    }

    /// <summary>The signs a person can put between two pieces, as the language spells them.</summary>
    private static readonly HashSet<string> OperatorsOfLanguage =
    [
        "+", "−", "-", "×", "*", "÷", "/", "%", "=", "==", "≠", "!=",
        ">", "≥", ">=", "<", "≤", "<=", "and", "or", "&&", "||", "not",
    ];

    /// <summary>The word the engine reads where the palette shows a mathematical sign.</summary>
    private static string OperatorOfLanguage(string token) => token switch
    {
        "−" => "-",
        "×" => "*",
        "÷" => "/",
        "=" => "==",
        "≠" => "!=",
        "≥" => ">=",
        "≤" => "<=",
        "&&" => "and",
        "||" => "or",
        _ => token,
    };

    /// <summary>The escapes a quoted word may carry, read back into the word itself.</summary>
    private static string Unquote(string written) => written
        .Replace("\\\\", "\\")
        .Replace("\\\"", "\"")
        .Replace("\\n", "\n")
        .Replace("\\r", "\r")
        .Replace("\\t", "\t");
}
