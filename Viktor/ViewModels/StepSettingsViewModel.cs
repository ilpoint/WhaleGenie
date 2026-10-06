using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Viktor.Core.Execution;
using Viktor.Localization;

namespace Viktor.ViewModels;

/// <summary>
/// Backs the step-settings dialog: the settings that belong to a step rather than to its action,
/// which is what a macro needs when a step is flaky — it may be tried again, it may be given longer,
/// and its failure may be handled rather than stopping everything.
/// </summary>
/// <remarks>
/// The dialog never touches the step list. It hands back the settings to keep and the editor puts
/// them on the step, so cancelling leaves the macro exactly as it was.
/// </remarks>
public partial class StepSettingsViewModel : ViewModelBase
{
    /// <summary>How many retries the dialog offers, so a slip of the hand cannot fill the log.</summary>
    private const int MostRetries = 20;

    /// <summary>How many waits the plan spells out before it stops listing them one by one.</summary>
    private const int MostWaitsShown = 6;

    public StepSettingsViewModel()
        : this(StepMeta.Empty)
    {
    }

    public StepSettingsViewModel(StepMeta meta)
    {
        Comment = meta.Comment;
        IsStepEnabled = meta.IsEnabled;
        DelayBeforeMs = meta.DelayBeforeMs;
        DelayAfterMs = meta.DelayAfterMs;
        TimeoutMs = meta.TimeoutMs;
        RetryCount = meta.RetryCount;
        RetryDelayMs = meta.RetryDelayMs;
        BackoffIndex = (int)meta.RetryBackoff;
        FailureIndex = (int)meta.OnError;
    }

    /// <summary>Raised with the settings to keep, or null when the dialog was dismissed.</summary>
    public event Action<StepMeta?>? CloseRequested;

    public string Header => Strings.Get("StepSettings.Title");

    public string Explanation => Strings.Get("StepSettings.Intro");

    /// <summary>Note the user wrote about this step, shown under it in the editor.</summary>
    [ObservableProperty]
    public partial string Comment { get; set; } = string.Empty;

    /// <summary>False skips the step without taking it out of the macro.</summary>
    [ObservableProperty]
    public partial bool IsStepEnabled { get; set; } = true;

    [ObservableProperty]
    public partial int? DelayBeforeMs { get; set; }

    [ObservableProperty]
    public partial int? DelayAfterMs { get; set; }

    /// <summary>How long the step may take before it counts as failed. 0 leaves it unlimited.</summary>
    [ObservableProperty]
    public partial int? TimeoutMs { get; set; }

    /// <summary>Extra attempts a failing step gets before the failure rule applies.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRetries))]
    [NotifyPropertyChangedFor(nameof(RetryPlan))]
    public partial int? RetryCount { get; set; }

    /// <summary>The pause the retries start from.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RetryPlan))]
    public partial int? RetryDelayMs { get; set; } = 500;

    /// <summary>Which of <see cref="BackoffOptions"/> is chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RetryPlan))]
    public partial int BackoffIndex { get; set; }

    /// <summary>Which of <see cref="FailureOptions"/> is chosen.</summary>
    [ObservableProperty]
    public partial int FailureIndex { get; set; }

    /// <summary>True while the step is retried at all.</summary>
    public bool HasRetries => RetryCount is > 0;

    /// <summary>
    /// What the retry settings add up to, spelled out with real numbers, so "double each attempt"
    /// is never a mystery about what the macro will actually do.
    /// </summary>
    public string RetryPlan
    {
        get
        {
            var count = RetryCount ?? 0;
            if (count <= 0)
            {
                return Strings.Get("StepSettings.Plan.None");
            }

            var settings = Build();
            return settings.RetryBackoff switch
            {
                RetryBackoff.Fixed => Strings.Format("StepSettings.Plan.Fixed", count,
                    Duration(settings.RetryDelayFor(1))),
                RetryBackoff.Doubling => Strings.Format("StepSettings.Plan.Growing", count,
                    string.Join(" → ", WaitList(settings, count))),
                _ => Strings.Format("StepSettings.Plan.Random", count,
                    Duration(settings.RetryDelayFor(1) / 2.0), Duration(settings.RetryDelayFor(1) * 1.5)),
            };
        }
    }

    /// <summary>The rules a failure can follow, in the order of <see cref="StepErrorAction"/>.</summary>
    public IReadOnlyList<string> FailureOptions { get; } =
    [
        Strings.Get("StepSettings.Option.Stop"),
        Strings.Get("StepSettings.Option.Continue"),
        Strings.Get("StepSettings.Option.NextIteration"),
        Strings.Get("StepSettings.Option.Ask"),
    ];

    /// <summary>The ways the pause between retries can grow, like <see cref="RetryBackoff"/>.</summary>
    public IReadOnlyList<string> BackoffOptions { get; } =
    [
        Strings.Get("StepSettings.Backoff.Fixed"),
        Strings.Get("StepSettings.Backoff.Doubling"),
        Strings.Get("StepSettings.Backoff.Jitter"),
    ];

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(Build());

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(null);

    /// <summary>The settings as the engine reads them, with every number brought back into range.</summary>
    public StepMeta Build() => new()
    {
        Comment = Comment.Trim(),
        IsEnabled = IsStepEnabled,
        DelayBeforeMs = Whole(DelayBeforeMs),
        DelayAfterMs = Whole(DelayAfterMs),
        TimeoutMs = Whole(TimeoutMs),
        RetryCount = Math.Clamp(Whole(RetryCount), 0, MostRetries),
        RetryDelayMs = Math.Clamp(Whole(RetryDelayMs), 0, StepMeta.MostRetryDelayMs),
        RetryBackoff = (RetryBackoff)Math.Clamp(BackoffIndex, 0, (int)RetryBackoff.Jitter),
        OnError = (StepErrorAction)Math.Clamp(FailureIndex, 0, (int)StepErrorAction.AskUser),
    };

    /// <summary>The waits the first few retries would use, for the growing backoff.</summary>
    private static IEnumerable<string> WaitList(StepMeta settings, int count)
    {
        var shown = Math.Min(count, MostWaitsShown);
        for (var attempt = 1; attempt <= shown; attempt++)
        {
            yield return Duration(settings.RetryDelayFor(attempt));
        }

        if (count > shown)
        {
            yield return "…";
        }
    }

    /// <summary>A length of time written the way a person reads it, the same as the run-speed dialog.</summary>
    private static string Duration(double milliseconds) => DelayScaleViewModel.Duration(milliseconds);

    /// <summary>A number typed into the dialog, falling back to zero when it is left empty.</summary>
    private static int Whole(int? value) => Math.Max(0, value ?? 0);
}
