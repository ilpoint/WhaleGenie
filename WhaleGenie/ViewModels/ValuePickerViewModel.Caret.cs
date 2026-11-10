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
    /// <summary>
    /// Where the next piece is written, counted in characters of the formula. The band draws one
    /// thin line here and every palette writes at it; the text stays the only thing stored.
    /// </summary>
    public int Caret
    {
        get => _caret;
        private set
        {
            var next = Math.Clamp(value, 0, Text.Length);
            if (_caret == next)
            {
                return;
            }

            _caret = next;
            OnPropertyChanged(nameof(Caret));
        }
    }

    /// <summary>Moves the insertion point, clamped to the formula that is there now.</summary>
    public void SetCaret(int at) => Caret = at;

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

        var seams = new SortedSet<int> { 0, Text.Length };
        foreach (var piece in Recipe.Walk().Where(one => one.Length > 0))
        {
            seams.Add(piece.Start);
            seams.Add(piece.Start + piece.Length);
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
    private void ClampCaret() => Caret = _caret;

    public bool CanConfirm => NamesOnly ? Wall.Looking is not null : _reads;

    /// <summary>One short line next to the confirm button saying why it is grey.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBlockReason))]
    [NotifyPropertyChangedFor(nameof(BlockReasonTip))]
    public partial string BlockReason { get; set; } = string.Empty;

    public bool HasBlockReason => BlockReason.Length > 0;

    /// <summary>Null when there is no reason, so the confirm button opens no wordless tooltip.</summary>
    public string? BlockReasonTip => HasBlockReason ? BlockReason : null;

    private readonly Stack<string> _before = new();
    private readonly Stack<string> _after = new();
    private string? _last;
    private bool _replaying;

    public bool CanUndo => _before.Count > 0;

    public bool CanRedo => _after.Count > 0;

    /// <summary>
    /// Goes back one step. Every way of changing the line — typing, a piece from the strip, a cross,
    /// a drag — writes the text, so one place is enough to remember all of them.
    /// </summary>
    public void Undo()
    {
        if (_before.Count == 0)
        {
            return;
        }

        _after.Push(Text);
        Replay(_before.Pop());
    }

    /// <summary>Goes forward again to the line undo left behind.</summary>
    public void Redo()
    {
        if (_after.Count == 0)
        {
            return;
        }

        _before.Push(Text);
        Replay(_after.Pop());
    }

    private void Replay(string text)
    {
        _replaying = true;
        Text = text;
        _replaying = false;
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    private void Remember(string previous)
    {
        if (_replaying || previous == Text)
        {
            return;
        }

        _before.Push(previous);
        _after.Clear();
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
    }

    /// <summary>True while there is a formula to take away.</summary>
    public bool CanClear => Text.Length > 0;

    /// <summary>
    /// Empties the line. It is written through <see cref="Text"/> like every other edit, so clearing
    /// is one more step in the same history and undo brings the whole formula back.
    /// </summary>
    public void Clear()
    {
        SetCaret(0);
        Text = string.Empty;
    }
}
