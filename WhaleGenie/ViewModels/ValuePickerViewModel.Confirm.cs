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

// What the window answers: the check it makes, and the recipe it rebuilds.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    /// <summary>Hands back what the cell should hold: the line, or the one name it wanted.</summary>
    public void Confirm()
    {
        if (NamesOnly)
        {
            if (Wall.Looking is { } chosen)
            {
                CloseRequested?.Invoke(chosen.Token);
            }

            return;
        }

        if (_reads)
        {
            CloseRequested?.Invoke(Text.Trim());
        }
    }

    /// <summary>Hands back one name, for a cell that holds nothing else.</summary>
    public void Choose(string token) => CloseRequested?.Invoke(token);

    public void Cancel() => CloseRequested?.Invoke(null);

    /// <summary>Why the formula does not read, or that it does. Empty is not a mistake yet.</summary>
    private void CheckText()
    {
        if (NamesOnly)
        {
            return;
        }

        var written = Text.Trim();
        ExpressionException? problem = null;
        if (written.Length == 0)
        {
            _reads = false;
            NoteIsProblem = false;
            Note = Strings.Get("ValuePicker.Note.Empty");
            Saying = string.Empty;
        }
        else if (Language.TryParse(written, out var parsed, out var error))
        {
            _reads = true;
            NoteIsProblem = false;
            Note = Strings.Get("ValuePicker.Note.Reads");
            Saying = ExpressionText.InChinese
                ? Strings.Get("ValuePicker.ReadsAs") + ExpressionPhrase.Say(parsed!, VariableCatalog.Spoken)
                : string.Empty;
        }
        else
        {
            _reads = false;
            NoteIsProblem = true;
            problem = error;
            Note = error is null
                ? Strings.Get("ExprBuilder.CannotRead")
                : ExpressionText.Describe(error);
            Saying = string.Empty;
        }

        RebuildRecipe();
        SlotDiagnostics.Mark(Recipe, problem, _table);
        BlockReason = written.Length == 0
            ? Strings.Get("ValuePicker.Confirm.Empty")
            : _reads ? string.Empty : Strings.Get("ValuePicker.Confirm.Unreadable");

        OnPropertyChanged(nameof(Reads));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(HasSaying));
    }

    /// <summary>
    /// Reads the line into the shape the band draws. An empty formula is one empty place rather than
    /// nothing at all: the page says "a value goes here" instead of looking broken.
    /// </summary>
    private void RebuildRecipe()
    {
        Recipe = NamesOnly ? null : SlotReader.Read(Text, _table);
        RefreshStacking(SlotStacking.CompactWidthPx);
        OnPropertyChanged(nameof(HasRecipe));
        ClampCaret();
    }
}
