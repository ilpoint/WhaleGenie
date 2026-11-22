using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;


namespace WhaleGenie.ViewModels;

// Writing a value into the field: typing, dropping a variable, clearing, and the expression behind it.
public partial class StepParameterViewModel : ViewModelBase
{
    /// <summary>
    /// The line under the field is only a picture of the shape. Everything that has to follow a
    /// change of value is said here, and none of it reads the line back: the shape is already the
    /// answer, and <see cref="SetWritten"/> is the only place it is put in.
    /// </summary>
    partial void OnTextChanged(string value)
    {
        // The pieces are the same value said another way, so they are drawn again on every change.
        RebuildParts();

        // A condition written by something other than the rows — an expression typed in the raw
        // view, a value read back from a file — has to be read into the rows again, or the two
        // views would be showing different conditions.
        if (IsCondition && ConditionEditor is { } condition && !_writingCondition
            && condition.Text != value)
        {
            condition.Read(_written);
        }

        if (IsWindowRule)
        {
            OnPropertyChanged(nameof(WindowRuleSummary));
            OnPropertyChanged(nameof(HasWindowRule));
            OnPropertyChanged(nameof(IsMissing));
            OnPropertyChanged(nameof(IsIncluded));
        }

        // A number written as an expression goes back to the numbered box the moment what is in the
        // box is a plain number again — which is also how the box is emptied. The builder and
        // a number typed by hand both land here, so neither has to know which box it wrote into.
        if (!_writing && IsNumber && UseFormula && (value.Trim().Length == 0 || TextIsNumber))
        {
            UseFormula = false;
            return;
        }

        if (IsExpression || IsNumberFormula)
        {
            UpdateExpression(_written);
        }
    }

    /// <summary>
    /// The one place a value enters the field. A shape goes in, the line is drawn from it, and
    /// nothing is ever read back out of that line.
    /// </summary>
    public void WriteValue(ExpressionNode value) => SetWritten(value);

    /// <summary>
    /// Puts a shape in as the field's value and redraws what the person sees. Every other writer —
    /// a picker, a drop, a file being opened, a row being filled — comes through here, so the
    /// identity a bound name carries is never lost on the way in.
    /// </summary>
    internal void SetWritten(ExpressionNode value)
    {
        _written = value;
        _writing = true;
        try
        {
            Text = DrawnLine(value);
            if (IsNumber)
            {
                var plain = IsPlainNumber(value);
                UseFormula = !plain;
                if (plain && value is NumberNode number)
                {
                    ShowMilliseconds((decimal)number.Value);
                }
            }
        }
        finally
        {
            _writing = false;
        }

        if (IsExpression || IsNumberFormula)
        {
            UpdateExpression(_written);
        }
    }

    /// <summary>The shape as the line under the field. A picture only, never a source.</summary>
    private string DrawnLine(ExpressionNode value)
        => Formula.IsNothing(value)
            ? string.Empty
            : Definition.NamesVariable
                ? Formula.NameOf(value)
                : ReadAsFormula || IsNumber
                    ? Expression.Render(value)
                    : ExpressionPhrase.Display(value);

    /// <summary>A plain number belongs in the numbered box; everything else in the formula one.</summary>
    private static bool IsPlainNumber(ExpressionNode value)
        => Formula.IsNothing(value)
            || value is NumberNode
            || value is UnaryNode { Operator: "-", Operand: NumberNode };

    /// <summary>
    /// Puts a plain number in the field, dropping an expression that was there. This is what the
    /// screen picker writes when it takes the pointer's place.
    /// </summary>
    public void SetNumber(int value)
    {
        UseFormula = false;
        NumberValue = value;
    }

    /// <summary>
    /// Empties the field whichever way it was written, so that the other way of answering the same
    /// question — a record here, the numbers beside it, or the other way round — can take over
    /// without the form holding two answers at once.
    /// </summary>
    public void Clear()
    {
        // A switch, a choice and the numbered box keep their own answer: the line is only a
        // picture, and clearing it must not quietly take the value away.
        Text = string.Empty;
        UseFormula = false;
        NumberValue = null;
    }

    partial void OnUseFormulaChanged(bool value)
    {
        // A value written in one go is not a question about the box it goes in: what the builder
        // handed back is what the field says, and the number it replaced is not what is meant.
        if (_writing)
        {
            return;
        }

        if (value)
        {
            // Start from the number that was showing, so the field is never left blank. The engine
            // reads an expression in milliseconds, so a time is written down in the stored unit
            // rather than the one the box happened to be showing.
            var shown = Milliseconds.ToString(CultureInfo.InvariantCulture);
            _written = Formula.Number(shown, (double)Milliseconds);
            Text = shown;
            return;
        }

        if (_written is NumberNode number)
        {
            ShowMilliseconds((decimal)number.Value);
        }
    }

    private void UpdateExpression(ExpressionNode written)
    {
        var (message, failed) = NumberField.Check(written, KnownNames);
        HasExpressionError = failed;
        ExpressionMessage = message;
    }

    /// <summary>The value as text, taken from whichever editor this parameter uses.</summary>
    public string CurrentText => Definition.Kind switch
    {
        ActionParameterKind.Number => NumberText,
        ActionParameterKind.Bool => Flag ? "true" : "false",
        ActionParameterKind.Choice or ActionParameterKind.Step => Option?.Value ?? string.Empty,
        _ => Text ?? string.Empty,
    };

    /// <summary>
    /// A number as it is to be written down. A box nobody has typed in holds nothing rather than a
    /// zero: zero is a number a step can mean — the left edge of the screen, a wait of no time —
    /// and a field nobody has answered has not said it. The engine reads an empty field as the
    /// "nothing was said" it is — which for a length of time is a step that follows the screen it
    /// runs on rather than one written as a zero, and zero is itself a length a step can mean.
    /// </summary>
    private string NumberText => UseFormula
        ? Text ?? string.Empty
        : NumberValue is not { } number
            ? string.Empty
            : IsDuration
                ? Milliseconds.ToString("0.####", CultureInfo.InvariantCulture)
                : number.ToString(CultureInfo.InvariantCulture);
}
