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

// Where the caret is: the seams it can stop at, stepping between them, undo and redo, and clearing.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    /// <summary>One past the last place the band draws, which bounds the caret.</summary>
    public int SpanEnd => _draft.End;

    /// <summary>
    /// Where the next piece goes, as a place in the band's drawing order. The band is drawn from
    /// the shape, so this is an offset into that drawing rather than into a line of text.
    /// </summary>
    public int Caret
    {
        get => _caret;
        private set
        {
            var next = Math.Clamp(value, 0, SpanEnd);
            if (_caret == next)
            {
                return;
            }

            _caret = next;
            OnPropertyChanged(nameof(Caret));
        }
    }

    /// <summary>
    /// Moves the insertion point, clamped to the drawing there is now. The two ways of naming a
    /// place are kept in step: the band measures an offset, the shape names a walk.
    /// </summary>
    public void SetCaret(int at)
    {
        Caret = Snap(at);
        var place = CaretPlaceFor(Caret);
        _draft.SetCaret(place.Path, place.After);
        RefreshOperatorState();
        OnPropertyChanged(nameof(CanPutValue));
        OnPropertyChanged(nameof(CanInsertTyped));
    }

    /// <summary>
    /// The nearest place a piece can be written at. A caret asked for inside a name is not a place
    /// the band can draw, and writing there would split the name; the line goes to the nearer edge,
    /// and a tie goes left so it never jumps past the piece it was pointed at.
    /// </summary>
    private int Snap(int at)
    {
        var clamped = Math.Clamp(at, 0, SpanEnd);
        var seams = Seams();
        if (seams.Count == 0)
        {
            return clamped;
        }

        var best = seams[0];
        foreach (var seam in seams)
        {
            if (Math.Abs(seam - clamped) < Math.Abs(best - clamped))
            {
                best = seam;
            }
        }

        return best;
    }

    /// <summary>
    /// Every offset a piece can be written at. A binary or unary is one unit here: its operands are
    /// not a sequence, so the only writable places are its outside edges and the holes inside it.
    /// </summary>
    public IReadOnlyList<int> Seams()
        => [.. WritableSeams().Select(seam => seam.At).Distinct().Order()];

    private readonly record struct CaretSeam(int At, NodePath Path, bool After, bool Hole);

    private List<CaretSeam> WritableSeams()
    {
        var seams = new List<CaretSeam>();
        if (Recipe is not null)
        {
            AddSeams(Recipe, seams);
        }

        return seams;
    }

    private static void AddSeams(SlotNode node, List<CaretSeam> seams)
    {
        switch (node)
        {
            case SlotRoot root:
                AddSeams(root.Child, seams);
                break;
            case SlotCall call:
                AddSeam(seams, call.Start, call.Path, after: false);
                AddSeam(seams, call.Start + call.Length, call.Path, after: true);
                foreach (var argument in call.Arguments)
                {
                    AddSeam(seams, argument.Start, argument.Path, after: false);
                    AddSeam(seams, argument.Start + argument.Length, argument.Path, after: true);
                    AddSeams(argument, seams);
                }

                if (call.Ghost is { Path: { } ghost } ghostEmpty)
                {
                    AddSeam(seams, ghostEmpty.Start, ghost, after: false, hole: true);
                }

                break;
            case SlotBinary binary:
                AddSeam(seams, binary.Start, binary.Path, after: false);
                AddSeam(seams, binary.Start + binary.Length, binary.Path, after: true);
                AddHoles(binary.Left, seams);
                AddHoles(binary.Right, seams);
                break;
            case SlotUnary unary:
                AddSeam(seams, unary.Start, unary.Path, after: false);
                AddSeam(seams, unary.Start + unary.Length, unary.Path, after: true);
                AddHoles(unary.Operand, seams);
                break;
            case SlotGroup group:
                AddSeam(seams, group.Start, group.Path, after: false);
                AddSeam(seams, group.Start + group.Length, group.Path, after: true);
                AddSeams(group.Inner, seams);
                break;
            case SlotList list:
                AddSeam(seams, list.Start, list.Path, after: false);
                AddSeam(seams, list.Start + list.Length, list.Path, after: true);
                foreach (var item in list.Items)
                {
                    AddSeam(seams, item.Start, item.Path, after: false);
                    AddSeam(seams, item.Start + item.Length, item.Path, after: true);
                    AddSeams(item, seams);
                }

                if (list.Ghost is { Path: { } listGhost, Start: var listGhostStart })
                {
                    AddSeam(seams, listGhostStart, listGhost, after: false, hole: true);
                }

                break;
            case SlotSequence sequence:
                AddSeam(seams, sequence.Start, sequence.Path, after: false);
                AddSeam(seams, sequence.Start + sequence.Length, sequence.Path, after: true);
                foreach (var item in sequence.Items)
                {
                    AddSeam(seams, item.Start, item.Path, after: false);
                    AddSeam(seams, item.Start + item.Length, item.Path, after: true);
                    AddSeams(item, seams);
                }

                break;
            case SlotEmpty empty:
                AddSeam(seams, empty.Start, empty.Path, after: false, hole: true);
                break;
        }
    }

    /// <summary>Finds holes even inside a structural binary, without making its operands writable.</summary>
    private static void AddHoles(SlotNode node, List<CaretSeam> seams)
    {
        switch (node)
        {
            case SlotCall call:
                foreach (var argument in call.Arguments)
                {
                    AddHoles(argument, seams);
                }

                if (call.Ghost is { Path: { } ghost, Start: var ghostStart })
                {
                    AddSeam(seams, ghostStart, ghost, after: false, hole: true);
                }

                break;
            case SlotBinary binary:
                AddHoles(binary.Left, seams);
                AddHoles(binary.Right, seams);
                break;
            case SlotUnary unary:
                AddHoles(unary.Operand, seams);
                break;
            case SlotGroup group:
                AddHoles(group.Inner, seams);
                break;
            case SlotList list:
                foreach (var item in list.Items)
                {
                    AddHoles(item, seams);
                }

                if (list.Ghost is { Path: { } listGhost, Start: var listGhostStart })
                {
                    AddSeam(seams, listGhostStart, listGhost, after: false, hole: true);
                }

                break;
            case SlotSequence sequence:
                foreach (var item in sequence.Items)
                {
                    AddHoles(item, seams);
                }

                break;
            case SlotEmpty empty:
                AddSeam(seams, empty.Start, empty.Path, after: false, hole: true);
                break;
        }
    }

    private static void AddSeam(List<CaretSeam> seams, int at, NodePath? path, bool after, bool hole = false)
    {
        if (path is not null)
        {
            seams.Add(new CaretSeam(at, path, after, hole));
        }
    }

    /// <summary>The one seam the numeric caret names, with its side of the piece made explicit.</summary>
    private (NodePath Path, bool After) CaretPlaceFor(int at)
    {
        var seams = WritableSeams();
        var hole = seams
            .Where(seam => seam.Hole && seam.At == at)
            .OrderByDescending(seam => seam.Path.Indices.Count)
            .FirstOrDefault();
        if (hole.Path is not null)
        {
            return (hole.Path, false);
        }

        if (at <= 0)
        {
            var before = seams
                .Where(seam => !seam.Hole && !seam.After && seam.At == at)
                .OrderBy(seam => seam.Path.Indices.Count)
                .FirstOrDefault();
            return (before.Path ?? NodePath.Root, false);
        }

        var ending = seams
            .Where(seam => !seam.Hole && seam.After && seam.At == at)
            .OrderBy(seam => seam.Path.Indices.Count)
            .ThenByDescending(seam => seam.At)
            .FirstOrDefault();
        if (ending.Path is not null)
        {
            return (ending.Path, true);
        }

        var starting = seams
            .Where(seam => !seam.Hole && !seam.After && seam.At == at)
            .OrderBy(seam => seam.Path.Indices.Count)
            .FirstOrDefault();
        if (starting.Path is not null)
        {
            return (starting.Path, false);
        }

        return (NodePath.Root, at >= SpanEnd);
    }

    /// <summary>
    /// Moves the line one place left or right, and says whether there was a place to move to. A place
    /// is a seam, not a character: the line is only ever drawn on a seam.
    /// </summary>
    public bool StepCaret(int direction)
    {
        var seams = Seams();
        var next = direction < 0
            ? seams.LastOrDefault(one => one < Caret, -1)
            : seams.FirstOrDefault(one => one > Caret, -1);

        if (next < 0)
        {
            return false;
        }

        SetCaret(next);
        return true;
    }

    public bool CanConfirm => Mode switch
    {
        ValueEditorMode.Name => Wall.Looking is not null || CanCreateName,
        ValueEditorMode.Text => true,
        _ => _reads || _draft.IsEmpty,
    };

    /// <summary>One short line next to the confirm button saying why it is grey.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlockReason))]
    [NotifyPropertyChangedFor(nameof(BlockReasonTip))]
    public partial string BlockReason { get; set; } = string.Empty;

    public bool HasBlockReason => BlockReason.Length > 0;

    /// <summary>Null when there is no reason, so the confirm button opens no wordless tooltip.</summary>
    public string? BlockReasonTip => HasBlockReason ? BlockReason : null;

    public bool CanUndo => ShowsBand && _draft.CanUndo;

    public bool CanRedo => ShowsBand && _draft.CanRedo;

    /// <summary>
    /// Goes back one edit. The history holds shapes, so a bound name is still bound when it comes
    /// back — a line of text could not carry that.
    /// </summary>
    public void Undo()
    {
        if (!ShowsBand || !_draft.CanUndo)
        {
            return;
        }

        _draft.Undo();
        RefreshFromDraft();
    }

    /// <summary>Goes forward again to the shape undo left behind.</summary>
    public void Redo()
    {
        if (!ShowsBand || !_draft.CanRedo)
        {
            return;
        }

        _draft.Redo();
        RefreshFromDraft();
    }

    /// <summary>True while there is something to take away in this mode.</summary>
    public bool CanClear => IsTextMode ? TextBody.Length > 0 : ShowsBand && !_draft.IsEmpty;

    /// <summary>Empties the value. A deliberate empty is one more step in the same history.</summary>
    public void Clear()
    {
        if (IsTextMode)
        {
            TextBody = string.Empty;
            return;
        }

        if (!ShowsBand)
        {
            return;
        }

        _draft.Clear();
        RefreshFromDraft();
        SetCaret(0);
    }
}
