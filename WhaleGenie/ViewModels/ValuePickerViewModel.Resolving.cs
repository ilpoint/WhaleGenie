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

// Where a piece is allowed to go, and the text that holds it apart from its neighbours.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    /// <summary>
    /// Decides where a drop actually goes. A run of pieces is not a thing to drop beside — the gap
    /// between two of them belongs to the run, so dropping in that gap means the end of the run.
    /// </summary>
    private bool Resolve(
        SlotNode? piece,
        SlotNode? target,
        bool after,
        out SlotNode anchor,
        out bool dropAfter,
        out bool gap)
    {
        anchor = target ?? piece ?? Recipe!;
        dropAfter = after;
        gap = false;

        if (piece is not { CanRemove: true, Length: > 0 } moved
            || target is null
            || ReferenceEquals(moved, target))
        {
            return false;
        }

        // A run is not a thing to drop beside: the gap between its pieces is the run's own space,
        // so a drop there belongs at the end of it. Checked before the "inside the piece" test,
        // because a run starts where its first piece does.
        if (target is SlotSequence run)
        {
            if (run.Items.LastOrDefault(one => one.Length > 0) is not { } last
                || ReferenceEquals(last, moved))
            {
                return false;
            }

            anchor = last;
            dropAfter = true;
            gap = true;
            return true;
        }

        return target.Start < moved.Start || target.Start >= moved.Start + moved.Length;
    }

    /// <summary>
    /// True when a seam is where this piece already stands — its own characters, or the space and
    /// comma that keep it from its neighbour. Dropping it there would take it out and put it back a
    /// little to the left, which is how "f(a, b, c)" used to become "f(ab, , c)".
    /// </summary>
    private bool Inside(int seam, SlotNode piece)
    {
        var start = piece.Start;
        while (start > 0 && Text[start - 1] == ' ')
        {
            start--;
        }

        if (start > 0 && Text[start - 1] == ',')
        {
            start--;
        }

        var end = piece.Start + piece.Length;
        while (end < Text.Length && Text[end] == ' ')
        {
            end++;
        }

        return seam >= start && seam <= end;
    }

    /// <summary>Where a drop lands, counted in the text as it stands before anything is cut.</summary>
    private static int SeamOf(SlotNode anchor, bool after) => after ? anchor.Start + anchor.Length : anchor.Start;

    /// <summary>
    /// The text that has to be written around a piece at this seam: inside a call's arguments it is
    /// the comma that keeps it apart from its neighbour — in front at the end of the list, behind it
    /// anywhere else. In front of a ghost it is the comma the ghost was standing behind. Everywhere
    /// else nothing, and the seam itself decides whether a space is needed.
    /// </summary>
    private (string Lead, string Trail) SeparatorsFor(SlotNode anchor, int seam, bool gap)
    {
        // Dropped into the space between pieces: it joins the end of that run, one space along.
        if (gap)
        {
            return (seam > 0 ? " " : string.Empty, string.Empty);
        }

        if (anchor is SlotEmpty { IsGhost: true } ghost)
        {
            return (ghost.Before, string.Empty);
        }

        if (anchor is SlotEmpty)
        {
            return (string.Empty, string.Empty);
        }

        var call = Recipe?.Walk()
            .OfType<SlotCall>()
            .Where(one => one.Arguments.Count > 0)
            .OrderBy(one => one.Length)
            .FirstOrDefault(one =>
            {
                var first = one.Arguments[0].Start;
                var last = one.Ghost?.Start ?? one.Start + one.Length;
                return seam >= first && seam <= last;
            });

        if (call is null)
        {
            return (string.Empty, string.Empty);
        }

        var end = call.Ghost?.Start ?? call.Start + call.Length;
        return seam >= end ? (", ", string.Empty) : (string.Empty, ", ");
    }

    /// <summary>Maps a seam measured before a cut onto the text that is left after it.</summary>
    public static int SeamAfterCut(int seam, CutSpan span)
        => seam >= span.End ? seam - span.Length
            : seam > span.Start ? span.Start
            : seam;

    /// <summary>
    /// Takes a span out and tidies the seam: the comma that kept the piece apart from its neighbours
    /// goes with it, and two spaces never end up next to each other. One rule covers everything that
    /// can be removed — an argument, a sign, a bracket — because all of them are just spans.
    /// </summary>
    private CutSpan Cut(int start, int length)
    {
        var text = CutText(Text, start, length, out var span);
        Text = text;
        return span;
    }

    /// <summary>
    /// The text a cut leaves, without writing it down yet. A move has to check the result before
    /// it changes the formula, so the cut and the tidy-up are one pure step here.
    /// </summary>
    private static string CutText(string original, int start, int length, out CutSpan span)
    {
        var at = Math.Clamp(start, 0, original.Length);
        var end = Math.Clamp(at + length, at, original.Length);
        var text = original.Remove(at, end - at);

        var before = at - 1;
        while (before >= 0 && text[before] == ' ')
        {
            before--;
        }

        var after = at;
        while (after < text.Length && text[after] == ' ')
        {
            after++;
        }

        var left = before >= 0 && text[before] == ',';
        var right = after < text.Length && (text[after] == ',' || text[after] is ')' or ']');

        if (left && right)
        {
            // The comma in front kept it from its neighbour; that neighbour now keeps the next one.
            var from = before;
            while (from > 0 && text[from - 1] == ' ')
            {
                from--;
            }

            text = text.Remove(from, at - from);
        }
        else if (right && text[after] == ',')
        {
            var to = after + 1;
            while (to < text.Length && text[to] == ' ')
            {
                to++;
            }

            text = text.Remove(at, to - at);
        }
        if (at > 0 && at < text.Length && text[at - 1] == ' ' && text[at] == ' ')
        {
            text = text.Remove(at, 1);
        }

        // Taking a sign out from between two numbers would leave one number where there were two.
        if (at > 0 && at < text.Length && Glues(text[at - 1], text[at]))
        {
            text = text.Insert(at, " ");
        }

        span = Difference(original, text);
        return text;
    }

    /// <summary>
    /// The stretch of text that actually left, found by comparing what was written with what is
    /// written now. Tidying up around a cut takes commas and spaces away on its own, so counting the
    /// characters beforehand would put the seam in the wrong place.
    /// </summary>
    private static CutSpan Difference(string before, string after)
    {
        var head = 0;
        var shortest = Math.Min(before.Length, after.Length);
        while (head < shortest && before[head] == after[head])
        {
            head++;
        }

        var tail = 0;
        while (tail < shortest - head
               && before[before.Length - 1 - tail] == after[after.Length - 1 - tail])
        {
            tail++;
        }

        return new CutSpan(head, before.Length - tail);
    }
}
