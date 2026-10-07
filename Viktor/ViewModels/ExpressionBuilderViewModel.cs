using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Viktor.Localization;

namespace Viktor.ViewModels;

/// <summary>
/// Backs the expression builder. Writing a formula by hand is easy to get wrong, so the dialog
/// puts every operator in front of the user as a button with a plain description beside it, keeps
/// the expression itself editable, and offers the variables the macro knows as a list to drop in.
/// </summary>
public partial class ExpressionBuilderViewModel : ViewModelBase
{
    /// <summary>The value the macro knows the names of, so a half-written formula can be checked.</summary>
    private readonly KnownVariables _known;

    public ExpressionBuilderViewModel()
        : this(string.Empty, [])
    {
    }

    public ExpressionBuilderViewModel(string expression, IReadOnlyList<string> variables)
    {
        Variables = variables;
        _known = new KnownVariables(variables);
        Operators = BuildOperators();
        Expression = expression ?? string.Empty;
    }

    /// <summary>Raised with the finished expression, or null when the dialog was dismissed.</summary>
    public event Action<string?>? CloseRequested;

    public string Header => Strings.Get("ExprBuilder.Title");

    public string Help => Strings.Get("ExprBuilder.Help");

    public string OperatorsHeader => Strings.Get("ExprBuilder.Operators");

    public string ExpressionHeader => Strings.Get("ExprBuilder.Expression");

    public string VariablesHeader => Strings.Get("ExprBuilder.Variables");

    public string InsertLabel => Strings.Get("ExprBuilder.Insert");

    public string Placeholder => Strings.Get("ExprBuilder.Placeholder");

    /// <summary>Every operator the expression language reads, each written the way it is inserted.</summary>
    public IReadOnlyList<ExpressionOperator> Operators { get; }

    /// <summary>Names offered to drop into the expression, always written with their dollar sign.</summary>
    public IReadOnlyList<string> Variables { get; }

    /// <summary>The expression being written. The window keeps the caret so buttons insert where it is.</summary>
    [ObservableProperty]
    public partial string Expression { get; set; } = string.Empty;

    /// <summary>The variable the list is pointing at, waiting to be added.</summary>
    [ObservableProperty]
    public partial string? Variable { get; set; }

    /// <summary>What the line under the box shows: the result, why it fails, or nothing yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowOk))]
    public partial string Message { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowError))]
    [NotifyPropertyChangedFor(nameof(ShowOk))]
    public partial bool HasError { get; set; }

    /// <summary>True while the line under the box is a problem worth flagging in red.</summary>
    public bool ShowError => HasError && Message.Length > 0;

    /// <summary>True while the line under the box is worth showing in green.</summary>
    public bool ShowOk => !HasError && Message.Length > 0;

    /// <summary>The text a variable button drops in, dollar sign and all.</summary>
    public string VariableToken => Variable is { Length: > 0 } name ? "$" + name : string.Empty;

    partial void OnExpressionChanged(string value) => Recheck();

    partial void OnVariableChanged(string? value) => OnPropertyChanged(nameof(VariableToken));

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(Expression);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    /// <summary>
    /// Says what the expression comes to, the same way the field under it does, so the user does
    /// not have to close the dialog to find out whether it reads.
    /// </summary>
    private void Recheck()
    {
        Message = string.Empty;
        HasError = false;

        if (string.IsNullOrWhiteSpace(Expression))
        {
            return;
        }

        if (!Viktor.Core.Expressions.Expression.TryEvaluate(Expression, _known, out var value,
                out var error))
        {
            HasError = true;
            Message = ExpressionText.Describe(error!);
            return;
        }

        Message = Expression.Contains('$')
            ? Strings.Get("Add.ExpressionValid")
            : Strings.Format("Add.ExpressionResult", value.AsText());
    }

    /// <summary>
    /// The operators, in the order the language reads them: arithmetic first, then comparison,
    /// then the words that join conditions. The text inserted is spaced out so two clicks make a
    /// formula that still reads after it is written.
    /// </summary>
    private static IReadOnlyList<ExpressionOperator> BuildOperators() =>
    [
        new("+", " + ", "Expr.Op.Add"),
        new("−", " - ", "Expr.Op.Subtract"),
        new("×", " * ", "Expr.Op.Multiply"),
        new("÷", " / ", "Expr.Op.Divide"),
        new("%", " % ", "Expr.Op.Remainder"),
        new("=", " == ", "Expr.Op.Equals"),
        new("≠", " != ", "Expr.Op.NotEquals"),
        new(">", " > ", "Expr.Op.Greater"),
        new("≥", " >= ", "Expr.Op.GreaterOrEqual"),
        new("<", " < ", "Expr.Op.Less"),
        new("≤", " <= ", "Expr.Op.LessOrEqual"),
        new("and", " and ", "Expr.Op.And"),
        new("or", " or ", "Expr.Op.Or"),
        new("not", "not ", "Expr.Op.Not"),
        new("( )", "()", "Expr.Op.Group"),
        new("[ ]", "[]", "Expr.Op.List"),
        new("\"{ }\"", "\"\"", "Expr.Op.Text"),
    ];
}

/// <summary>One operator button on the expression builder: what it looks like, what it inserts, and what it means.</summary>
public sealed class ExpressionOperator
{
    public ExpressionOperator(string token, string insert, string hintKey)
    {
        Token = token;
        Insert = insert;
        Hint = Strings.Get(hintKey);
    }

    /// <summary>The short sign shown on the button, such as <c>+</c> or <c>and</c>.</summary>
    public string Token { get; }

    /// <summary>The text dropped into the expression, spaced so it reads after insertion.</summary>
    public string Insert { get; }

    /// <summary>One plain line saying what the operator does.</summary>
    public string Hint { get; }
}
