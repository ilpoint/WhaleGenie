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
        ActionValueForm valueForm, string? target = null)
    {
        NamesOnly = valueForm == ActionValueForm.Name;
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
        };

        Operators = SplitBrackets(OperatorPalette.Build());
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
        // A number is the more common thing to type, so the standing field opens in that mode.
        IsTypingNumber = true;

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
