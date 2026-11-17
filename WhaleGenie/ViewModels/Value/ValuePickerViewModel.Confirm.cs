using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

// The language itself, spelled out: the property below is the text being edited, which hides the
// static class of the same name inside this file.
using Language = WhaleGenie.Core.Expressions.Expression;

namespace WhaleGenie.ViewModels;

// What the window answers: the check it makes, and the band it draws from the shape.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    /// <summary>Hands back the shape the cell should hold, or the one name it wanted.</summary>
    public void Confirm()
    {
        if (IsNameMode)
        {
            if (Wall.Looking is { } chosen)
            {
                CloseRequested?.Invoke(new ValuePick(Formula.Name(chosen.Name), true));
            }
            else
            {
                CreateName();
            }

            return;
        }

        if (IsTextMode)
        {
            // A text field holds one body of text. An empty body is how the cell is cleared.
            CloseRequested?.Invoke(new ValuePick(
                TextBody.Length == 0 ? Formula.Nothing : Formula.Text(TextBody), true));
            return;
        }

        // An empty value is a value too: it is how a cell is cleared without leaving the window.
        // The shape is the answer, not a second reading of the line, so a name bound to a
        // declaration keeps the identity a line cannot carry.
        if (_reads || _draft.IsEmpty)
        {
            CloseRequested?.Invoke(new ValuePick(_draft.Value, true));
        }
    }

    /// <summary>
    /// Hands back one name, for a cell that holds nothing else. The choice carries the identity of
    /// the declaration it came from; no line with a mark in front of the name is needed to say it.
    /// </summary>
    public void Choose(VariableChoice choice)
        => CloseRequested?.Invoke(new ValuePick(Formula.Name(choice.Name), true));

    public void Cancel() => CloseRequested?.Invoke(ValuePick.Cancelled);

    /// <summary>
    /// Why the shape cannot be handed over, or that it can. Nothing here reads the line back into a
    /// second value: an unfinished shape (a hole) is the one thing a cell cannot take yet.
    /// </summary>
    private void CheckText()
    {
        if (!ShowsBand)
        {
            return;
        }

        var empty = _draft.IsEmpty;
        // A piece that still reads as a fault is just as unfinished as a hole, and a cell
        // cannot take either until somebody fills it in. Removing a required argument no longer
        // leaves a hole, so the missing argument count has to be asked separately.
        var hole = _draft.HasHole() || HoldsFault(_draft.Value) || HasMissingArgument(_draft.Value);
        _reads = !hole && !empty;
        NoteIsProblem = hole;
        Note = empty
            ? Strings.Get(IsRunMode ? "ValuePicker.Note.Empty.Run" : "ValuePicker.Note.Empty")
            : _reads
                ? Strings.Get(IsRunMode ? "ValuePicker.Note.Reads.Run" : "ValuePicker.Note.Reads")
                : Strings.Get("ExprBuilder.CannotRead");
        Saying = IsValueMode && _reads && ExpressionText.InChinese
            ? Strings.Get("ValuePicker.ReadsAs") + ExpressionPhrase.Say(_draft.Value, VariableCatalog.Spoken)
            : string.Empty;

        RebuildRecipe();
        SlotDiagnostics.Mark(Recipe, null, _table, _knownNames);
        BlockReason = hole
            ? Strings.Get("ValuePicker.Confirm.Unreadable")
            : string.Empty;

        OnPropertyChanged(nameof(Reads));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(HasSaying));
    }

    /// <summary>True while a place in the value still says it could not be read.</summary>
    private static bool HoldsFault(ExpressionNode node) => node switch
    {
        HoleNode => true,
        ConcatNode run => run.Parts.Any(HoldsFault),
        ListNode list => list.Items.Any(HoldsFault),
        CallNode call => call.Arguments.Any(HoldsFault),
        GroupNode group => HoldsFault(group.Inner),
        UnaryNode unary => HoldsFault(unary.Operand),
        BinaryNode binary => HoldsFault(binary.Left) || HoldsFault(binary.Right),
        _ => false,
    };

    /// <summary>True when a call is short of an argument its function requires.</summary>
    private bool HasMissingArgument(ExpressionNode node) => node switch
    {
        ConcatNode run => run.Parts.Any(HasMissingArgument),
        ListNode list => list.Items.Any(HasMissingArgument),
        CallNode call => (_table.TryGetValue(call.Name, out var entry)
                             && call.Arguments.Count < entry.MinArguments)
                         || call.Arguments.Any(HasMissingArgument),
        GroupNode group => HasMissingArgument(group.Inner),
        UnaryNode unary => HasMissingArgument(unary.Operand),
        BinaryNode binary => HasMissingArgument(binary.Left) || HasMissingArgument(binary.Right),
        _ => false,
    };

    /// <summary>The shape drawn as the line under the band. A picture, never the value.</summary>
    private string DraftLine()
        => _draft.IsEmpty ? string.Empty : Language.Render(_draft.Value);

    /// <summary>Redraws the line and the band after an edit that changed the shape.</summary>
    private void RefreshFromDraft(bool syncCaret = true)
    {
        Text = DraftLine();
        CheckText();
        if (syncCaret)
        {
            SyncCaret();
        }

        RefreshOperatorState();
        OnPropertyChanged(nameof(IsValueEmpty));
        OnPropertyChanged(nameof(EmptyBandHint));
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(CanClear));
    }

    /// <summary>
    /// An edit may leave the caret inside a hole it just made, or in the first argument of a call it
    /// just wrote. The shape already knows where that is; draw the standing line there so the next
    /// piece typed lands in the same place without guessing from an offset.
    /// </summary>
    private void SyncCaret()
    {
        if (Recipe is null)
        {
            return;
        }

        var offset = Math.Clamp(_draft.CaretOffset, 0, SpanEnd);
        if (_caret != offset)
        {
            _caret = offset;
            OnPropertyChanged(nameof(Caret));
        }

        // Put/Cut may leave a path on the last piece even when the line is at the end of the run.
        // Canonicalize the seam after every edit so "at the end" means the whole run's end, not
        // the last piece's start; the next operator then wraps the run rather than that one piece.
        // A path that vanished with an undo is kept for the redo that brings it back, instead of
        // being replaced by the current numeric seam.
        var path = _draft.Caret;
        var exists = Recipe.Walk().Any(piece => piece.Path is { } piecePath
                && piecePath.Equals(path))
            || Recipe.Walk().OfType<SlotCall>().Any(call => call.Ghost?.Path is { } ghost
                && ghost.Equals(path));
        if (exists)
        {
            var place = CaretPlaceFor(offset);
            _draft.SetCaret(place.Path, place.After);
        }
    }

    /// <summary>
    /// Draws the band from the shape. Each piece carries the walk to its place in the value and is
    /// shown as the name or word it is, with no sign in front of it — the shape already says which
    /// is which. A shape has no function-name or bracket tokens, so trying to align a line's
    /// tokens to it would put a function's name on its argument.
    /// </summary>
    private void RebuildRecipe()
    {
        Recipe = ShowsBand ? Band(_draft.Recipe) : null;
        ApplySelection();
        RefreshStacking(SlotStacking.CompactWidthPx);
        OnPropertyChanged(nameof(HasRecipe));
    }

    /// <summary>
    /// The band's root. A run is a sequence of words and names; a whole value is one expression, so
    /// it sits under a root that is not itself a piece and cannot be dragged or taken away.
    /// </summary>
    private SlotNode Band(SlotNode root) => IsRunMode
        ? root is SlotSequence ? root : new SlotSequence { Items = [root], Path = NodePath.Root }
        : new SlotRoot { Child = root, Path = NodePath.Root };

    /// <summary>The words a dragged piece shows while it follows the pointer.</summary>
    public string PieceLabel(SlotNode piece) => piece switch
    {
        SlotValue value => value.Text,
        SlotEmpty empty => empty.Text,
        SlotCall call => call.Name,
        SlotBinary or SlotUnary or SlotGroup or SlotList => piece.Source is { } source
            ? Formula.Speak(source)
            : string.Empty,
        _ => string.Empty,
    };
}
