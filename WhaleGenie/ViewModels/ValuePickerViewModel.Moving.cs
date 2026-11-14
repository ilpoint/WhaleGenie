using System;
using System.Collections.Generic;
using System.Globalization;
using WhaleGenie.Core.Expressions;

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
    public void Place(string token)
    {
        var written = token.Trim();
        if (written.Length == 0)
        {
            return;
        }

        switch (written)
        {
            case "true" or "false" or "null":
                _draft.PutConstant(written);
                break;

            case "(" or ")" or "[" or "]":
                // A shape has no bracket pieces: the renderer draws them where precedence asks for
                // them. There is nothing to put into the value for a press on a bracket button.
                return;

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
                    _draft.PutOperator(OperatorOfLanguage(written));
                }
                else
                {
                    _draft.PutName(written);
                }

                break;
        }

        RefreshFromDraft();
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
        _draft.PutCall(name, places);
        RefreshFromDraft();
    }

    /// <summary>
    /// Takes one piece away, leaving a hole that can be filled again. A shape is never pulled apart
    /// by cutting characters out of a line.
    /// </summary>
    public bool RemoveArgument(SlotNode? piece)
    {
        if (piece is not { CanRemove: true, Path: { } path })
        {
            return false;
        }

        _draft.Remove(path);
        RefreshFromDraft();
        return true;
    }

    /// <summary>
    /// Where a piece would land if it were dropped here. The band draws one cursor from this and
    /// applies the same thing on release, so what the person sees cannot disagree with what happens.
    /// </summary>
    public DropPreview PreviewMove(SlotNode? piece, SlotNode? target, bool after)
    {
        var allowed = piece is { CanRemove: true, Path: not null, Source: not null }
            && target is { Path: not null }
            && !ReferenceEquals(piece, target);
        var noOp = allowed && piece!.Path!.Equals(target!.Path);
        return new DropPreview(target ?? piece ?? Recipe!, after, allowed, noOp);
    }

    /// <summary>
    /// Moves one piece to just before (or after) another. Both places are walks into the value, so
    /// the move is a removal and an insert on the shape rather than a cut and paste of characters.
    /// </summary>
    public bool MoveOnto(SlotNode? piece, SlotNode? target, bool after)
    {
        if (piece is not { CanRemove: true, Path: { } from, Source: { } shape }
            || target is not { Path: { } to }
            || ReferenceEquals(piece, target))
        {
            return false;
        }

        _draft.Remove(from);
        var destination = after
            ? to.Parent?.Append(to.Leaf + 1) ?? to
            : to;
        _draft.SetCaret(destination);
        _draft.Put(shape);
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
