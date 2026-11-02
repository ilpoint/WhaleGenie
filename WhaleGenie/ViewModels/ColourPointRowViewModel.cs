using System;
using System.Linq;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One colour a pattern wants around its base colour, as the form edits it: how far right and how
/// far down from the base colour it has to be, and which colour has to be there. Three cells rather
/// than one line of punctuation, because a pattern is a list of places and a list is read one row at
/// a time.
/// </summary>
/// <remarks>
/// A cell is kept as it was typed rather than parsed here: a cell may be a number, a variable or a
/// sum of them, which is what lets a pattern be moved by a number another step worked out. The
/// engine reads the same text and works out what it means.
/// </remarks>
public partial class ColourPointRowViewModel : ViewModelBase
{
    public ColourPointRowViewModel(string dx = "", string dy = "", string colour = "",
        string tolerance = "", bool mustNot = false)
    {
        Dx = dx;
        Dy = dy;
        Colour = colour;
        Tolerance = tolerance;
        MustNot = mustNot;
    }

    /// <summary>How far right of the base colour this one has to be, in pixels.</summary>
    [ObservableProperty]
    public partial string Dx { get; set; }

    /// <summary>How far below the base colour this one has to be, in pixels.</summary>
    [ObservableProperty]
    public partial string Dy { get; set; }

    /// <summary>
    /// The colour that has to be there, written as #RRGGBB. More than one may be written, with a bar
    /// between them, for a shape that is drawn in more than one shade: the place is satisfied by any
    /// of them, which is one place rather than several patterns.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Colours))]
    public partial string Colour { get; set; }

    /// <summary>True when this place has to be none of those colours rather than one of them.</summary>
    [ObservableProperty]
    public partial bool MustNot { get; set; }

    /// <summary>
    /// The colours of this place one by one, for the swatches beside the box: reading them off the
    /// text is what shows that a bar was understood and not left as punctuation nobody noticed.
    /// </summary>
    /// <remarks>
    /// A cell may hold a variable instead of a colour, and there is nothing to paint for one of
    /// those: it is left out rather than shown as an empty square, which reads as a broken colour.
    /// </remarks>
    public IReadOnlyList<string> Colours => [.. (Colour ?? string.Empty)
        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Where(LooksLikeColour)];

    private static bool LooksLikeColour(string token)
    {
        var value = token.Trim().TrimStart('#');
        return value.Length is 3 or 6 && value.All(char.IsAsciiHexDigit);
    }

    /// <summary>
    /// How far this colour may sit from the one written, in per cent, or nothing to use the same
    /// tolerance as the base colour. An anchor that has to be exact often sits beside shapes whose
    /// shading drifts, so one number for all of them is either too loose or too tight.
    /// </summary>
    [ObservableProperty]
    public partial string Tolerance { get; set; }

    /// <summary>True when this row says all three of the things a pattern needs from it.</summary>
    public bool IsComplete => Dx.Trim().Length > 0 && Dy.Trim().Length > 0 && Colour.Trim().Length > 0;

    /// <summary>
    /// What takes this row out of the list it is in. The list belongs to the parameter, so the row
    /// is told what to call rather than knowing where it is.
    /// </summary>
    public Action<ColourPointRowViewModel>? Take { get; set; }

    /// <summary>Takes this row out of the list.</summary>
    [RelayCommand]
    private void Remove() => Take?.Invoke(this);

    /// <summary>This row the way the step stores it, by the column names the catalogue declares.</summary>
    public StepParameterRow ToRow() => new()
    {
        Columns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["dx"] = Dx.Trim(),
            ["dy"] = Dy.Trim(),
            ["colour"] = Colour.Trim(),
            ["mustNot"] = MustNot ? "true" : string.Empty,
            ["tolerance"] = Tolerance.Trim(),
        },
    };

    /// <summary>Reads a row back out of a step that was saved with this pattern in it.</summary>
    public static ColourPointRowViewModel From(StepParameterRow row)
        => new(row.Text("dx").Trim(), row.Text("dy").Trim(), row.Text("colour").Trim(),
            row.Text("tolerance").Trim(),
            string.Equals(row.Text("mustNot").Trim(), "true", StringComparison.OrdinalIgnoreCase));
}
