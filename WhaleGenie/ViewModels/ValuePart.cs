using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One piece of a value while it is being edited: a run of text somebody can type in, or a name
/// standing between two runs.
///
/// <para>
/// The list of these is the field's editing surface, and the text of the whole value is written
/// from it. A run of text is a box like any other box, so what a person types there is text —
/// a dollar sign among it is a dollar sign — and a name is only ever made by the picker, never by
/// spelling something. That is what leaves the language with no character to type.
/// </para>
/// </summary>
public abstract class ValuePart : ViewModelBase;

/// <summary>A run of text, edited in place.</summary>
public sealed partial class TextPart : ValuePart
{
    private readonly Action _changed;

    public TextPart(string text, Action changed)
    {
        _changed = changed;
        Text = text;
    }

    [ObservableProperty]
    public partial string Text { get; set; }

    /// <summary>True for the run nobody has typed in yet, which is drawn as the place to start.</summary>
    public bool IsBlank => Text.Length == 0;

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsBlank));
        _changed();
    }
}

/// <summary>A name the value reads, drawn as a box of its own with the way to take it out.</summary>
public sealed class NamePart : ValuePart
{
    public required string Name { get; init; }

    /// <summary>Takes this name, and the text it stood between, back out of the value.</summary>
    public required ICommand Remove { get; init; }
}
