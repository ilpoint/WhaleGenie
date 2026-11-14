using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One number of a place to look, edited the way any other number is: typed in as a number, or
/// written as an expression the engine works out while the macro runs. Which of the two boxes is
/// showing follows what is in it, so a place can be given exactly or worked out from what an earlier
/// step found without the dialog asking which one is meant.
/// </summary>
public partial class NumberCellViewModel : ViewModelBase
{
    /// <summary>True while a whole value is being put in at once, rather than typed a character at a time.</summary>
    private bool _writing;

    /// <summary>The value this cell keeps. The boxes and their line are drawn from it.</summary>
    private ExpressionNode _shape = Formula.Nothing;

    /// <summary>
    /// One number of a place to look: what it is called, one line saying what it counts from, and
    /// what the box says while it is empty. The last one is given rather than worked out because
    /// what an empty box means is a question about the place the numbers describe.
    /// </summary>
    public NumberCellViewModel(string label, string hint, string? placeholder = null,
        string? shortLabel = null)
        => (Label, Hint, _placeholder, _shortLabel) = (label, hint, placeholder, shortLabel);

    /// <summary>What the box says while it is empty, when the caller named one.</summary>
    private readonly string? _placeholder;

    /// <summary>
    /// What this number is called when the four of a rectangle are pressed onto one line: "X" beside
    /// a box says the same thing as "top-left X" above it, and only one of them fits.
    /// </summary>
    private readonly string? _shortLabel;

    /// <summary>What this number is called on the line it stands on.</summary>
    public string Label { get; }

    /// <summary>The name a single row of four numbers has room for.</summary>
    public string ShortLabel => _shortLabel ?? Label;

    /// <summary>One line saying which number this is and what it counts from.</summary>
    public string Hint { get; }

    /// <summary>The variables an expression written here may name, as the builder shows them.</summary>
    public IReadOnlyList<VariableChoice> Variables { get; private set; } = [];

    /// <summary>What is offered while typing here: the variables, and the functions of the language.</summary>
    public IReadOnlyList<string> Suggestions { get; private set; } = [];

    /// <summary>The names the expression is checked against.</summary>
    private IReadOnlyList<string> Named { get; set; } = [];

    /// <summary>
    /// What the expression box suggests when it is empty: what this particular number is, or what
    /// writing an expression there looks like.
    /// </summary>
    public string Placeholder => _placeholder ?? Strings.Get("Add.FormulaPlaceholder");

    /// <summary>The shape the value window starts from, which is the value this cell keeps.</summary>
    public ExpressionNode Seed => _shape;

    /// <summary>What this number says, whether it is a plain number or a formula.</summary>
    [ObservableProperty]
    public partial string Text { get; set; } = string.Empty;

    /// <summary>The number, while it is a number.</summary>
    [ObservableProperty]
    public partial decimal? Number { get; set; }

    /// <summary>True while it is written as an expression rather than dialled in.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlain))]
    public partial bool IsFormula { get; set; }

    /// <summary>True while the box holds a number rather than an expression.</summary>
    public bool IsPlain => !IsFormula;

    /// <summary>What the expression comes to, or why it cannot be read.</summary>
    [ObservableProperty]
    public partial string Message { get; set; } = string.Empty;

    /// <summary>True when the message is a problem rather than a result.</summary>
    [ObservableProperty]
    public partial bool IsError { get; set; }

    /// <summary>True when this number has been filled in at all.</summary>
    public bool HasValue => Text.Trim().Length > 0;

    /// <summary>
    /// Hands this cell what a formula written here may use. A name is offered as itself: the cell
    /// reads a bare word as a name, so no mark in front of it is needed or wanted. The names come
    /// from the step being edited rather than from the cell.
    /// </summary>
    public void Prepare(IReadOnlyList<VariableChoice> variables)
    {
        Variables = variables;
        Named = [.. variables.Select(choice => choice.Name)];
        Suggestions =
        [
            .. Named,
            .. Expression.Functions.Select(function => function.Name + "("),
        ];
    }

    /// <summary>
    /// Puts a value in, whichever way it is written. It is what the expression builder hands back:
    /// a plain number is a number and anything else is an expression, and this decides that from
    /// what is written rather than from what was here a moment ago.
    /// </summary>
    public void Write(ExpressionNode value) => Put(Expression.Render(value, bare: true), value);

    public void Write(string? value)
        => Put(value, Formula.FromFormula((value ?? string.Empty).Trim()));

    private void Put(string? value, ExpressionNode shape)
    {
        var written = (value ?? string.Empty).Trim();
        var formula = written.Length > 0 && !NumberField.IsNumberText(written);
        _shape = shape;
        _writing = true;
        try
        {
            Text = written;
            IsFormula = formula;
            Number = formula
                ? null
                : decimal.TryParse(written, NumberStyles.Number, CultureInfo.InvariantCulture,
                    out var number)
                        ? number
                        : null;
        }
        finally
        {
            _writing = false;
        }

        Check();
    }

    /// <summary>Puts a plain number in, which is what the region picker writes.</summary>
    public void SetNumber(decimal value)
    {
        _writing = true;
        try
        {
            _shape = Formula.Number((double)value);
            IsFormula = false;
            Number = value;
            Text = value.ToString("0.####", CultureInfo.InvariantCulture);
        }
        finally
        {
            _writing = false;
        }

        Message = string.Empty;
        IsError = false;
    }

    /// <summary>Puts what the macro file holds back in, without asking which box it belongs in.</summary>
    public void Load(string written)
    {
        var text = (written ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return;
        }

        if (NumberField.IsNumberText(text))
        {
            SetNumber(decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture));
            return;
        }

        Write(text);
    }

    /// <summary>Empties this number, which is what a row written as one variable leaves behind.</summary>
    public void Clear()
    {
        if (Text.Length == 0 && Number is null && !IsFormula)
        {
            return;
        }

        _writing = true;
        try
        {
            _shape = Formula.Nothing;
            Text = string.Empty;
            Number = null;
            IsFormula = false;
        }
        finally
        {
            _writing = false;
        }

        Message = string.Empty;
        IsError = false;
        OnPropertyChanged(nameof(HasValue));
    }

    partial void OnTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasValue));

        if (_writing)
        {
            return;
        }

        // Typing is the one place a line becomes a shape: the box is edited a character at a time,
        // and the value the cell keeps follows what the person wrote.
        _shape = Formula.FromFormula(value);

        // What is in the box decides which box it is: a plain number is the numbered one, and
        // clearing it goes back there, so the way back from a formula is simply to empty it.
        if (IsFormula && (value.Trim().Length == 0 || NumberField.IsNumberText(value)))
        {
            IsFormula = false;
            Number = decimal.TryParse(value.Trim(), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var number)
                    ? number
                    : null;
        }

        Check();
    }

    partial void OnNumberChanged(decimal? value)
    {
        if (_writing || IsFormula)
        {
            return;
        }

        _writing = true;
        try
        {
            _shape = value is { } number ? Formula.Number((double)number) : Formula.Nothing;
            Text = value is { } written
                ? written.ToString("0.####", CultureInfo.InvariantCulture)
                : string.Empty;
        }
        finally
        {
            _writing = false;
        }

        OnPropertyChanged(nameof(HasValue));
    }

    private void Check()
    {
        var (message, failed) = IsFormula
            ? NumberField.Check(Text, Named)
            : (string.Empty, false);
        IsError = failed;
        Message = message;
    }
}
