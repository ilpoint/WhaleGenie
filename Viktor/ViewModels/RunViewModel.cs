using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Viktor.Core.Devices;
using Viktor.Core.Devices.Platform;
using Viktor.Core.Execution;
using Viktor.Core.Variables;
using Viktor.Execution;
using Viktor.Localization;
using Viktor.Models;

namespace Viktor.ViewModels;

/// <summary>One step in the debugger's list.</summary>
public partial class RunStepViewModel : ObservableObject
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required string Detail { get; init; }

    /// <summary>How deep the step sits, used to indent it under its parent.</summary>
    public required int Depth { get; init; }

    /// <summary>Left margin that lines the step up under its parent.</summary>
    public Thickness Indent => new(Depth * 16, 0, 0, 0);

    [ObservableProperty]
    public partial bool HasBreakpoint { get; set; }

    [ObservableProperty]
    public partial bool IsCurrent { get; set; }
}

/// <summary>One line of the run log, already translated.</summary>
public sealed record RunLineViewModel(string Time, LogLevel Level, Thickness Indent, string Text)
{
    public string Color => Level switch
    {
        LogLevel.Error => "#E06C75",
        LogLevel.Warn => "#D8B36A",
        LogLevel.Debug => "#808080",
        _ => "#CFCFCF",
    };
}

/// <summary>One variable the run can see.</summary>
public sealed record WatchedVariable(string Name, string Scope, string Value);

/// <summary>
/// Backs the run window: it drives a <see cref="MacroRunner"/>, shows what the macro did,
/// and lets the user watch it one step at a time. It is the runner's host, so the engine
/// asks it to pause between steps and to hand a failure to the user.
/// </summary>
public partial class RunViewModel : ViewModelBase, IRunHost
{
    private readonly IReadOnlyList<ExecutableStep> _steps;
    private readonly VariableStore _variables;
    private readonly IDeviceLayer _devices;
    private readonly double _delayScale;
    private readonly IMacroLibrary _macros;
    private readonly HashSet<string> _watched;
    private readonly bool _ownsDevices;
    private readonly HashSet<string> _breakpoints = new(StringComparer.OrdinalIgnoreCase);

    private CancellationTokenSource? _cancellation;
    private TaskCompletionSource<bool>? _gate;
    private TaskCompletionSource<StepErrorChoice>? _answer;
    private bool _stopAtNextStep;
    private string? _currentId;

    /// <summary>Raised when the window should close itself.</summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Called once the window is gone. The device layer is only let go when this window made
    /// it, so a layer handed in from outside stays the caller's to dispose.
    /// </summary>
    public void ReleaseDevices()
    {
        if (_ownsDevices && _devices is IDisposable owned)
        {
            owned.Dispose();
        }
    }

    public RunViewModel(IEnumerable<MacroStep> steps, IDeviceLayer? devices = null, double delayScale = 1,
        IMacroLibrary? macros = null)
    {
        var source = steps as IReadOnlyList<MacroStep> ?? [.. steps];
        _steps = source.ToExecutable();
        _ownsDevices = devices is null;
        _devices = devices ?? new WindowsDeviceLayer();
        _delayScale = delayScale;
        _macros = macros ?? EmptyMacroLibrary.Instance;
        _watched = VariablesUsed(source);
        _variables = MacroVariables.Seed();

        Build(_steps, 0);
        RefreshVariables();
        Status = _steps.Count == 0 ? Strings.Get("Run.Empty") : Strings.Get("Run.Ready");
    }

    /// <summary>Every step of the macro, in the order it runs, nested ones indented.</summary>
    public ObservableCollection<RunStepViewModel> Steps { get; } = [];

    /// <summary>What the macro has done so far.</summary>
    public ObservableCollection<RunLineViewModel> Lines { get; } = [];

    /// <summary>What the variables hold right now.</summary>
    public ObservableCollection<WatchedVariable> Variables { get; } = [];

    public bool HasVariables => Variables.Count > 0;

