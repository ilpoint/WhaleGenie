using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

// The language itself, spelled out: the property below is the text being edited, which hides the
// static class of the same name inside this file.
using Language = WhaleGenie.Core.Expressions.Expression;

namespace WhaleGenie.ViewModels;

// Moving a piece of the formula: the cut it leaves, the preview, and where it lands.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    /// <summary>One stretch of the formula, measured in the coordinates of the text it came from.</summary>
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

    /// <summary>A move worked out before anything is cut: where, what, and what goes around it.</summary>
    private readonly record struct MovePlan(SlotNode Piece, int Seam, string Raw, string Lead, string Trail);

    /// <summary>Writes one material at the insertion point, leaving everything already written alone.</summary>
    public void Place(string token) => InsertAtCaret(token);

    /// <summary>
    /// Adds an empty call and leaves the insertion point between its brackets, ready for an argument.
    /// Nothing already written is ever put inside it against the reader's wish.
    /// </summary>
    public void PlaceCall(string name)
    {
        var start = InsertAtCaret(name + "()");
        SetCaret(start + name.Length + 1);
    }

    /// <summary>
    /// Writes one piece where the insertion point is, putting a space on either side only where the
    /// characters would otherwise be read as a single piece. Filling the ghost also writes the comma
    /// it stands behind. Returns where the written piece begins, so a call can put its insertion
    /// point inside the brackets rather than after them.
    /// </summary>
    private int InsertAtCaret(string token)
    {
        var at = Snap(Math.Clamp(Caret, 0, Text.Length));
        if (token.Length == 0)
        {
            return at;
        }

        // The ghost is not an argument and is not in Walk's children: it is the one place a call
        // keeps that the text never wrote, so it is reached through the call that owns it.
        var ghost = Recipe?.Walk().OfType<SlotCall>()
            .Select(call => call.Ghost)
            .FirstOrDefault(one => one is { IsGhost: true } && one.Start == at);
        var end = Text.TrimEnd().Length;
        var left = at > 0 ? Text[at - 1] : '\0';
        var right = at < end ? Text[at] : '\0';
        var lead = ghost is not null ? ghost.Before
            : at == 0 ? string.Empty
            : at >= end ? " "
            : left != ' ' && (Glues(left, token[0]) || (EndsPiece(left) && StartsPiece(token[0])))
                ? " " : string.Empty;
        // A space already standing there is the separator: adding another would leave a double gap.
        var trail = ghost is null && at < end && right != ' '
            && (Glues(token[^1], right) || (EndsPiece(token[^1]) && StartsPiece(right)))
            ? " " : string.Empty;

        Text = Text.Insert(at, lead + token + trail);
        SetCaret(at + lead.Length + token.Length);
        return at + lead.Length;
    }

    /// <summary>True for the characters a name, a number or a word is made of.</summary>
    private static bool IsWord(char character)
        => char.IsLetterOrDigit(character) || character is '_' or '.' or '$';

    /// <summary>
    /// True when these two characters would be read as one piece if they ended up side by side.
    /// </summary>
    private static bool Glues(char left, char right)
        => (IsWord(left) && IsWord(right))
            || left is '"' or '\''
            || right is '"' or '\''
            || (left, right) is ('!', '=') or ('<', '=') or ('>', '=') or ('=', '=') or ('&', '&') or ('|', '|');

    /// <summary>True for a character that can end one piece of a formula.</summary>
    private static bool EndsPiece(char character)
        => IsWord(character) || character is '"' or '\'' or ')' or ']';

    /// <summary>True for a character that can begin one piece of a formula.</summary>
    private static bool StartsPiece(char character)
        => IsWord(character) || character is '"' or '\'' or '(' or '[';

    /// <summary>
    /// Takes one piece away, and the comma that kept it apart from its neighbour. The ghost at the
    /// end needs no restoring: it is not stored anywhere, it is what a call always has, so it is
    /// there again the moment the text is read back.
    /// </summary>
    public bool RemoveArgument(SlotNode? piece)
    {
        if (piece is not { CanRemove: true, Length: > 0 } target)
        {
            return false;
        }

        Cut(target.Start, target.Length);
        return true;
    }

    /// <summary>
    /// Where a piece would land if it were dropped here. The band draws one cursor from this and
    /// applies the same thing on release, so what the person sees cannot disagree with what happens.
    /// </summary>
    public DropPreview PreviewMove(SlotNode? piece, SlotNode? target, bool after)
    {
        if (!Resolve(piece, target, after, out var anchor, out var dropAfter, out _))
        {
            return new DropPreview((target ?? piece ?? Recipe)!, after, Allowed: false, NoOp: false);
        }

        var seam = SeamOf(anchor, dropAfter);
        var noOp = piece is { } moved && Inside(seam, moved);
        if (!TryPlanMove(piece, target, after, out var plan))
        {
            return new DropPreview(anchor, dropAfter, Allowed: false, noOp);
        }

        return new DropPreview(anchor, dropAfter, Allowed: TryBuildMove(plan, out _), noOp);
    }

    /// <summary>
    /// Moves one piece to just before (or after) another. The piece is taken out the same way its
    /// cross takes it out — commas and all — and written back where it was dropped, carrying the
    /// comma that keeps it apart from its neighbour inside a call, and a space where it would
    /// otherwise fuse with what stands next to it.
    /// </summary>
    public bool MoveOnto(SlotNode? piece, SlotNode? target, bool after)
    {
        if (!TryPlanMove(piece, target, after, out var plan) || !TryBuildMove(plan, out var next))
        {
            return false;
        }

        Text = next;
        return true;
    }

    /// <summary>
    /// The formula a move would leave, or null when it would turn a formula that read into one that
    /// does not. Nothing is written here: the drop preview and the drop itself ask the same question.
    /// </summary>
    private bool TryBuildMove(in MovePlan plan, out string next)
    {
        next = Text;
        var without = CutText(Text, plan.Piece.Start, plan.Piece.Length, out var span);
        var at = Math.Clamp(SeamAfterCut(plan.Seam, span), 0, without.Length);

        var lead = plan.Lead;
        var trail = plan.Trail;
        if (lead.Length == 0 && at > 0 && Glues(without[at - 1], plan.Raw[0]))
        {
            lead = " ";
        }

        if (trail.Length == 0 && at < without.Length && Glues(plan.Raw[^1], without[at]))
        {
            trail = " ";
        }

        // A move never means to leave spaces hanging off either end of the line.
        var built = without.Insert(at, lead + plan.Raw + trail).Trim();

        // A formula that already did not read may be on its way somewhere: refusing every move in a
        // half-written line would make the band feel stuck. Only a move that breaks a working
        // formula is refused.
        if (ReadsAsField(Text) && !ReadsAsField(built))
        {
            return false;
        }

        next = built;
        return true;
    }

    /// <summary>
    /// Works out what a move would do without doing it: the seam, counted in the text as it stands
    /// now, and the separators that seam asks for. Everything is measured before the cut, because
    /// cutting can take away the very comma the seam was measured against.
    /// </summary>
    private bool TryPlanMove(SlotNode? piece, SlotNode? target, bool after, out MovePlan plan)
    {
        plan = default;
        if (piece is not { CanRemove: true, Length: > 0 } moved
            || !Resolve(piece, target, after, out var anchor, out var dropAfter, out var gap))
        {
            return false;
        }

        var raw = Text.Substring(moved.Start, moved.Length).Trim();
        var seam = SeamOf(anchor, dropAfter);

        // Dropping a piece where it already stands is not a move: it would only cut and paste the
        // same characters back, sometimes leaving an empty argument behind.
        if (raw.Length == 0 || Inside(seam, moved))
        {
            return false;
        }

        var (lead, trail) = SeparatorsFor(anchor, seam, gap);
        plan = new MovePlan(moved, seam, raw, lead, trail);
        return true;
    }
}
