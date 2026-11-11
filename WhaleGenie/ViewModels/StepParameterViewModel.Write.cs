using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media.Imaging;
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
    /// <summary>Re-checks an expression as it is typed, so the editor can show the outcome.</summary>
    partial void OnTextChanged(string value)
    {
        // A line that came from somewhere else — the picker, a drop, a step being opened — is read
        // once into the value the pieces are built from. While the pieces are the ones writing, the
        // value is already theirs and is not read back out of the line they just wrote.
        if (!_composing)
        {
            _written = Formula.FromLine(value);
        }

        // The pieces are the same value said another way, so they are read again on every change:
        // what the field keeps and what it draws cannot drift apart if one is derived. While the
        // pieces are the ones writing, this is the change they just made and they stay as they are.
        RebuildParts();

        // A condition written by something other than the rows — an expression typed in the raw
        // view, a value read back from a file — has to be read into the rows again, or the two
        // views would be showing different conditions.
        if (IsCondition && ConditionEditor is { } condition && !_writingCondition
            && condition.Text != value)
        {
            condition.Read(value);
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

        if (IsImage)
        {
            RefreshThumbnail();
        }

        if (IsExpression || IsNumberFormula)
        {
            UpdateExpression(value);
        }
    }

    /// <summary>
    /// Puts a value in the field whichever way it is written, which is what comes back from the
    /// expression builder: a plain number is a number and anything else is an expression, and the
    /// field decides that for itself rather than the button knowing which kind it is.
    /// </summary>
    public void WriteValue(string? value)
    {
        var written = (value ?? string.Empty).Trim();

        if (!IsNumber)
        {
            Text = written;
            return;
        }

        // Which box the field shows is decided from what is being written rather than from what was
        // there a moment ago, and the value goes in before the mode does: switching a number into an
        // expression would otherwise read the number that was showing and decide the other way.
        var number = written.Length == 0 || NumberField.IsNumberText(written);
        _writing = true;
        try
        {
            Text = written;
            if (!number)
            {
                UseFormula = true;
            }
        }
        finally
        {
            _writing = false;
        }

        if (number)
        {
            UseFormula = false;
        }
        else
        {
            // The check runs here rather than while the text went in, because the field only became
            // an expression a moment ago and what the text says is read off that.
            UpdateExpression(written);
        }
    }

    /// <summary>
    /// Puts a plain number in the field, dropping an expression that was there. This is what the
    /// screen picker writes when it takes the pointer's place.
    /// </summary>
    public void SetNumber(int value)
    {
        UseFormula = false;
        NumberValue = value;
    }

    /// <summary>Writes a variable the way this field reads a value, optionally at the text caret.</summary>
    public void ApplyVariableDrop(string value, int? caret = null)
    {
        if (Definition.ReadMode is ActionReadMode.Text)
        {
            var current = Text ?? string.Empty;
            var at = Math.Clamp(caret ?? current.Length, 0, current.Length);
            Text = current[..at] + value + current[at..];
            return;
        }

        if (Definition.Kind is ActionParameterKind.WindowRule)
        {
            UseWindowVariable(value);
            return;
        }

        if (Definition.Kind is ActionParameterKind.Number or ActionParameterKind.Expression
            or ActionParameterKind.Record or ActionParameterKind.Point
            or ActionParameterKind.Rect)
        {
            UseFormula = true;
        }

        Text = value;
    }

    /// <summary>
    /// Empties the field whichever way it was written, so that the other way of answering the same
    /// question — a record here, the numbers beside it, or the other way round — can take over
    /// without the form holding two answers at once.
    /// </summary>
    public void Clear()
    {
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
            Text = Milliseconds.ToString(CultureInfo.InvariantCulture);
            return;
        }

        if (decimal.TryParse((Text ?? string.Empty).Trim(), NumberStyles.Number,
                CultureInfo.InvariantCulture, out var number))
        {
            ShowMilliseconds(number);
        }
    }

    private void UpdateExpression(string? source)
    {
        var (message, failed) = NumberField.Check(source, KnownNames);
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
