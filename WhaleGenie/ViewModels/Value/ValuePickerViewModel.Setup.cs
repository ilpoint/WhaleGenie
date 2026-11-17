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

// Making the picker: what it starts from, and the parts it is built out of.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    /// <summary>
    /// Opens the window on a shape. The shape is the only value the window keeps: the line under
    /// the band is drawn from it and given back to the eye, never read back into the value.
    /// </summary>
    public ValuePickerViewModel(ExpressionNode seed, IReadOnlyList<VariableChoice>? variables,
        ActionValueForm valueForm, string? target = null, bool allowNewName = false)
    {
        Mode = valueForm switch
        {
            ActionValueForm.Name => ValueEditorMode.Name,
            ActionValueForm.Text => ValueEditorMode.Text,
            ActionValueForm.Run => ValueEditorMode.Run,
            _ => ValueEditorMode.Value,
        };
        AllowNewName = allowNewName && Mode is ValueEditorMode.Name;
        Target = target ?? string.Empty;

        foreach (var variable in variables ?? [])
        {
            _knownNames.Add(variable.Name);
        }

        _functions = [.. Language.Functions.Select(function => new ExpressionFunctionChoice(function))];
        _table = Language.Functions.ToDictionary(function => function.Name, StringComparer.OrdinalIgnoreCase);
        _draft = new ValueDraft(Formula.Nothing, _table);
        _draft.Replace(seed);

        // The wall keeps what a flat list cannot: the steps that read a name, and the way to one.
        // Its own search and filters are shown, because the variables panel is that wall.
        Wall = new VariableWallViewModel(variables) { ShowsDetails = false };
        Wall.Jumped += step => Jumped?.Invoke(step);
        Wall.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(VariableWallViewModel.Looking))
            {
                // One material is being described at a time: picking a name lets go of the card
                // that was looked at before, and the other way round.
                if (Wall.Looking is not null)
                {
                    SelectedFunction = null;
                }

                OnPropertyChanged(nameof(HasChosenVariable));
                OnPropertyChanged(nameof(CanConfirm));
            }
            else if (args.PropertyName == nameof(VariableWallViewModel.SearchText))
            {
                // The name list filters as the person types; the same word is the candidate for a
                // name this macro does not have yet.
                OnPropertyChanged(nameof(NewName));
                OnPropertyChanged(nameof(CanCreateName));
                OnPropertyChanged(nameof(NewNameLine));
                OnPropertyChanged(nameof(CanConfirm));
            }
        };

        Operators = OperatorPalette.Build();
        // A number is typed, and text has its own sign in the operator panel; what is left is the
        // three words a formula cannot be typed from memory as easily as it can be clicked.
        Values =
        [
            new ValueToken(Strings.Get("ExprBuilder.Value.True"), "true"),
            new ValueToken(Strings.Get("ExprBuilder.Value.False"), "false"),
            new ValueToken(Strings.Get("ExprBuilder.Value.Null"), "null"),
        ];

        FunctionGroups = BuildGroups();
        SelectedFunctionGroup = FunctionGroups[0];
        RefreshFunctions();

        // A text field edits one body of text; the band and the small field are not part of it.
        if (IsTextMode)
        {
            TextBody = seed is TextNode body ? body.Text : string.Empty;
        }

        // A field that holds one name opens on the name it already has, so the list shows what is
        // selected and a new name can be typed over it.
        if (IsNameMode && seed is VariableNode named)
        {
            Wall.SearchText = named.Name;
            Wall.Looking = Wall.Shown.FirstOrDefault(
                choice => string.Equals(choice.Name, named.Name, StringComparison.Ordinal));
        }

        // A number is the more common thing to type in a formula; a run is written as words.
        IsTypingNumber = IsValueMode;

        // Draw the line once from the shape and put the caret at its end. From here on every edit
        // goes through the shape, and the line follows it.
        RefreshFromDraft();
        SetCaret(SpanEnd);
    }

    /// <summary>Raised with what the cell should hold, or a cancelled pick when it was left alone.</summary>
    public event Action<ValuePick>? CloseRequested;

    /// <summary>Raised with a step somebody asked to be shown, so the editor behind can look.</summary>
    public event Action<MacroStep>? Jumped;
}
