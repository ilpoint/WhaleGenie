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

// Making the picker: what it starts from, and the parts it is built out of.
public sealed partial class ValuePickerViewModel : ViewModelBase
{
    public ValuePickerViewModel(string? seed, IReadOnlyList<VariableChoice>? variables, bool namesOnly,
        string? target = null)
    {
        NamesOnly = namesOnly;
        Target = target ?? string.Empty;

        // The function table is what the structure band reads a line with, so it is in hand before
        // the line is set: setting the text is what asks the band to read it.
        _functions = [.. Language.Functions.Select(function => new ExpressionFunctionChoice(function))];
        _table = Language.Functions.ToDictionary(function => function.Name, StringComparer.OrdinalIgnoreCase);

        Text = seed ?? string.Empty;
        _caret = Text.Length;

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
        CheckText();
    }

    /// <summary>Raised with what the cell should hold, or null when the window was left alone.</summary>
    public event Action<string?>? CloseRequested;

    /// <summary>Raised with a step somebody asked to be shown, so the editor behind can look.</summary>
    public event Action<MacroStep>? Jumped;
}
