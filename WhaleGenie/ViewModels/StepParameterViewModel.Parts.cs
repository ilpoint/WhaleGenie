using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Expressions;

namespace WhaleGenie.ViewModels;

// The value as the pieces it is made of: text runs somebody types in, and the names between them.
public partial class StepParameterViewModel : ViewModelBase
{
    /// <summary>
    /// True while the pieces are writing themselves back into <see cref="Text"/>. Reading the text
    /// back into pieces at that moment would throw away the box the person is typing in, so the
    /// pieces only rebuild when the text came from somewhere else.
    /// </summary>
    private bool _composing;

    /// <summary>
    /// The value as the pieces come to it. A field edited as pieces keeps its value here and writes
    /// the line from it, rather than reading its own line back: the pieces are what the person is
    /// working with, and a line is only ever how the same value is said to whatever still asks for
    /// one — the value picker, a check, a log.
    /// </summary>
    private ExpressionNode _written = Formula.Nothing;

    /// <summary>Where the last edit was made, so a name picked from the wall lands beside it.</summary>
    private ValuePart? _activePart;

    private int _activeCaret;

    /// <summary>
    /// The value as the pieces it is made of. A name is one piece, and so is every run of text
    /// between two names: what tells a reader that a word is a variable is then the shape of it
    /// rather than a sign in front of it, and nothing has to be typed to make one.
    /// </summary>
    public ObservableCollection<ValuePart> Parts { get; } = [];

    /// <summary>
    /// True when the field is edited as pieces rather than as one line: only for a value that is a
    /// run of text with names in it. A field holding a formula stays one box, because a formula is
    /// not a sentence and its pieces are not runs of words.
    /// </summary>
    public bool ShowsParts => Parts.Any(part => part is NamePart);

    /// <summary>Reads the line into pieces again, with an empty run at the end to type in.</summary>
    private void RebuildParts()
    {
        if (_composing)
        {
            return;
        }

        // A part writes the value back as soon as it is made, so the pieces are held still while
        // they are being made: otherwise the first one made would write a value of its own and the
        // list would start again underneath itself.
        _composing = true;
        try
        {
            FillParts();
        }
        finally
        {
            _composing = false;
        }

        OnPropertyChanged(nameof(ShowsParts));
    }

    private void FillParts()
    {
        Parts.Clear();
        _activePart = null;
        _activeCaret = 0;

        var written = _written;
        if (written is not (TextNode or VariableNode or ConcatNode))
        {
            return;
        }

        foreach (var part in Flatten(written))
        {
            Parts.Add(part);
        }

        if (Parts.Count == 0 || Parts[^1] is not TextPart)
        {
            Parts.Add(NewText(string.Empty));
        }
    }

    private System.Collections.Generic.IEnumerable<ValuePart> Flatten(ExpressionNode node)
    {
        switch (node)
        {
            case TextNode text when text.Text.Length > 0:
                yield return NewText(text.Text);
                break;
            case VariableNode name:
                yield return NewName(name.Name);
                break;
            case ConcatNode run:
                foreach (var part in run.Parts.SelectMany(Flatten))
                {
                    yield return part;
                }

                break;
        }
    }

    /// <summary>One run of text, wired to write the value back when it changes.</summary>
    private TextPart NewText(string text) => new(text, Compose);

    /// <summary>One name, wired to take itself out when its cross is clicked.</summary>
    private NamePart NewName(string name)
    {
        NamePart? part = null;
        part = new NamePart { Name = name, Remove = new RelayCommand(() => Drop(part!)) };
        return part;
    }

    /// <summary>
    /// Writes the pieces back into the one line the field keeps. A run that says nothing is left
    /// out, so an empty place to type in never becomes part of the value.
    /// </summary>
    private void Compose()
    {
        if (_composing)
        {
            return;
        }

        var parts = Parts
            .Select(part => part switch
            {
                NamePart name => Formula.Name(name.Name),
                TextPart text when text.Text.Length > 0 => Formula.Text(text.Text),
                _ => null,
            })
            .OfType<ExpressionNode>()
            .ToList();

        _written = Formula.Run(parts);
        _composing = true;
        try
        {
            // The line is said from the value rather than the other way round. What still asks for
            // one — a check, a log, the value picker — reads it here; nothing writes back into it.
            Text = FormulaText.Write(_written);
        }
        finally
        {
            _composing = false;
        }
    }

