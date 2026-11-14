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
        if (NamesOnly)
        {
            if (Wall.Looking is { } chosen)
            {
                CloseRequested?.Invoke(new ValuePick(Formula.Name(chosen.Name, chosen.Id), true));
            }

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
        => CloseRequested?.Invoke(new ValuePick(Formula.Name(choice.Name, choice.Id), true));

    public void Cancel() => CloseRequested?.Invoke(ValuePick.Cancelled);

    /// <summary>
    /// Why the shape cannot be handed over, or that it can. Nothing here reads the line back into a
    /// second value: an unfinished shape (a hole) is the one thing a cell cannot take yet.
    /// </summary>
    private void CheckText()
    {
        if (NamesOnly)
        {
            return;
        }

        var empty = _draft.IsEmpty;
        // A piece that still reads as a fault is just as unfinished as a hole, and a cell
        // cannot take either until somebody fills it in.
        var hole = _draft.HasHole() || HoldsFault(_draft.Value);
        _reads = !hole && !empty;
        NoteIsProblem = hole;
        Note = empty
            ? Strings.Get("ValuePicker.Note.Empty")
            : _reads
                ? Strings.Get("ValuePicker.Note.Reads")
                : Strings.Get("ExprBuilder.CannotRead");
        Saying = _reads && ExpressionText.InChinese
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
        UnaryNode unary => HoldsFault(unary.Operand),
        BinaryNode binary => HoldsFault(binary.Left) || HoldsFault(binary.Right),
        _ => false,
    };

    /// <summary>The shape drawn as the line under the band. A picture, never the value.</summary>
    private string DraftLine()
        => _draft.IsEmpty ? string.Empty : Language.Render(_draft.Value);

    /// <summary>Redraws the line and the band after an edit that changed the shape.</summary>
    private void RefreshFromDraft()
    {
        Text = DraftLine();
        CheckText();
        SyncCaret();
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

        var match = Recipe.Walk().FirstOrDefault(piece =>
            piece.Path is { } path && path.Equals(_draft.Caret));
        if (match is null)
        {
            return;
        }

        _caret = match.Start;
        OnPropertyChanged(nameof(Caret));
    }

    /// <summary>
    /// Draws the band from the shape. Each piece carries the walk to its place in the value and is
    /// shown as the name or word it is, with no sign in front of it — the shape already says which
    /// is which. A shape has no function-name or bracket tokens, so trying to align a line's
    /// tokens to it would put a function's name on its argument.
    /// </summary>
    private void RebuildRecipe()
    {
        Recipe = NamesOnly ? null : Band(_draft.Recipe);
        RefreshStacking(SlotStacking.CompactWidthPx);
        OnPropertyChanged(nameof(HasRecipe));
        ClampCaret();
    }

    /// <summary>A value that is one piece is still drawn as a run of one, so the band has edges.</summary>
    private static SlotNode Band(SlotNode root)
        => root is SlotSequence
            ? root
            : new SlotSequence { Items = [root], Path = NodePath.Root };

    /// <summary>
    /// The place in the value that the standing line names. The band measures a caret as an offset
    /// into the drawing; the shape names the same place as a walk from its root.
    /// </summary>
    private NodePath PathForCaret()
    {
        if (Recipe is null)
        {
            return NodePath.Root;
        }

        // The ghost at the end of a call is drawn but is not one of its arguments, so it is not in
        // the walk. It is the one place a click can name that needs its own answer.
        foreach (var call in Recipe.Walk().OfType<SlotCall>())
        {
            if (call.Ghost is { Path: { } ghost, Start: var start } && start == Caret)
            {
                return ghost;
            }
        }

        var pieces = Recipe.Walk().Where(piece => piece.Path is not null).ToList();
        var exact = pieces.Where(piece => piece.Start == Caret)
            .OrderByDescending(piece => piece.Path!.Indices.Count)
            .FirstOrDefault();
        if (exact?.Path is { } found)
        {
            return found;
        }

        return pieces.Where(piece => piece.Start + piece.Length <= Caret)
            .OrderByDescending(piece => piece.Start + piece.Length)
            .ThenByDescending(piece => piece.Path!.Indices.Count)
            .FirstOrDefault()?.Path ?? NodePath.Root;
    }

    /// <summary>The words a dragged piece shows while it follows the pointer.</summary>
    public string PieceLabel(SlotNode piece) => piece switch
    {
        SlotValue value => value.Text,
        SlotOperator sign => sign.Text,
        SlotBracket bracket => bracket.Text,
        SlotEmpty empty => empty.Text,
        SlotCall call => call.Name,
        _ => string.Empty,
    };
}
