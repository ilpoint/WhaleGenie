using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One colour an action leaves out of a comparison, as the form edits it: a colour written down,
/// taken with the magnifier, or the name of a variable that holds one. It is kept as it was typed
/// rather than parsed here, which is what lets it name a variable the macro fills in later.
/// </summary>
public partial class ColourRowViewModel : ViewModelBase
{
    public ColourRowViewModel(string colour = "") => Colour = colour;

    /// <summary>The colour this row stands for, as it is written: #RRGGBB, a name, or a formula.</summary>
    [ObservableProperty]
    public partial string Colour { get; set; }

    /// <summary>What takes this row out of the list it is in.</summary>
    public Action<ColourRowViewModel>? Take { get; set; }

    /// <summary>Takes this row out of the list.</summary>
    [RelayCommand]
    private void Remove() => Take?.Invoke(this);

    /// <summary>Puts a name where the caret of this row's box is, which is what the chip does.</summary>
    public void InsertVariable(string name, int caret)
    {
        var line = Colour ?? string.Empty;
        var at = Math.Clamp(caret, 0, line.Length);
        var before = line[..at];
        var after = line[at..];
        var space = before.Length > 0 && !before.EndsWith(' ') ? " " : string.Empty;
        Colour = before + space + name + after;
    }

    /// <summary>This row the way the step stores it, by the column names the catalogue declares.</summary>
    public StepParameterRow ToRow() => StepParameterRow.Of("colour", Stored());

    /// <summary>
    /// The cell as the step keeps it: a colour is text, and a name stays the name it is, so the
    /// colour a variable holds is read when the step runs rather than frozen when it was typed.
    /// </summary>
    private ExpressionNode Stored()
    {
        var written = Colour.Trim();
        return PixelColor.TryParse(written, out _) || !VariableName.IsName(written)
            ? Formula.Text(written)
            : Formula.Name(written);
    }

    /// <summary>Reads a row back out of a step that was saved with this list in it.</summary>
    public static ColourRowViewModel From(StepParameterRow row) => new(row.Text("colour").Trim());
}
