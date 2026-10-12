using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One number of a place to look, edited the way any other number is: dialled in with the spinner,
/// or written as an expression the engine works out while the macro runs. Which of the two boxes is
/// showing follows what is in it, so a place can be given exactly or worked out from what an earlier
/// step found without the dialog asking which one is meant.
/// </summary>
public partial class NumberCellViewModel : ViewModelBase
{
    /// <summary>True while a whole value is being put in at once, rather than typed a character at a time.</summary>
    private bool _writing;

    /// <summary>
    /// One number of a place to look: what it is called, one line saying what it counts from, and
    /// what the box says while it is empty. The last one is given rather than worked out because
    /// what an empty box means is a question about the place the numbers describe.
    /// </summary>
    public NumberCellViewModel(string label, string hint, string? placeholder = null)
        => (Label, Hint, _placeholder) = (label, hint, placeholder);

    /// <summary>What the box says while it is empty, when the caller named one.</summary>
    private readonly string? _placeholder;

    /// <summary>What this number is called on the line it stands on.</summary>
    public string Label { get; }

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

    /// <summary>True while the spinner is showing.</summary>
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
    /// Hands this cell what a formula written here may use. The names come from the step being
    /// edited rather than from the cell, which is why they are given to it rather than asked for.
    /// </summary>
    public void Prepare(IReadOnlyList<VariableChoice> variables)
    {
        Variables = variables;
        Named = [.. variables.Select(choice => choice.Name)];
        Suggestions =
        [
            .. Named.Select(name => "$" + name),
            .. Expression.Functions.Select(function => function.Name + "("),
        ];
    }

    /// <summary>
    /// Puts a value in, whichever way it is written. It is what the expression builder hands back:
    /// a plain number is a number and anything else is an expression, and this decides that from
    /// what is written rather than from what was here a moment ago.
    /// </summary>
    public void Write(string? value)
    {
        var written = (value ?? string.Empty).Trim();
        var formula = written.Length > 0 && !NumberField.IsNumberText(written);
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

        // What is in the box decides which box it is: a plain number is dialled in, and clearing it
        // leaves the spinner there, so the way back from a formula is simply to empty it.
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
            Text = value is { } number
                ? number.ToString("0.####", CultureInfo.InvariantCulture)
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
