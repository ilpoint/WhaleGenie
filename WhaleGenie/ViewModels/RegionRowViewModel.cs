using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One of the places a step looks at, as the dialog edits it: the four numbers of a rectangle,
/// which the region picker fills in from a drag on the screen, or the name of a variable that says
/// where to look instead — a picture taken earlier, or a rectangle held in a variable. A row says
/// one or the other, never both, so there is nothing to choose between before typing.
/// </summary>
public partial class RegionRowViewModel : ViewModelBase
{
    /// <summary>True while a whole row is being built or emptied, rather than edited by hand.</summary>
    private bool _writing;

    public RegionRowViewModel()
    {
        // The four numbers stand for the same thing a step reads directly, so an empty box says
        // what an empty box says there: the side reaches the edge rather than meaning nothing.
        var empty = Strings.Get("Add.EmptyReachesTheEdge");
        X = new NumberCellViewModel(Strings.Get("Add.RegionX"), Strings.Get("Add.RegionXHint"), empty);
        Y = new NumberCellViewModel(Strings.Get("Add.RegionY"), Strings.Get("Add.RegionYHint"), empty);
        Width = new NumberCellViewModel(Strings.Get("Add.RegionWidth"),
            Strings.Get("Add.RegionWidthHint"), empty);
        Height = new NumberCellViewModel(Strings.Get("Add.RegionHeight"),
            Strings.Get("Add.RegionHeightHint"), empty);
        Cells = [X, Y, Width, Height];

        foreach (var cell in Cells)
        {
            cell.PropertyChanged += OnCellChanged;
        }
    }

    /// <summary>How far the left edge of this place is from the left of what it is counted from.</summary>
    public NumberCellViewModel X { get; }

    /// <summary>How far its top edge is from the top of what it is counted from.</summary>
    public NumberCellViewModel Y { get; }

    /// <summary>How wide it is.</summary>
    public NumberCellViewModel Width { get; }

    /// <summary>How tall it is.</summary>
    public NumberCellViewModel Height { get; }

    /// <summary>The four numbers of this place, in the order they are written on the line.</summary>
    public IReadOnlyList<NumberCellViewModel> Cells { get; }

    /// <summary>
    /// The variable that says where to look instead of the four numbers, or nothing while the four
    /// numbers are what says it.
    /// </summary>
    [ObservableProperty]
    public partial string OrVariable { get; set; } = string.Empty;

    /// <summary>What the one-variable box suggests when it is empty.</summary>
    public string VariablePlaceholder => Strings.Get("Add.RegionOrVariablePlaceholder");

    /// <summary>What the one-variable box is for.</summary>
    public string VariableHint => Strings.Get("Add.RegionOrVariableHint");

    /// <summary>True when one of the numbers is written as an expression rather than dialled in.</summary>
    public bool HasFormula => Cells.Any(cell => cell.IsFormula);

    /// <summary>What a number written as an expression says, or why it cannot be read.</summary>
    public string CellMessage =>
        (Cells.FirstOrDefault(cell => cell.IsError && cell.Message.Length > 0)
            ?? Cells.FirstOrDefault(cell => cell.Message.Length > 0))?.Message ?? string.Empty;

    /// <summary>True while that message is a problem worth flagging in red.</summary>
    public bool ShowCellError => Cells.Any(cell => cell.IsError && cell.Message.Length > 0);

    /// <summary>True while the message is a result worth showing in green.</summary>
    public bool ShowCellResult => !ShowCellError && CellMessage.Length > 0;

    /// <summary>
    /// What takes this row out of the list it is in. The list belongs to the parameter, so the row
    /// is told what to call rather than knowing where it is.
    /// </summary>
    public Action<RegionRowViewModel>? Take { get; set; }

    /// <summary>Takes this row out of the list.</summary>
    [RelayCommand]
    private void Remove() => Take?.Invoke(this);

    /// <summary>Hands the row what a formula written in it may name.</summary>
    public void Prepare(IReadOnlyList<VariableChoice> variables)
    {
        foreach (var cell in Cells)
        {
            cell.Prepare(variables);
        }
    }

    /// <summary>Writes a rectangle dragged on the screen into this row.</summary>
    public void Put(int x, int y, int width, int height)
    {
        _writing = true;
        try
        {
            OrVariable = string.Empty;
            X.SetNumber(x);
            Y.SetNumber(y);
            Width.SetNumber(width);
            Height.SetNumber(height);
        }
        finally
        {
            _writing = false;
        }

        Announce();
    }

    /// <summary>Writes a variable that says where to look into this row, where the caret is.</summary>
    public void InsertVariable(string name, int caret)
    {
        var current = OrVariable ?? string.Empty;
        var index = Math.Clamp(caret, 0, current.Length);
        OrVariable = current[..index] + name + current[index..];
    }

    /// <summary>
    /// This row the way the step stores it. A row written as one variable says so in a column of its
    /// own; otherwise it is the four numbers that were filled in, and a column left empty is left
    /// out rather than written as zero, because a zero is a place rather than none.
    /// </summary>
    public StepParameterRow ToRow()
    {
        var row = new StepParameterRow();
        var named = OrVariable.Trim();
        if (named.Length > 0)
        {
            row.Columns["text"] = named;
            return row;
        }

        Keep(row, "x", X);
        Keep(row, "y", Y);
        Keep(row, FieldNames.Width, Width);
        Keep(row, FieldNames.Height, Height);
        return row;
    }

    /// <summary>One row back out of the macro file.</summary>
    public static RegionRowViewModel From(StepParameterRow row)
    {
        var place = new RegionRowViewModel();
        var named = row.Text("text");

        // A row that names something is a row that names something: the name is the whole answer,
        // and four numbers beside it would be a second one.
        if (named.Trim().Length > 0)
        {
            place.OrVariable = named.Trim();
            return place;
        }

        place.X.Load(row.Text("x"));
        place.Y.Load(row.Text("y"));
        place.Width.Load(row.Text(FieldNames.Width));
        place.Height.Load(row.Text(FieldNames.Height));
        return place;
    }

    private static void Keep(StepParameterRow row, string column, NumberCellViewModel cell)
    {
        var written = cell.Text.Trim();
        if (written.Length > 0)
        {
            row.Columns[column] = written;
        }
    }

    /// <summary>
    /// A place is said one way at a time: naming a variable takes the four numbers out, and writing
    /// a number takes the name out. A row therefore never carries an answer nobody meant.
    /// </summary>
    partial void OnOrVariableChanged(string value)
    {
        if (_writing || value.Trim().Length == 0)
        {
            Announce();
            return;
        }

        _writing = true;
        try
        {
            foreach (var cell in Cells)
            {
                cell.Clear();
            }
        }
        finally
        {
            _writing = false;
        }

        Announce();
    }

    private void OnCellChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_writing)
        {
            return;
        }

        if (e.PropertyName is nameof(NumberCellViewModel.Text) && Cells.Any(cell => cell.HasValue))
        {
            _writing = true;
            try
            {
                OrVariable = string.Empty;
            }
            finally
            {
                _writing = false;
            }
        }

        Announce();
    }

    /// <summary>
    /// Says that this row has changed. The dialog follows the field rather than its rows, so a row
    /// that was filled in has to be reported as a change to the field it is a row of.
    /// </summary>
    private void Announce()
    {
        OnPropertyChanged(nameof(HasFormula));
        OnPropertyChanged(nameof(CellMessage));
        OnPropertyChanged(nameof(ShowCellError));
        OnPropertyChanged(nameof(ShowCellResult));
    }
}
