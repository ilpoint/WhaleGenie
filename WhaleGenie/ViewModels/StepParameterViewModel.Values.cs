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

// What the field holds: text, number, unit, flag or the option picked, and the error it shows.
public partial class StepParameterViewModel : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(KeyFieldDisplay))]
    [NotifyPropertyChangedFor(nameof(DisplayLine))]
    public partial string Text { get; set; }

    /// <summary>
    /// The value as a person reads it in the field. Nothing is ever read back from this line: the
    /// field is read-only, and every value is put in through the value window as a shape. Names
    /// are said with their own typography rather than a sign in front of them.
    /// </summary>
    public string DisplayLine => Formula.Speak(Written);

    /// <summary>The key or chord shown on the keyboard field's button.</summary>
    public string KeyFieldDisplay => Text.Trim().Length > 0
        ? Text
        : Strings.Get(IsKeys ? "Add.ChooseKeys" : "Add.ChooseKey");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JitterRange))]
    [NotifyPropertyChangedFor(nameof(HasJitterRange))]
    [NotifyPropertyChangedFor(nameof(PercentValue))]
    [NotifyPropertyChangedFor(nameof(ChannelNote))]
    [NotifyPropertyChangedFor(nameof(HasChannelNote))]
    public partial decimal? NumberValue { get; set; }

    /// <summary>
    /// How far a length of time may move each run, as a percentage of it: 20 means the wait comes
    /// out somewhere between 80% and 120% of what is written. Empty means "use it as written".
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JitterRange))]
    [NotifyPropertyChangedFor(nameof(HasJitterRange))]
    public partial decimal? JitterPercent { get; set; }

    /// <summary>
    /// The unit the box is showing a length of time in. Switching it keeps the moment the same
    /// and changes only how it reads: 1000 milliseconds becomes 1 second, and the step still
    /// stores 1000.
    /// </summary>
    public DurationUnit Unit
    {
        get => _unit;
        set
        {
            if (value is null || value == _unit)
            {
                return;
            }

            // The number on screen is written in the unit, so it moves across rather than changing
            // what it means. An empty box stays empty.
            var milliseconds = Milliseconds;
            _unit = value;
            OnPropertyChanged();
            if (NumberValue is not null)
            {
                NumberValue = milliseconds / value.Factor;
            }

            OnPropertyChanged(nameof(Minimum));
            OnPropertyChanged(nameof(Maximum));
            OnPropertyChanged(nameof(CurrentText));
            OnPropertyChanged(nameof(JitterRange));
            OnPropertyChanged(nameof(HasJitterRange));
        }
    }

    /// <summary>The value in milliseconds, whichever unit the box is showing it in.</summary>
    private decimal Milliseconds => (NumberValue ?? 0m) * _unit.Factor;

    /// <summary>
    /// Shows a length of time that is stored in milliseconds as milliseconds. The unit box starts
    /// on milliseconds whatever the number is, so every time field in a step reads the same way;
    /// writing "5 分" is something the user asks for by picking the unit, not something the dialog
    /// decides for them.
    /// </summary>
    private void ShowMilliseconds(decimal milliseconds)
    {
        Unit = DurationUnits[0];
        NumberValue = milliseconds;
    }

    /// <summary>
    /// True while a number is being written as an expression (<c>match.x</c>, <c>count + 1</c>)
    /// instead of typed in as a plain number. The engine reads both the same way.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNumberValue))]
    [NotifyPropertyChangedFor(nameof(IsNumberFormula))]
    [NotifyPropertyChangedFor(nameof(IsDurationEditor))]
    [NotifyPropertyChangedFor(nameof(IsDurationFormula))]
    [NotifyPropertyChangedFor(nameof(JitterRange))]
    [NotifyPropertyChangedFor(nameof(HasJitterRange))]
    // The slider and the note about colour channels both read the field as a plain number, so they
    // go away with the box when an expression takes its place. Nobody telling them leaves a slider
    // on screen writing into a field that no longer has one.
    [NotifyPropertyChangedFor(nameof(IsPercent))]
    [NotifyPropertyChangedFor(nameof(ChannelNote))]
    [NotifyPropertyChangedFor(nameof(HasChannelNote))]
    public partial bool UseFormula { get; set; }

    [ObservableProperty]
    public partial bool Flag { get; set; }

    [ObservableProperty]
    public partial ActionParameterOption? Option { get; set; }

    /// <summary>
    /// Keeps the shape in step with a control that never writes a line. A switch, a list and a
    /// number box answer the field without going through <see cref="Text"/>, so each of them has
    /// to put its answer into the shape here: that shape is the only thing the step saves, and a
    /// control that changed must not leave an older one behind. The shapes match what the file
    /// reads back — a switch is a constant, a choice is a word, a numbered box is a number — so
    /// opening a step and saving it again does not change what it says.
    /// </summary>
    partial void OnFlagChanged(bool value)
        => _written = Formula.Constant(value ? "true" : "false");

    partial void OnOptionChanged(ActionParameterOption? value)
        => _written = Formula.Text(value?.Value ?? string.Empty);

    partial void OnNumberValueChanged(decimal? value)
    {
        // While the numbered box is showing, its number is the answer. Once the expression box
        // takes over, the line is the answer and is read where it is written.
        if (!UseFormula && Definition.Kind is ActionParameterKind.Number)
        {
            _written = Expression.Read(CurrentText.Trim());
        }
    }

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


    /// <summary>
    /// Choices offered by a <see cref="ActionParameterKind.Choice"/> editor, and the steps offered
    /// by a <see cref="ActionParameterKind.Step"/> one. It is a list rather than a fixed set because
    /// a step name that is not in the macro any more still has to be shown.
    /// </summary>
    public ObservableCollection<ActionParameterOption> Choices { get; }
}
