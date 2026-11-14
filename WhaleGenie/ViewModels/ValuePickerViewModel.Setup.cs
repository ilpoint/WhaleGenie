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
    /// Opens the window on a shape. The line is drawn once for the editor, then the shape itself is
    /// put back as the value: a line cannot carry the identity a name is bound to, and confirming
    /// hands the shape over rather than reading the line again.
    /// </summary>
    public ValuePickerViewModel(ExpressionNode seed, IReadOnlyList<VariableChoice>? variables,
        ActionValueForm valueForm, bool bareNames = false, string? target = null)
        : this(SeedLine(seed, bareNames), variables, valueForm, bareNames, target)
    {
        // The line is only a drawing of the seed. Put the shape itself back so a bound name keeps
        // the identity no line can carry, then redraw the band from it.
        _draft.Replace(seed);
        CheckText();
        SetCaret(SpanEnd);
    }

    /// <summary>
    /// The one place a shape is drawn as a line, and only because the window still edits one. The
    /// line is written in the spelling the window will read it back with: bare names when it reads
    /// bare names, the marked spelling otherwise. A shape that says nothing opens an empty window
    /// rather than one holding the quoted empty word a renderer would draw for it. This is a bridge
    /// to be removed when the window keeps the shape itself while editing.
    /// </summary>
    internal static string SeedLine(ExpressionNode? seed, bool bareNames)
        => seed is null || Formula.IsNothing(seed)
            ? string.Empty
            : bareNames
                ? Language.Render(seed)
                : Formula.Speak(seed);

    public ValuePickerViewModel(string? seed, IReadOnlyList<VariableChoice>? variables,
        ActionValueForm valueForm, bool bareNames = false, string? target = null)
    {
        _valueForm = valueForm;
        _bareNames = bareNames;
        NamesOnly = valueForm == ActionValueForm.Name;
        Target = target ?? string.Empty;

        foreach (var variable in variables ?? [])
        {
            _knownNames.Add(variable.Name);
        }

        // The function table is what the structure band reads a line with, so it is in hand before
        // the line is set: setting the text is what asks the band to read it.
        _functions = [.. Language.Functions.Select(function => new ExpressionFunctionChoice(function))];
        _table = Language.Functions.ToDictionary(function => function.Name, StringComparer.OrdinalIgnoreCase);
        _draft = new ValueDraft(Formula.Nothing, _table);

        // Opening a window is not an edit. The seed line is read once into the shape, then kept as
        // the line a person may go on typing into; the shape is the value from here on.
        _draft.Replace(ReadLine(seed ?? string.Empty));
        _composing = true;
        try
        {
            Text = seed ?? string.Empty;
        }
        finally
        {
            _composing = false;
        }

        _caret = SpanEnd;

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
        // The standing line opens at the end of the drawing. Set it through the shape so the two
        // ways of naming a place agree before the first piece is put in.
        SetCaret(SpanEnd);
    }

    /// <summary>Raised with what the cell should hold, or a cancelled pick when it was left alone.</summary>
    public event Action<ValuePick>? CloseRequested;

    /// <summary>Raised with a step somebody asked to be shown, so the editor behind can look.</summary>
    public event Action<MacroStep>? Jumped;
}
