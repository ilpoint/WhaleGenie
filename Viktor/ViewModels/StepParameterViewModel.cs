using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Viktor.Core.Expressions;
using Viktor.Models;
using Viktor.Localization;

namespace Viktor.ViewModels;

/// <summary>
/// Editable wrapper around one <see cref="ActionParameter"/>. Each editor kind has
/// its own property, so the dialog never has to convert between them while typing.
/// </summary>
public partial class StepParameterViewModel : ViewModelBase
{
    public StepParameterViewModel(ActionParameter definition, IReadOnlyList<string>? variables = null,
        IReadOnlyList<string>? macros = null)
    {
        Definition = definition;
        Variables = variables ?? [];
        Macros = macros ?? [];
        ExpressionSuggestions = definition.Kind is ActionParameterKind.Expression
            ? [.. Variables.Select(name => "$" + name),
               .. Expression.Functions.Select(function => function.Name + "(")]
            : [];
        Text = definition.DefaultValue;
        Flag = string.Equals(definition.DefaultValue, "true", StringComparison.OrdinalIgnoreCase);
        Choices = [.. definition.OptionChoices.Select(choice => choice with
        {
            Display = Strings.Get($"{definition.OwnerKey}.{definition.Name}.option.{choice.Value}",
                choice.Display),
        })];
        Option = PickInitialOption(Choices, definition.DefaultValue);
        IsEnabled = string.IsNullOrEmpty(definition.EnabledBySibling);

        if (decimal.TryParse(definition.DefaultValue, NumberStyles.Number, CultureInfo.InvariantCulture,
                out var number))
        {
            NumberValue = number;
        }

        if (definition.Kind is ActionParameterKind.Steps or ActionParameterKind.Condition)
        {
            var isCondition = definition.Kind is ActionParameterKind.Condition;
            var addLabelKey = isCondition
                ? "Add.NestedCondition"
                : definition.ConditionsOnly
                    ? "Add.NestedAddCondition"
                    : "Add.NestedAdd";
            List = new StepListEditorViewModel(
                Strings.Get(addLabelKey),
                isCondition,
                isCondition || definition.ConditionsOnly ? ActionCatalog.Conditions : null);
        }
    }

    public ActionParameter Definition { get; }

    /// <summary>Nested editor, set when this parameter holds steps or a condition.</summary>
    public StepListEditorViewModel? List { get; }

    /// <summary>Variable names offered while editing a <see cref="ActionParameterKind.Variable"/>.</summary>
    public IReadOnlyList<string> Variables { get; }

    /// <summary>Macro names offered while editing an <see cref="ActionParameterKind.Macro"/>.</summary>
    public IReadOnlyList<string> Macros { get; }

    /// <summary>
    /// Tokens an expression field offers while the user types: the variables the macro
    /// knows, then the built-in function names with their opening bracket.
    /// </summary>
    public IReadOnlyList<string> ExpressionSuggestions { get; }

    /// <summary>
    /// False while this parameter waits on a sibling list (the logic of a condition group
    /// needs two or more conditions). Such a parameter is neither validated nor saved.
    /// </summary>
    public bool IsEnabled { get; private set; } = true;

    [ObservableProperty]
    public partial string Text { get; set; }

    [ObservableProperty]
    public partial decimal? NumberValue { get; set; }

    [ObservableProperty]
    public partial bool Flag { get; set; }

    [ObservableProperty]
    public partial ActionParameterOption? Option { get; set; }

    /// <summary>What the expression field shows underneath: the result, or why it fails.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExpressionError))]
    [NotifyPropertyChangedFor(nameof(ShowExpressionSuccess))]
    public partial string ExpressionMessage { get; set; } = string.Empty;

    /// <summary>True while <see cref="ExpressionMessage"/> describes a problem.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowExpressionError))]
    [NotifyPropertyChangedFor(nameof(ShowExpressionSuccess))]
    public partial bool HasExpressionError { get; set; }

    /// <summary>True while the message is a problem worth flagging in red.</summary>
    public bool ShowExpressionError => HasExpressionError && ExpressionMessage.Length > 0;

    /// <summary>True while the message is a result worth showing in green.</summary>
    public bool ShowExpressionSuccess => !HasExpressionError && ExpressionMessage.Length > 0;

    public bool IsText => Definition.Kind
        is ActionParameterKind.Text or ActionParameterKind.Key;

    /// <summary>True when this parameter is a colour, edited with the screen picker.</summary>
    public bool IsColor => Definition.Kind is ActionParameterKind.Color;

    /// <summary>
    /// True when this parameter is one half of a screen position. Set by the dialog once it
    /// knows the action carries both an x and a y, so either field can take the pointer's place.
    /// </summary>
    public bool IsCoordinate { get; set; }

    public bool IsMultiline => Definition.Kind is ActionParameterKind.MultilineText;

    public bool IsNumber => Definition.Kind is ActionParameterKind.Number;

    public bool IsBool => Definition.Kind is ActionParameterKind.Bool;

    public bool IsChoice => Definition.Kind is ActionParameterKind.Choice;

    /// <summary>True when this parameter is edited with the nested step editor.</summary>
    public bool IsNested => List is not null;

    /// <summary>True when this parameter is edited as an expression.</summary>
    public bool IsExpression => Definition.Kind is ActionParameterKind.Expression;

    /// <summary>True when this parameter names a variable and offers suggestions.</summary>
    public bool IsVariable => Definition.Kind is ActionParameterKind.Variable;

    /// <summary>True when this parameter names another macro and offers the project's macros.</summary>
    public bool IsMacro => Definition.Kind is ActionParameterKind.Macro;

