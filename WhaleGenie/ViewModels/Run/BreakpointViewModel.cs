using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// The little panel a breakpoint's condition is written in: one expression, one count, and the
/// names the run knows, offered so a condition can be built instead of remembered. It is not the
/// action editor: a condition is a question about the run, not a step of the macro.
/// </summary>
public sealed partial class BreakpointViewModel : ObservableObject
{
    /// <summary>What the panel was closed with.</summary>
    public enum Ending
    {
        Save,
        Clear,
        Cancel,
    }

    private ExpressionNode _expression = Formula.Nothing;

    public BreakpointViewModel(BreakpointRule? current, IReadOnlyList<VariableChoice> variables)
    {
        // The names come first: the box checks itself as soon as it is given a value, and it checks
        // against them.
        Choices = variables;
        Variables = [.. variables.Select(choice => choice.Name).OrderBy(name => name,
            StringComparer.OrdinalIgnoreCase)];
        HitTimes = current?.HitTimes ?? 1;
        _expression = current?.Expression ?? Formula.Nothing;

        // The same names and the same functions the action form offers in a condition field: a
        // condition is written with the same words wherever it is written.
        ExpressionSuggestions =
        [
            .. Variables,
            .. WhaleGenie.Core.Expressions.Expression.Functions
                .Select(function => function.Name + "("),
        ];

        CheckExpression();
    }

    /// <summary>The names and their scopes, which the builder and the checker both work from.</summary>
    public IReadOnlyList<VariableChoice> Choices { get; }

    /// <summary>What the box completes to while it is being typed in.</summary>
    public IReadOnlyList<string> ExpressionSuggestions { get; }

    /// <summary>What the line under the box says, or nothing while the box is empty or reads.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExpressionError))]
    [NotifyPropertyChangedFor(nameof(ShowExpressionSuccess))]
    public partial string ExpressionMessage { get; set; } = string.Empty;

    /// <summary>True while the message describes a mistake rather than a reading.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExpressionError))]
    [NotifyPropertyChangedFor(nameof(ShowExpressionSuccess))]
    public partial bool HasExpressionError { get; set; }

    public bool ShowExpressionError => HasExpressionError && ExpressionMessage.Length > 0;

    public bool ShowExpressionSuccess => !HasExpressionError && ExpressionMessage.Length > 0;

    /// <summary>
    /// The condition as the shape it will be kept as. This is the one value the panel owns: the
    /// line in the box is drawn from it and is never read back into it.
    /// </summary>
    public ExpressionNode Expression
    {
        get => _expression;
        set
        {
            if (Equals(_expression, value))
            {
                return;
            }

            _expression = value;
            OnPropertyChanged(nameof(Expression));
            OnPropertyChanged(nameof(ExpressionLine));
            CheckExpression();
        }
    }

    /// <summary>
    /// What the box shows, said for a person. Typing here is a plain value: the words are kept as
    /// they are, with nothing in them read as a name or a formula. A condition made of pieces is
    /// edited in the value window instead, so changing its words cannot quietly flatten a tree.
    /// </summary>
    public string ExpressionLine
    {
        get => ExpressionPhrase.Display(_expression);
        set
        {
            if (!Formula.IsPlain(_expression))
            {
                return;
            }

            Expression = Formula.Text(value ?? string.Empty);
        }
    }

    /// <summary>
    /// The one check every expression field in the program gets: what it comes to when that can be
    /// worked out here, and why it cannot be read when the mistake is certain.
    /// </summary>
    private void CheckExpression()
    {
        var (message, failed) = NumberField.Check(_expression, Variables);
        ExpressionMessage = message;
        HasExpressionError = failed;
    }

    /// <summary>
    /// How many arrivals count before the run stops. Kept as a decimal because the box that edits it
    /// is one, and turned back into a count when the panel is saved.
    /// </summary>
    [ObservableProperty]
    public partial decimal HitTimes { get; set; } = 1;

    /// <summary>The names a condition may use, which are the ones the macro's own fields offer.</summary>
    public IReadOnlyList<string> Variables { get; }

    /// <summary>What the panel was closed with.</summary>
    public Ending How { get; private set; } = Ending.Cancel;

    /// <summary>The rule to keep, or nothing when the breakpoint was put back to stopping always.</summary>
    public BreakpointRule? Rule => How is Ending.Save
        ? new BreakpointRule
        {
            Expression = _expression,
            HitTimes = Math.Max(1, (int)HitTimes),
        }
        : null;

    /// <summary>Raised once the panel is finished, whichever way it ended.</summary>
    public event Action? Finished;

    [RelayCommand]
    private void Save()
    {
        How = Ending.Save;
        Finished?.Invoke();
    }

    [RelayCommand]
    private void Clear()
    {
        How = Ending.Clear;
        Finished?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        How = Ending.Cancel;
        Finished?.Invoke();
    }
}
