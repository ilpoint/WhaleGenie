using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

// Typing a plain number or word into the formula, and the quotes around it.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    /// <summary>True when the small field takes a number, false when it takes a piece of text.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTypingText))]
    public partial bool IsTypingNumber { get; set; }

    /// <summary>The other mode, for the button that is lit while the field takes text.</summary>
    public bool IsTypingText => !IsTypingNumber;

    [ObservableProperty]
    public partial string TypingText { get; set; } = string.Empty;

    /// <summary>Why what is typed cannot be put in, or empty while it can.</summary>
    [ObservableProperty]
    public partial string TypingNote { get; set; } = string.Empty;

    public bool HasTypingNote => TypingNote.Length > 0;

    public bool CanInsertTyped => TypedToken() is not null;

    partial void OnTypingTextChanged(string value) => RefreshTyped();

    /// <summary>
    /// The small field is always there, so a button only says what the field takes. Switching mode
    /// clears what was half typed in the other one: a number and a piece of text are written differently.
    /// </summary>
    public void SetTypingMode(bool number)
    {
        IsTypingNumber = number;
        TypingText = string.Empty;
        RefreshTyped();
    }

    /// <summary>Empties the field after an insert, so the next value can be typed straight away.</summary>
    public void ClearTyped()
    {
        TypingText = string.Empty;
        RefreshTyped();
    }

    /// <summary>
    /// Adds what was typed while the band had the focus. The band is a picture of the formula, not
    /// a text box, so a letter typed there belongs to the standing field rather than being lost.
    /// </summary>
    public void AppendTyped(string text)
    {
        if (text.Length > 0)
        {
            TypingText += text;
        }
    }

    /// <summary>
    /// What the strip would put into the formula, or null while what is typed is not something the
    /// language holds. Text goes in quoted, with the quotes and backslashes it contains escaped the
    /// way the language reads them back.
    /// </summary>
    public string? TypedToken()
    {
        if (!IsTypingNumber)
        {
            return TypingText.Length > 0 ? Quoted(TypingText) : null;
        }

        var written = TypingText.Trim();
        return written.Length > 0
               && Language.TryParse(written, out var tree, out _)
               && tree is NumberNode or UnaryNode { Operator: "-", Operand: NumberNode }
            ? written
            : null;
    }

    private void RefreshTyped()
    {
        TypingNote = IsTypingNumber && TypingText.Trim().Length > 0 && TypedToken() is null
            ? Strings.Get("ValuePicker.Value.BadNumber")
            : string.Empty;

        OnPropertyChanged(nameof(CanInsertTyped));
        OnPropertyChanged(nameof(HasTypingNote));
    }

    /// <summary>The text as the language reads it back: quoted, with what is inside it escaped.</summary>
    private static string Quoted(string text)
    {
        var builder = new StringBuilder("\"");
        foreach (var character in text)
        {
            builder.Append(character switch
            {
                '\\' => "\\\\",
                '"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => character.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }
}
