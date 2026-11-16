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
        _draft.SetCaret(PathForCaret());
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
    /// Every offset a piece begins or ends at, plus both ends of the line, in order. These are the
    /// places the standing line can stand: an offset inside a name is nowhere a piece could be
    /// written, so stepping one character at a time would leave the line standing still.
    /// </summary>
    public IReadOnlyList<int> Seams()
    {
        if (Recipe is null)
        {
            return [];
        }

        var seams = new SortedSet<int> { 0, SpanEnd };
        foreach (var piece in Recipe.Walk())
        {
            // A place with nothing in it is still a place: the space between the brackets of an
            // empty call is where its first argument goes, and it has no characters to measure.
            seams.Add(piece.Start);
            seams.Add(piece.Start + piece.Length);
            if (piece is SlotCall { Ghost: { } ghost })
            {
                seams.Add(ghost.Start);
            }
        }

        return [.. seams];
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

    /// <summary>Keeps the insertion point legal after the text changed under it.</summary>
    private void ClampCaret() => Caret = Snap(_caret);

    public bool CanConfirm => NamesOnly
        ? Wall.Looking is not null
        : _reads || _draft.IsEmpty;

    /// <summary>One short line next to the confirm button saying why it is grey.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlockReason))]
    [NotifyPropertyChangedFor(nameof(BlockReasonTip))]
    public partial string BlockReason { get; set; } = string.Empty;

    public bool HasBlockReason => BlockReason.Length > 0;

    /// <summary>Null when there is no reason, so the confirm button opens no wordless tooltip.</summary>
    public string? BlockReasonTip => HasBlockReason ? BlockReason : null;

    public bool CanUndo => _draft.CanUndo;

    public bool CanRedo => _draft.CanRedo;

    /// <summary>
    /// Goes back one edit. The history holds shapes, so a bound name is still bound when it comes
    /// back — a line of text could not carry that.
    /// </summary>
    public void Undo()
    {
        if (!_draft.CanUndo)
        {
            return;
        }

        _draft.Undo();
        RefreshFromDraft();
    }

    /// <summary>Goes forward again to the shape undo left behind.</summary>
    public void Redo()
    {
        if (!_draft.CanRedo)
        {
            return;
        }

        _draft.Redo();
        RefreshFromDraft();
    }

    /// <summary>True while there is a value to take away.</summary>
    public bool CanClear => !_draft.IsEmpty;

    /// <summary>Empties the value. A deliberate empty is one more step in the same history.</summary>
    public void Clear()
    {
        _draft.Clear();
        RefreshFromDraft();
        SetCaret(0);
    }
}
