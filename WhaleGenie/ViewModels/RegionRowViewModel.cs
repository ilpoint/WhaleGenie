using System;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One of the places a step looks at, as the dialog edits it: either the four numbers of a
/// rectangle, which the region picker fills in from a drag on the screen, or the name of a
/// variable that says where to look instead — a picture taken earlier, or a rectangle held in a
/// variable. Both are one row, because both answer the same question.
/// </summary>
public partial class RegionRowViewModel : ViewModelBase
{
    /// <summary>True when this row names a variable rather than giving four numbers.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWritten))]
    [NotifyPropertyChangedFor(nameof(Written))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial bool IsFromVariable { get; set; }

    /// <summary>True when this row gives the rectangle as four numbers.</summary>
    public bool IsWritten => !IsFromVariable;

    /// <summary>
    /// True when the field holds this one row and no other, which is what a step that reads or
    /// copies one part of the screen asks for: there is nothing to take away, because a step with
    /// nowhere to look is not a step.
    /// </summary>
    public bool IsSingle { get; set; }

    /// <summary>
    /// True when this row says nothing at all: no numbers and no variable. Such a row is not a
    /// place, and a step that looked there would look at nothing.
    /// </summary>
    public bool IsEmpty => IsFromVariable
        ? Named.Trim().Length == 0
        : X is null && Y is null && Width is null && Height is null;

    /// <summary>
    /// This row written as one rectangle, <c>x,y,width,height</c>, or nothing when it names a
    /// variable. It is how a row reads back on the basic page, where the four numbers are not
    /// edited, and how a rectangle written before this field existed is read.
    /// </summary>
    public string Written => IsFromVariable
        ? string.Empty
        : string.Join(',', new[]
        {
            WrittenNumber(X), WrittenNumber(Y), WrittenNumber(Width), WrittenNumber(Height),
        });

    /// <summary>A row made out of a rectangle written as four numbers.</summary>
    public static RegionRowViewModel FromRectangle(string rectangle)
    {
        var parts = rectangle.Split(',', StringSplitOptions.TrimEntries);
        var row = new RegionRowViewModel();
        if (parts.Length == 4)
        {
            row.X = Number(parts[0]);
            row.Y = Number(parts[1]);
            row.Width = Number(parts[2]);
            row.Height = Number(parts[3]);
        }

        return row;
    }

    private static string WrittenNumber(decimal? value)
        => (value ?? 0m).ToString("0.####", CultureInfo.InvariantCulture);

    private static decimal? Number(string text)
        => decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Written))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial decimal? X { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Written))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial decimal? Y { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Written))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial decimal? Width { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Written))]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial decimal? Height { get; set; }

    /// <summary>The variable this row names, when it names one.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial string Named { get; set; } = string.Empty;

    /// <summary>
    /// What takes this row out of the list it is in. The list belongs to the parameter, so the row
    /// is told what to call rather than knowing where it is.
    /// </summary>
    public Action<RegionRowViewModel>? Take { get; set; }

    /// <summary>Writes where to look as four numbers rather than as a variable.</summary>
    [RelayCommand]
    private void UseNumbers() => IsFromVariable = false;

    /// <summary>Hands the question of where to look over to a variable.</summary>
    [RelayCommand]
    private void UseVariable() => IsFromVariable = true;

    /// <summary>Takes this row out of the list.</summary>
    [RelayCommand]
    private void Remove() => Take?.Invoke(this);

    /// <summary>Writes a rectangle dragged on the screen into this row.</summary>
    public void Put(int x, int y, int width, int height)
    {
        IsFromVariable = false;
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// This row the way the step stores it. A column with nothing in it is left out: an empty
    /// number is not a rectangle, and writing it down as zero would make one out of nothing.
    /// </summary>
    public StepParameterRow ToRow()
    {
        if (IsFromVariable)
        {
            return StepParameterRow.Of("text", Named.Trim());
        }

        var row = new StepParameterRow();
        Keep(row, "x", X);
        Keep(row, "y", Y);
        Keep(row, "width", Width);
        Keep(row, "height", Height);
        return row;
    }

    /// <summary>One row back out of the macro file.</summary>
    public static RegionRowViewModel From(StepParameterRow row)
    {
        var named = row.Text("text");
        if (named.Length > 0)
        {
            return new RegionRowViewModel { IsFromVariable = true, Named = named };
        }

        return new RegionRowViewModel
        {
            X = Number(row, "x"),
            Y = Number(row, "y"),
            Width = Number(row, "width"),
            Height = Number(row, "height"),
        };
    }

    private static void Keep(StepParameterRow row, string column, decimal? value)
    {
        if (value is not null)
        {
            row.Columns[column] = value.Value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static decimal? Number(StepParameterRow row, string column)
        => decimal.TryParse(row.Text(column), NumberStyles.Number, CultureInfo.InvariantCulture,
            out var number)
                ? number
                : null;
}