    /// <summary>Remembers where the person last was, so a name lands where they were writing.</summary>
    public void RememberPlace(ValuePart part, int caret)
    {
        _activePart = part;
        _activeCaret = Math.Max(0, caret);
    }

    /// <summary>
    /// Puts a name into the value: at the place the person left, splitting the run of text they were
    /// in if they were in the middle of one. This is the only way a name gets in, and it never goes
    /// through the text — the value keeps a shape, not a spelling.
    /// </summary>
    public void InsertName(string name, int? caret = null)
    {
        if (!ShowsParts)
        {
            // A field holding one formula or one piece of text has no pieces to put a name between,
            // so it takes the name the way it always did: as the line it is written as.
            InsertVariable("$" + name, caret ?? (Text ?? string.Empty).Length);
            return;
        }

        var at = _activePart is null ? -1 : IndexOf(_activePart);
        if (at < 0)
        {
            at = Math.Max(0, Parts.Count - 1);
        }

        var place = caret ?? _activeCaret;
        if (Parts[at] is TextPart text && place > 0 && place < text.Text.Length)
        {
            var tail = text.Text[place..];
            text.Text = text.Text[..place];
            Parts.Insert(at + 1, NewName(name));
            Parts.Insert(at + 2, NewText(tail));
        }
        else
        {
            Parts.Insert(at + 1, NewName(name));
        }

        if (Parts[^1] is not TextPart)
        {
            Parts.Add(NewText(string.Empty));
        }

        _activePart = Parts[Math.Min(at + 1, Parts.Count - 1)];
        Compose();
        OnPropertyChanged(nameof(ShowsParts));
    }

    /// <summary>Takes one piece out, joining the two runs of text it stood between.</summary>
    private void Drop(ValuePart part)
    {
        var at = IndexOf(part);
        if (at < 0)
        {
            return;
        }

        Parts.RemoveAt(at);
        Join(at - 1);
        Compose();
        OnPropertyChanged(nameof(ShowsParts));
    }

    /// <summary>
    /// Backspace at the very start of a run: the piece before it goes, and the run takes its place.
    /// Nothing when there is nothing before it.
    /// </summary>
    public (ValuePart Part, int Caret)? JoinBack(ValuePart part)
    {
        var at = IndexOf(part);
        if (at <= 0)
        {
            return null;
        }

        var before = Parts[at - 1];
        if (before is TextPart previous && part is TextPart current)
        {
            var caret = previous.Text.Length;
            current.Text = previous.Text + current.Text;
            Parts.RemoveAt(at - 1);
            Compose();
            return (current, caret);
        }

        Parts.RemoveAt(at - 1);
        Join(at - 2);
        Compose();
        OnPropertyChanged(nameof(ShowsParts));
        return (Parts[Math.Max(0, at - 1)], 0);
    }

    /// <summary>Delete at the very end of a run: the piece after it goes the same way.</summary>
    public (ValuePart Part, int Caret)? JoinForward(ValuePart part)
    {
        var at = IndexOf(part);
        if (at < 0 || at >= Parts.Count - 1)
        {
            return null;
        }

        var after = Parts[at + 1];
        if (after is TextPart next && part is TextPart current)
        {
            current.Text += next.Text;
            Parts.RemoveAt(at + 1);
            Compose();
            return (current, current.Text.Length - next.Text.Length);
        }

        Parts.RemoveAt(at + 1);
        Join(at);
        Compose();
        OnPropertyChanged(nameof(ShowsParts));
        return (Parts[Math.Min(at, Parts.Count - 1)], 0);
    }

    /// <summary>Joins the two runs on either side of <paramref name="at"/> when both are text.</summary>
    private void Join(int at)
    {
        if (at < 0 || at + 1 >= Parts.Count)
        {
            return;
        }

        if (Parts[at] is TextPart left && Parts[at + 1] is TextPart right)
        {
            left.Text += right.Text;
            Parts.RemoveAt(at + 1);
        }

        if (Parts.Count == 0 || Parts[^1] is not TextPart)
        {
            Parts.Add(NewText(string.Empty));
        }
    }

    private int IndexOf(ValuePart part)
    {
        for (var index = 0; index < Parts.Count; index++)
        {
            if (ReferenceEquals(Parts[index], part))
            {
                return index;
            }
        }

        return -1;
    }
}