    public bool HasSteps => Steps.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunCommand))]
    [NotifyCanExecuteChangedFor(nameof(StepCommand))]
    [NotifyCanExecuteChangedFor(nameof(ContinueCommand))]
    [NotifyCanExecuteChangedFor(nameof(StopCommand))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>Set while a failure is waiting for the user to choose what happens next.</summary>
    [ObservableProperty]
    public partial bool IsAsking { get; set; }

    [ObservableProperty]
    public partial string Question { get; set; } = string.Empty;

    /// <summary>Running from the top needs steps and an idle window.</summary>
    public bool CanStart => !IsBusy && _steps.Count > 0;

    /// <summary>Stepping works before a run starts and while one is paused.</summary>
    public bool CanStep => _steps.Count > 0;

    public bool CanStop => IsBusy;

    /// <summary>True when this run bends the macro's timings away from how they were written.</summary>
    public bool IsSpeedAdjusted => Math.Abs(_delayScale - 1) > 0.001;

    /// <summary>The run-speed badge shown in the toolbar, for example <c>×2</c>.</summary>
    public string SpeedNote => DelayScaleViewModel.FormatScale(_delayScale);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Run()
    {
        // Run goes all the way through, only stopping where a breakpoint waits.
        _stopAtNextStep = false;
        _ = RunAsync();
    }

    [RelayCommand(CanExecute = nameof(CanStep))]
    private void Step()
    {
        _stopAtNextStep = true;

        if (IsBusy)
        {
            Release();
        }
        else
        {
            _ = RunAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Continue()
    {
        _stopAtNextStep = false;
        Release();
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _stopAtNextStep = false;
        _cancellation?.Cancel();
        Release();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();

    [RelayCommand]
    private void ToggleBreakpoint(RunStepViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        row.HasBreakpoint = !row.HasBreakpoint;
        if (row.HasBreakpoint)
        {
            _breakpoints.Add(row.Id);
        }
        else
        {
            _breakpoints.Remove(row.Id);
        }
    }

    /// <summary>Answers the question a failed step asked.</summary>
    [RelayCommand]
    private void Answer(string? choice)
    {
        _answer?.TrySetResult(choice switch
        {
            "retry" => StepErrorChoice.Retry,
            "skip" => StepErrorChoice.Skip,
            _ => StepErrorChoice.Stop,
        });
    }

    private async Task RunAsync()
    {
        Lines.Clear();
        IsBusy = true;
        Status = Strings.Get("Run.Running");
        IsAsking = false;
        CurrentId = null;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;

        try
        {
            await new MacroRunner(_variables, this, _devices, _delayScale, _macros)
            {
                FailureScreenshot = LocalSettings.LoadFailureScreenshot(),
            }.RunAsync(_steps, cancellation.Token);
        }
        finally
        {
            _cancellation = null;
            IsBusy = false;
            _stopAtNextStep = false;
            CurrentId = null;
            RefreshVariables();
            Status = Lines.Count > 0 ? Lines[^1].Text : Strings.Get("Run.Ready");
        }
    }

    /// <summary>Highlights the step that is about to run, and stops when asked to.</summary>
    public async Task BeforeStep(ExecutableStep step, int depth, CancellationToken token)
    {
        CurrentId = step.Id;
        RefreshVariables();

        if (!_stopAtNextStep && !_breakpoints.Contains(step.Id))
        {
            return;
        }

        _stopAtNextStep = false;
        Status = Strings.Get("Run.Paused");

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gate = gate;
        using var registration = token.Register(() => gate.TrySetResult(false));

        var go = await gate.Task;
        _gate = null;
        Status = Strings.Get("Run.Running");

        if (!go)
        {
            throw new OperationCanceledException(token);
        }
    }

    /// <summary>Hands a failed step to the user, who picks what happens next.</summary>
    public async Task<StepErrorChoice> Ask(string step, string reason, string detail, CancellationToken token)
    {
        Question = FailedStepPrompt.Question(step, reason, detail);
        IsAsking = true;

        var answer = new TaskCompletionSource<StepErrorChoice>(TaskCreationOptions.RunContinuationsAsynchronously);
        _answer = answer;
        using var registration = token.Register(() => answer.TrySetResult(StepErrorChoice.Stop));

        var choice = await answer.Task;
        _answer = null;
        IsAsking = false;
        return choice;
    }

    /// <summary>Writes one translated line into the log.</summary>
    public void Log(LogEntry entry)
    {
        var text = entry.Arguments.Count == 0
            ? Strings.Get(entry.Key)
            : Strings.Format(entry.Key, [.. entry.Arguments]);

        Lines.Add(new RunLineViewModel(
            entry.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            entry.Level,
            new Thickness(entry.Depth * 14, 0, 0, 0),
            text));
    }

    private string? CurrentId
    {
        get => _currentId;
        set
        {
            if (_currentId == value)
            {
                return;
            }

            _currentId = value;
            foreach (var row in Steps)
            {
                row.IsCurrent = string.Equals(row.Id, value, StringComparison.Ordinal);
            }
        }
    }

    private void Release()
    {
        var gate = _gate;
        _gate = null;
        gate?.TrySetResult(true);
    }

    private void Build(IReadOnlyList<ExecutableStep> steps, int depth)
    {
        foreach (var step in steps)
        {
            Steps.Add(new RunStepViewModel
            {
                Id = step.Id,
                Label = FailedStepPrompt.Name(step.Type),
                Detail = Detail(step),
                Depth = depth,
            });

            // The condition reads first, so an "if"/"while" shows its test above the bodies.
            foreach (var parameter in step.Parameters)
            {
                if (parameter.Condition is { } condition)
                {
                    Build([condition], depth + 1);
                }
            }

            Build(step.Children("steps"), depth + 1);
            Build(step.Children("then"), depth + 1);
            Build(step.Children("else"), depth + 1);
            Build(step.Children("body"), depth + 1);
            Build(step.Children("catch"), depth + 1);
            Build(step.Children("finally"), depth + 1);
            Build(step.Children("conditions"), depth + 1);
        }
    }

    private static string Detail(ExecutableStep step)
    {
        var parts = step.Parameters
            .Where(parameter => parameter.Text.Trim().Length > 0)
            .Select(parameter => $"{parameter.Name} = {parameter.Text.Trim()}")
            .ToList();

        var text = parts.Count == 0 ? step.Type : $"{step.Type} · {string.Join(", ", parts)}";
        return text.Length <= 90 ? text : text[..87] + "\u2026";
    }

    private void RefreshVariables()
    {
        Variables.Clear();
        // Only what this macro touches: a macro that never reads the clock should not have
        // the clock sitting in its watch list.
        var rows = _variables.Flatten()
            .Where(pair => _watched.Contains(pair.Key))
            .Select(pair => new WatchedVariable(pair.Key, ScopeOf(pair.Key), pair.Value.AsText()))
            .OrderBy(row => Rank(row.Name))
            .ThenBy(row => row.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            Variables.Add(row);
        }

        OnPropertyChanged(nameof(HasVariables));
    }

    private int Rank(string name) => _variables.Local.Contains(name) ? 0
        : _variables.Global.Contains(name) ? 1
        : 2;

    private string ScopeOf(string name) => _variables.Local.Contains(name)
        ? Strings.Get("Variable.Scope.Local")
        : _variables.Global.Contains(name)
            ? Strings.Get("Variable.Scope.Global")
            : Strings.Get("Variable.Scope.System");

    /// <summary>
    /// The names worth watching: everything the macro creates for itself, plus everything it
    /// reads. Anything else stays out of the list.
    /// </summary>
    private static HashSet<string> VariablesUsed(IReadOnlyList<MacroStep> steps)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var defined = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in steps)
        {
            step.CollectVariables(defined);
        }

        // The macro's own variables are worth watching even before a step sets them.
        names.UnionWith(defined);

        foreach (var step in steps)
        {
            CollectUses(step, defined, names);
        }

        return names;
    }

    private static void CollectUses(MacroStep step, HashSet<string> defined, HashSet<string> names)
    {
        foreach (var parameter in step.Parameters)
        {
            switch (parameter.Kind)
            {
                case ActionParameterKind.Steps:
                    foreach (var child in parameter.Steps)
                    {
                        CollectUses(child, defined, names);
                    }

                    break;
                case ActionParameterKind.Condition:
                    if (parameter.Condition is { } condition)
                    {
                        CollectUses(condition, defined, names);
                    }

                    break;
                default:
                    Note(parameter.Value, defined, names);
                    break;
            }
        }
    }

    /// <summary>
    /// Reads a field the way the runner would: a bare variable name, or <c>$name</c> mentioned
    /// inside a longer piece of text or an expression.
    /// </summary>
    private static void Note(string text, HashSet<string> defined, HashSet<string> names)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        if (defined.Contains(trimmed) || VariableCatalog.IsSystem(trimmed) || VariableCatalog.IsGlobal(trimmed))
        {
            names.Add(trimmed);
        }

        foreach (var name in Viktor.Core.Expressions.Expression.ReferencedNames(trimmed))
        {
            names.Add(name);
        }
    }

}
