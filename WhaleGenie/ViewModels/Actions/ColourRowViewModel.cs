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
/// One colour a step leaves out of a comparison, as the form edits it: the colour itself, and how
/// far a pixel may sit from it and still count as that colour. Both cells are kept as they were
/// typed rather than parsed here, which is what lets either of them name a variable.
/// </summary>
public partial class ColourRowViewModel : ViewModelBase
{
    public ColourRowViewModel(string colour = "", string tolerance = "")
    {
        Colour = colour;
        Tolerance = tolerance;
    }

    /// <summary>The colour this row stands for, as it is written: #RRGGBB, a name, or a formula.</summary>
    [ObservableProperty]
    public partial string Colour { get; set; }

    /// <summary>
    /// How far a pixel may sit from this colour, in per cent, or nothing to use the tolerance the
    /// field gives every colour.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowsTheField))]
    public partial string Tolerance { get; set; }

    /// <summary>True when this row brings no reach of its own and follows the field's.</summary>
    public bool FollowsTheField => Tolerance.Trim().Length == 0;

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
    public StepParameterRow ToRow()
    {
        var row = StepParameterRow.Of("colour", Stored());
        row.Set("tolerance", Formula.Text(Tolerance.Trim()));
        return row;
    }

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
    public static ColourRowViewModel From(StepParameterRow row)
        => new(row.Text("colour").Trim(), row.Text("tolerance").Trim());
}
