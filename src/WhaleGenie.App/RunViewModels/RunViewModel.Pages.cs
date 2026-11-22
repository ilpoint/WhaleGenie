using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// Which page of the run window is shown, and whether the panel is busy or waiting on an answer.
public partial class RunViewModel
{
    /// <summary>Which half of the window is showing: the steps, or the variables.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDebugPage))]
    [NotifyPropertyChangedFor(nameof(IsVariablesPage))]
    public partial RunPage Page { get; set; }

    public bool IsDebugPage => Page is RunPage.Debug;

    public bool IsVariablesPage => Page is RunPage.Variables;

    [RelayCommand]
    private void SelectPage(string? page)
        => Page = page == nameof(RunPage.Variables) ? RunPage.Variables : RunPage.Debug;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>
    /// 这次运行"处在什么状态"。**文字说是什么，这个说该怎么画**——
    /// 原来只有文字、颜色写死成琥珀，于是"已完成"和"出问题了"长得一样。
    /// </summary>
    public RunStatusKind StatusKindValue { get; private set; } = RunStatusKind.Ready;

    /// <summary>Set while a failure is waiting for the user to choose what happens next.</summary>
    [ObservableProperty]
    public partial bool IsAsking { get; set; }

    /// <summary>Set while the macro itself is waiting for an answer.</summary>
    [ObservableProperty]
    public partial bool IsAskingUser { get; set; }

    /// <summary>True when the macro's question wants a line of writing rather than a yes or no.</summary>
    [ObservableProperty]
    public partial bool WantsTypedAnswer { get; set; }

    /// <summary>What the answer box holds while the macro waits for a line of writing.</summary>
    [ObservableProperty]
    public partial string AnswerText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Question { get; set; } = string.Empty;

    /// <summary>Running from the top needs steps, an idle window and every required parameter filled.</summary>
    public bool CanStart => !IsBusy && _steps.Count > 0 && ArgumentsReady;

    /// <summary>Stepping works before a run starts and while one is paused.</summary>
    public bool CanStep => _steps.Count > 0 && ArgumentsReady;

    /// <summary>True when the declaration's required parameters have a value or an explicit empty.</summary>
    private bool ArgumentsReady => Arguments.All(row => !row.IsMissing);

    public bool CanStop => IsBusy;

    /// <summary>True when this run bends the macro's timings away from how they were written.</summary>
    public bool IsSpeedAdjusted => Math.Abs(_delayScale - 1) > 0.001;

    /// <summary>The run-speed badge shown in the toolbar, for example <c>×2</c>.</summary>
    public string SpeedNote => DelayScaleViewModel.FormatScale(_delayScale);
}
