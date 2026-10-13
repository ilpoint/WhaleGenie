using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

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

    public ExpressionBuilderViewModel(string expression, IReadOnlyList<VariableChoice> variables)
    {
        Variables = variables;
        _known = new KnownVariables([.. variables.Select(choice => choice.Name)]);
        Wall = new VariableWallViewModel(variables);
        Operators = BuildOperators();
        Functions = BuildFunctions();
        Expression = expression ?? string.Empty;
    }

    /// <summary>Raised with the finished expression, or null when the dialog was dismissed.</summary>
    public event Action<string?>? CloseRequested;

    public string Header => Strings.Get("ExprBuilder.Title");

    public string Help => Strings.Get("ExprBuilder.Help");

    public string OperatorsHeader => Strings.Get("ExprBuilder.Operators");

    public string ExpressionHeader => Strings.Get("ExprBuilder.Expression");

    public string VariablesHeader => Strings.Get("ExprBuilder.Variables");

    public string FunctionsHeader => Strings.Get("ExprBuilder.Functions");

    public string FunctionsFilterHint => Strings.Get("ExprBuilder.FunctionsFilter");

    public string InsertLabel => Strings.Get("ExprBuilder.Insert");

    public string Placeholder => Strings.Get("ExprBuilder.Placeholder");

    /// <summary>Every operator the expression language reads, each written the way it is inserted.</summary>
    public IReadOnlyList<ExpressionOperator> Operators { get; }

    /// <summary>Every variable the macro can read, with what a person choosing one needs to know.</summary>
    public IReadOnlyList<VariableChoice> Variables { get; }

    /// <summary>
    /// The built-in functions, one heading to a group, which is what the filter above them narrows
    /// down. Narrowing rebuilds the list rather than hiding rows, so a heading whose every function
    /// was filtered out goes away with them rather than standing over nothing.
    /// </summary>
    public ObservableCollection<ExpressionFunctionGroup> Functions { get; }

    /// <summary>The wall of variable buttons, which drops the one that was picked in at the caret.</summary>
    public VariableWallViewModel Wall { get; }

    /// <summary>The expression being written. The window keeps the caret so buttons insert where it is.</summary>
    [ObservableProperty]
    public partial string Expression { get; set; } = string.Empty;

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

    /// <summary>What is typed into the filter above the function list, empty to show them all.</summary>
    [ObservableProperty]
    public partial string FunctionFilter { get; set; } = string.Empty;

    partial void OnExpressionChanged(string value) => Recheck();

    partial void OnFunctionFilterChanged(string value) => Refill();

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

        if (!WhaleGenie.Core.Expressions.Expression.TryEvaluate(Expression, _known, out var value,
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

    /// <summary>
    /// Every built-in function, in the order the table is written: the groups come out in the order
    /// their first function does, which is the order the manual lists them in as well.
    /// </summary>
    private static ObservableCollection<ExpressionFunctionGroup> BuildFunctions()
    {
        var groups = new List<ExpressionFunctionGroup>();
        // Written out in full because this dialog's own Expression property is the text being
        // edited, which hides the static class of the same name.
        foreach (var function in WhaleGenie.Core.Expressions.Expression.Functions)
        {
            var group = groups.LastOrDefault(entry => entry.Group == function.Group);
            if (group is null)
            {
                group = new ExpressionFunctionGroup(function.Group,
                    Strings.Get($"Expr.Group.{function.Group}"));
                groups.Add(group);
            }

            group.Functions.Add(new ExpressionFunctionChoice(function));
        }

        return [.. groups];
    }

    /// <summary>
    /// Narrows the list to the functions whose name or signature holds what was typed. Nothing
    /// typed is the whole table, which is what the dialog opens with.
    /// </summary>
    private void Refill()
    {
        var tables = BuildFunctions();
        var wanted = FunctionFilter.Trim();
        Functions.Clear();
        foreach (var group in tables)
        {
            group.Functions = [.. group.Functions.Where(function =>
                wanted.Length == 0
                || function.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase)
                || function.Signature.Contains(wanted, StringComparison.OrdinalIgnoreCase))];
            if (group.Functions.Count > 0)
            {
                Functions.Add(group);
            }
        }
    }
}

/// <summary>One heading of the function list, and the functions written under it.</summary>
public sealed class ExpressionFunctionGroup(string group, string header)
{
    /// <summary>The key every function of this group shares, such as <c>text</c>.</summary>
    public string Group { get; } = group;

    /// <summary>What the group is called in the reader's own language.</summary>
    public string Header { get; } = header;

    /// <summary>The functions written under this heading.</summary>
    public List<ExpressionFunctionChoice> Functions { get; set; } = [];
}

/// <summary>
/// One function button: what it is called, how it is written, and the text one click drops into the
/// expression. The description the manual carries is left out — it is written in English, and the
/// signature is a piece of code rather than a sentence, so the signature is what the button shows.
/// </summary>
public sealed class ExpressionFunctionChoice
{
    public ExpressionFunctionChoice(ExpressionFunction function)
    {
        Name = function.Name;
        Signature = function.Signature;
        Insert = function.Name + "(";
    }

    /// <summary>The name the function is called by.</summary>
    public string Name { get; }

    /// <summary>How it is written, with the arguments it takes.</summary>
    public string Signature { get; }

    /// <summary>What one click leaves in the expression, with the caret after the bracket.</summary>
    public string Insert { get; }
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
