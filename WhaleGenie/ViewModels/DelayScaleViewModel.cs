using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Localization;
using WhaleGenie.Models;

namespace WhaleGenie.ViewModels;

/// <summary>
/// Backs the run-speed dialog. It only reads the macro's steps: the factor is remembered on the
/// macro and bent into the timings while it runs, so nothing in the step list is ever rewritten
/// and setting the factor back to ×1 is a complete undo.
/// </summary>
public partial class DelayScaleViewModel : ViewModelBase
{
    private readonly DelayScan _scan;

    public DelayScaleViewModel()
        : this(1, [])
    {
    }

    public DelayScaleViewModel(double scale, IReadOnlyList<MacroStep> steps)
    {
        _scan = DelayScan.Of(steps);
        Factor = (decimal)Clamp(scale);

        Presets =
        [
            .. new[] { 0.5m, 1m, 1.5m, 2m, 3m }.Select(value => new ScalePresetChoice
            {
                Label = "×" + value.ToString("0.##", CultureInfo.InvariantCulture),
                Command = new RelayCommand(() => Factor = value),
            }),
        ];
    }

    /// <summary>Raised with the chosen factor, or null when the dialog was cancelled.</summary>
    public event Action<double?>? CloseRequested;

    public string Header => Strings.Get("Speed.Title");

    public string Explanation => Strings.Get("Speed.Explanation");

    /// <summary>Writes a speed factor the way the interface shows it, for example <c>×2</c>.</summary>
    public static string FormatScale(double scale)
        => "×" + scale.ToString("0.##", CultureInfo.InvariantCulture);

    public IReadOnlyList<ScalePresetChoice> Presets { get; }

    /// <summary>The factor being chosen; 1 leaves every wait exactly as written.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Display))]
    [NotifyPropertyChangedFor(nameof(Summary))]
    [NotifyPropertyChangedFor(nameof(IsOriginal))]
    public partial decimal? Factor { get; set; } = 1m;

    /// <summary>The factor as it is shown, for example <c>×2</c>.</summary>
    public string Display => "×" + Current.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>True when the macro would run at exactly the speed it was written.</summary>
    public bool IsOriginal => Math.Abs(Current - 1) < 0.001;

    /// <summary>What the factor does to the waits this macro actually has.</summary>
    public string Summary => _scan.Waits == 0
        ? Strings.Get("Speed.NoWaits")
        : Strings.Format("Speed.Summary", _scan.Waits,
            Duration(_scan.WaitMs), Duration(_scan.WaitMs * Current));

    /// <summary>The other timings the factor bends, when the macro has any.</summary>
    public string Detail => HasDetail ? Strings.Format("Speed.Detail", _scan.Intervals, _scan.Paces) : string.Empty;

    public bool HasDetail => _scan.Intervals + _scan.Paces > 0;

    private double Current => Clamp((double)(Factor ?? 1m));

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(Current);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    /// <summary>A length of time written the way a person reads it.</summary>
    public static string Duration(double milliseconds) => milliseconds < 1000
        ? Strings.Format("Speed.Milliseconds",
            Math.Round(milliseconds).ToString("0", CultureInfo.InvariantCulture))
        : Strings.Format("Speed.Seconds",
            (milliseconds / 1000).ToString("0.##", CultureInfo.InvariantCulture));

    private static double Clamp(double scale)
        => double.IsFinite(scale) ? Math.Clamp(scale, 0.1, 10) : 1;

    /// <summary>Works out what the factor would touch, without changing anything.</summary>
    private sealed class DelayScan
    {
        public int Waits { get; private set; }

        public double WaitMs { get; private set; }

        public int Intervals { get; private set; }

        public int Paces { get; private set; }

        public static DelayScan Of(IReadOnlyList<MacroStep> steps)
        {
            var scan = new DelayScan();
            foreach (var step in steps)
            {
                scan.Walk(step);
            }

            return scan;
        }

        private void Walk(MacroStep step)
        {
            foreach (var parameter in step.Parameters)
            {
                switch (parameter.Kind)
                {
                    case ActionParameterKind.Steps:
                        foreach (var child in parameter.Steps)
                        {
                            Walk(child);
                        }

                        break;
                    case ActionParameterKind.Condition when parameter.Condition is not null:
                        Walk(parameter.Condition);
                        break;
                }
            }

            switch (step.Type)
            {
                case "control.delay":
                    Add(Value(step, "ms"));
                    break;
                case "control.delayRandom":
                    Add((Value(step, "minMs") + Value(step, "maxMs")) / 2.0);
                    break;
                case "control.repeat" or "control.for":
                    if (Value(step, "intervalMs") > 0)
                    {
                        Intervals++;
                    }

                    break;
                default:
                    if (PaceParameter(step.Type) is { } pace && Value(step, pace) > 0)
                    {
                        Paces++;
                    }

                    break;
            }

            Add(step.Meta.DelayBeforeMs);
            Add(step.Meta.DelayAfterMs);
        }

        private void Add(double milliseconds)
        {
            if (milliseconds > 0)
            {
                Waits++;
                WaitMs += milliseconds;
            }
        }

        /// <summary>The parameter that says how long an input takes, when the action has one.</summary>
        private static string? PaceParameter(string type) => type switch
        {
            "input.keyPress" or "input.hotkey" => "holdMs",
            "input.typeText" or "input.mouseClick" => "intervalMs",
            "input.mouseMove" or "input.mouseMoveRelative" or "input.mouseDrag" => "durationMs",
            _ => null,
        };

        private static int Value(MacroStep step, string name)
        {
            var text = step.Parameters.FirstOrDefault(parameter => parameter.Name == name)?.Value
                       ?? string.Empty;

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                ? number
                : 0;
        }
    }
}

/// <summary>One offered speed on the run-speed dialog, with the command that picks it.</summary>
public sealed class ScalePresetChoice
{
    public required string Label { get; init; }

    public required ICommand Command { get; init; }
}
