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

        // An empty line is a value too: it is how a cell is cleared without leaving the window.
        if (_reads || Text.Trim().Length == 0)
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
        var reads = TryReadLine(written, out var parsed, out var problem, out var fault);
        _reads = reads;
        NoteIsProblem = written.Length > 0 && !_reads;
        Note = written.Length == 0
            ? Strings.Get("ValuePicker.Note.Empty")
            : _reads
                ? Strings.Get("ValuePicker.Note.Reads")
                : problem is not null
                    ? ExpressionText.Describe(problem)
                    : fault is { Length: > 0 }
                        ? fault
                        : Strings.Get("ExprBuilder.CannotRead");
        Saying = _reads && _bareNames && parsed is not null && ExpressionText.InChinese
            ? Strings.Get("ValuePicker.ReadsAs") + ExpressionPhrase.Say(parsed, VariableCatalog.Spoken)
            : string.Empty;

        RebuildRecipe();
        SlotDiagnostics.Mark(Recipe, problem, _table, _knownNames);
        BlockReason = _reads || written.Length == 0
            ? string.Empty
            : Strings.Get("ValuePicker.Confirm.Unreadable");

        OnPropertyChanged(nameof(Reads));
        OnPropertyChanged(nameof(CanConfirm));
        OnPropertyChanged(nameof(CanClear));
        OnPropertyChanged(nameof(HasSaying));
    }

    /// <summary>
    /// Reads one line the way the field will read it. This is the only place the window decides
    /// whether a line reads, so the band and the field cannot answer differently.
    /// </summary>
    private bool TryReadLine(string written, out ExpressionNode? parsed,
        out ExpressionException? problem, out string? fault)
    {
        parsed = null;
        problem = null;
        fault = null;

        if (written.Length == 0)
        {
            return false;
        }

        if (_readMode is ActionReadMode.Text)
        {
            var tree = FormulaText.Read(written, ActionReadMode.Text);
            if (tree is FaultNode bad)
            {
                fault = bad.Message;
            }
            else
            {
                parsed = tree;
            }
        }
        else if (_bareNames)
        {
            if (Language.TryParse(written, out parsed, out problem, bare: true))
            {
                problem = null;
            }
        }
        else if (Language.TryParse(written, out parsed, out problem))
        {
            problem = null;
        }
        else
        {
            // A line that is not a formula may still be the text the field holds. Only a line
            // that opens as a value and then fails — a half-written "$name +" — is a mistake
            // rather than prose.
            var tree = Formula.FromLine(written);
            if (tree is FaultNode bad)
            {
                fault = bad.Message;
            }
            else
            {
                parsed = tree;
                problem = null;
            }
        }

        return parsed is not null;
    }

    /// <summary>True when a line the window would hand back reads as the field reads it.</summary>
    private bool ReadsAsField(string written) => TryReadLine(written, out _, out _, out _);

    /// <summary>
    /// Reads the line into the shape the band draws. An empty formula is one empty place rather than
    /// nothing at all: the page says "a value goes here" instead of looking broken.
    /// </summary>
    private void RebuildRecipe()
    {
        Recipe = NamesOnly ? null : SlotReader.Read(Text, _table, _bareNames);
        RefreshStacking(SlotStacking.CompactWidthPx);
        OnPropertyChanged(nameof(HasRecipe));
        ClampCaret();
    }
}