    /// <summary>Choices offered by a <see cref="ActionParameterKind.Choice"/> editor.</summary>
    public IReadOnlyList<ActionParameterOption> Choices { get; }

    public decimal Minimum => Definition.Minimum;

    public decimal Maximum => Definition.Maximum;

    public decimal Increment => Definition.Increment;

    public string Label => Definition.Required
        ? Definition.LocalLabel
        : Strings.Format("Common.OptionalSuffix", Definition.LocalLabel);

    public string Hint => Definition.LocalHint;

    public bool HasHint => !string.IsNullOrWhiteSpace(Definition.Hint);

    public string Placeholder => Definition.Placeholder;

    /// <summary>True when a required parameter is still empty, which blocks saving.</summary>
    public bool IsMissing => IsEnabled && Definition.Required && Definition.Kind switch
    {
        ActionParameterKind.Bool => false,
        ActionParameterKind.Steps or ActionParameterKind.Condition => !HasNestedSteps,
        _ => string.IsNullOrWhiteSpace(CurrentText),
    };

    /// <summary>Optional parameters left blank are dropped from the saved node.</summary>
    public bool IsIncluded => IsEnabled && Definition.Kind switch
    {
        ActionParameterKind.Steps or ActionParameterKind.Condition => HasNestedSteps,
        _ => Definition.Required || !string.IsNullOrWhiteSpace(CurrentText),
    };

    /// <summary>
    /// Recomputes whether this parameter applies, from the item count of the sibling
    /// list it waits on. Logic needs at least two conditions to combine.
    /// </summary>
    public void UpdateGate(int siblingItemCount)
    {
        if (string.IsNullOrEmpty(Definition.EnabledBySibling))
        {
            return;
        }

        var enabled = siblingItemCount >= 2;
        if (enabled == IsEnabled)
        {
            return;
        }

        IsEnabled = enabled;
        OnPropertyChanged(nameof(IsEnabled));
    }

    /// <summary>True when the nested editor holds at least one step.</summary>
    public bool HasNestedSteps => List?.Steps.Count > 0;

    /// <summary>Re-checks an expression as it is typed, so the editor can show the outcome.</summary>
    partial void OnTextChanged(string value)
    {
        if (IsExpression)
        {
            UpdateExpression(value);
        }
    }

    private void UpdateExpression(string? source)
    {
        ExpressionMessage = string.Empty;
        HasExpressionError = false;

        if (string.IsNullOrWhiteSpace(source))
        {
            return;
        }

        if (!Expression.TryEvaluate(source, new KnownVariables(Variables), out var value, out var error))
        {
            HasExpressionError = true;
            ExpressionMessage = ExpressionText.Describe(error!);
            return;
        }

        // Without variables the answer is exact, so it is worth showing. With variables
        // there is nothing to run yet, so the field only confirms that the text reads.
        ExpressionMessage = source.Contains('$')
            ? Strings.Get("Add.ExpressionValid")
            : Strings.Format("Add.ExpressionResult", value.AsText());
    }

    /// <summary>
    /// Answers for every name the macro knows with a plain value, so an expression can be
    /// checked while the macro is still being written and nothing has run yet.
    /// </summary>
    private sealed class KnownVariables(IReadOnlyList<string> names) : IVariableResolver
    {
        public bool TryGet(string name, out Value value)
        {
            if (names.Any(candidate => string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase)))
            {
                value = Value.FromNumber(0);
                return true;
            }

            value = Value.Null;
            return false;
        }
    }

    /// <summary>The value as text, taken from whichever editor this parameter uses.</summary>
    public string CurrentText => Definition.Kind switch
    {
        ActionParameterKind.Number => (NumberValue ?? 0m).ToString(CultureInfo.InvariantCulture),
        ActionParameterKind.Bool => Flag ? "true" : "false",
        ActionParameterKind.Choice => Option?.Value ?? string.Empty,
        _ => Text ?? string.Empty,
    };

    /// <summary>Flattens the edited value into the model the step is built from.</summary>
    public StepParameter ToStepParameter() => new()
    {
        Name = Definition.Name,
        Kind = Definition.Kind,
        Value = CurrentText.Trim(),
        Steps = Definition.Kind is ActionParameterKind.Steps
            ? List?.Steps.ToList() ?? []
            : [],
        Condition = Definition.Kind is ActionParameterKind.Condition
            ? List?.Steps.FirstOrDefault()
            : null,
    };

    /// <summary>Loads a value that was stored on an existing step.</summary>
    public void ApplyValue(StepParameter stored)
    {
        if (Definition.Kind is ActionParameterKind.Steps)
        {
            List?.Load(stored.Steps);
            return;
        }

        if (Definition.Kind is ActionParameterKind.Condition)
        {
            if (stored.Condition is not null)
            {
                List?.Load([stored.Condition]);
            }

            return;
        }

        var raw = stored.Value;
        switch (Definition.Kind)
        {
            case ActionParameterKind.Number:
                if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                {
                    NumberValue = number;
                }
                break;
            case ActionParameterKind.Bool:
                Flag = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
                break;
            case ActionParameterKind.Choice:
                Option = Choices.FirstOrDefault(choice => choice.Value == raw) ?? Choices.FirstOrDefault();
                break;
            default:
                Text = raw;
                break;
        }
    }

    private static ActionParameterOption? PickInitialOption(
        IReadOnlyList<ActionParameterOption> choices, string defaultValue)
    {
        if (choices.Count == 0)
        {
            return null;
        }

        return choices.FirstOrDefault(choice => choice.Value == defaultValue) ?? choices[0];
    }
}
