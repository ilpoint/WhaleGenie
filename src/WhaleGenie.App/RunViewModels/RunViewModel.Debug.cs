using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Contracts;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

// Starting a run from the debugger: which macro is being run and what stands behind a call.
public partial class RunViewModel
{
    /// <summary>
    /// Points the window at a macro without running it, which is what all three ways in ask for: the
    /// card's own menu, the editor's 「运行调试」 button and the picker in a window that was opened
    /// empty. Loading and running are two things here, because the first thing a run is wanted for is
    /// often a look at the steps and a breakpoint or two — and a run that starts before anyone can
    /// say that is a run that has already done something. Where the run begins is this window's own
    /// 「运行」/「单步」/F5.
    ///
    /// One of these windows is open at a time, so asking for a second macro stops the run that was
    /// going first. Why it stopped is kept for the next run's log rather than written now: a run that
    /// ends early by itself looks like a failure, and the log is read from the top when it starts.
    /// </summary>
    public async Task DebugAsync(MacroItem macro)
    {
        if (IsBusy && Debugged == macro.Name)
        {
            // The macro asked for is the one already running. Starting it again would throw away
            // the run in progress, which is not what "debug this" means.
            return;
        }

        var switched = IsBusy;
        var was = Debugged;
        if (switched && _ended is { } ending)
        {
            Stop();

            // The steps the run is working through are about to be replaced, so wait for it to be
            // over rather than pulling them out from under it.
            await ending.Task;
        }

        _callable = macro.Callable;
        Load(macro.Steps);
        Debugged = macro.Name;
        _inputRate = macro.InputRate;
        OnPropertyChanged(nameof(ShowsInputOwner));
        OnPropertyChanged(nameof(InputOwnerLine));
        OnPropertyChanged(nameof(InputRate));
        OnPropertyChanged(nameof(InputRateOwner));
        if (switched)
        {
            SwapNote = Strings.Format("Run.DebugSwitched", was, macro.Name);
        }
    }

    /// <summary>
    /// The macros a debug run can be started on when this window was opened without one, in the
    /// order the main list shows them. Whether a macro is armed makes no difference here: that is
    /// the switch on its trigger, not whether the macro itself can be run.
    /// </summary>
    public IReadOnlyList<MacroItem> DebugChoices => _listed;

    /// <summary>The macro the picker is standing on, or null while nobody has chosen one.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartDebugCommand))]
    public partial MacroItem? ChosenMacro { get; set; }

    /// <summary>Starting a debug run needs a macro to run.</summary>
    public bool CanStartDebug => ChosenMacro is not null;

    /// <summary>
    /// Loads the macro the picker is on, which is what the window opened from the main window offers
    /// instead of the run controls. It takes the card menu's own road, because it is the same thing
    /// asked for from a different place — and like that road it stops there rather than starting
    /// anything.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartDebug))]
    private Task StartDebug() => ChosenMacro is { } macro ? DebugAsync(macro) : Task.CompletedTask;

    /// <summary>
    /// The macros a run may call: the ones in the list as they stand, so a macro that was just
    /// edited is the one called. A window handed its own library uses that instead, which is what
    /// the editor's own run does — it knows exactly which steps are being run, saved or not.
    /// </summary>
    private IMacroLibrary Library => _macros ?? (_listed.Count == 0
        ? EmptyMacroLibrary.Instance
        : new ProjectMacroLibrary(CallableMacros.Entries(_listed)));

    /// <summary>Every step of the macro, in the order it runs, nested ones indented.</summary>
    public ObservableCollection<RunStepViewModel> Steps { get; } = [];

    /// <summary>What the macro has done so far.</summary>
    public ObservableCollection<RunLineViewModel> Lines { get; } = [];

    /// <summary>What the variables hold right now.</summary>
    public ObservableCollection<WatchedVariable> Variables { get; } = [];

    /// <summary>The declared parameters a direct run asks for before it starts, one row each.</summary>
    public ObservableCollection<RunArgumentViewModel> Arguments { get; } = [];

    public bool HasArguments => Arguments.Count > 0;

    /// <summary>
    /// How each step went. Kept apart from the macro's own values, and folded away by default: a
    /// macro has one of these for every step of it, so a list that mixed them in would bury the
    /// few values the user is actually watching.
    /// </summary>
    public ObservableCollection<StepOutcome> StepOutcomes { get; } = [];

    public bool HasStepOutcomes => StepOutcomes.Count > 0;

    /// <summary>The heading over the folded-away list, with the number of steps behind it.</summary>
    public string StepOutcomeHeader => Strings.Format("Run.StepOutcomes", StepOutcomes.Count);

    [ObservableProperty]
    public partial bool ShowStepOutcomes { get; set; }

    /// <summary>
    /// What each armed trigger is waiting for, one line each. A macro that was expected to run and
    /// did not is nearly always a trigger whose waiting is not true, and this is where that is read.
    /// Only the triggers something looks at on a clock are here: a key binding answers a key, so
    /// there is no moment it was last looked at.
    /// </summary>
    public ObservableCollection<RunTrigger> Triggers { get; } = [];

    public bool HasTriggers => Triggers.Count > 0;

    /// <summary>Reads the trigger service again, which the window does while it is open.</summary>
    public void RefreshTriggers()
    {
        Triggers.Clear();

        if (_triggers is { } service)
        {
            foreach (var watch in service.Watches())
            {
                Triggers.Add(RunTriggerLine.Of(watch));
            }
        }

        OnPropertyChanged(nameof(HasTriggers));
    }

    [RelayCommand]
    private void ToggleStepOutcomes() => ShowStepOutcomes = !ShowStepOutcomes;

    public bool HasVariables => Variables.Count > 0;

    public bool HasSteps => Steps.Count > 0;
}
