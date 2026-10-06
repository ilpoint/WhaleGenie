using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Viktor.Core.Execution;
using Viktor.Models;

namespace Viktor.ViewModels;

/// <summary>
/// One length-of-time box in the step settings: the number and the unit it is written in. The
/// step stores milliseconds whatever unit is showing, the same way an action parameter does.
/// </summary>
public partial class DurationSettingViewModel : ViewModelBase
{
    private readonly decimal _maximum;
    private DurationUnit _unit;

    public DurationSettingViewModel(decimal initialMilliseconds = 0m,
        decimal maximum = StepMeta.LongestPauseMs)
    {
        _maximum = maximum;
        Units = DurationUnit.Localized();
        _unit = Units[0];
        Load(initialMilliseconds);
    }

    /// <summary>Units offered beside the box, labelled in the interface language.</summary>
    public IReadOnlyList<DurationUnit> Units { get; }

    /// <summary>
    /// The unit the number is written in. Switching it keeps the moment the same and changes only
    /// how it reads, so the step still stores the number it stored before.
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
            if (Value is not null)
            {
                Value = milliseconds / value.Factor;
            }

            OnPropertyChanged(nameof(Minimum));
            OnPropertyChanged(nameof(Maximum));
        }
    }

    /// <summary>The number on screen, in the unit above. Null leaves the box empty.</summary>
    [ObservableProperty]
    public partial decimal? Value { get; set; }

    public decimal Minimum => 0m;

    /// <summary>The top of the box, in whichever unit it is showing.</summary>
    public decimal Maximum => _maximum / _unit.Factor;

    /// <summary>The number the step stores, always in milliseconds.</summary>
    public int Milliseconds => Value is null
        ? 0
        : (int)Math.Clamp(decimal.Round(Value.Value * _unit.Factor), 0m, _maximum);

    /// <summary>
    /// Shows a stored number as milliseconds, the unit every time field starts on; zero leaves the
    /// box empty, so a step that says nothing about this pause still opens with nothing in it.
    /// </summary>
    public void Load(decimal milliseconds)
    {
        Unit = Units[0];
        Value = milliseconds == 0m ? null : milliseconds;
    }
}
