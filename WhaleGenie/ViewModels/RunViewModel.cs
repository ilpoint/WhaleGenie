using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

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

    /// <summary>True once this step has something to show for itself: what it last looked at.</summary>
    [ObservableProperty]
    public partial bool HasLook { get; set; }

    /// <summary>
    /// The picture this step last looked at, small. It is what makes a run readable after the
    /// fact: a log line says a picture was found, and this says what the screen looked like when
    /// that was decided.
    /// </summary>
    [ObservableProperty]
    public partial Bitmap? Thumbnail { get; set; }
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

/// <summary>Which half of the run-and-debug window is showing.</summary>
public enum RunPage
{
    /// <summary>The steps, how each of them went, the variables in play and the log.</summary>
    Debug,

    /// <summary>Which variables there are, and the shared ones' own fields.</summary>
    Variables,
}

/// <summary>The three pages the variables half is divided into.</summary>
public enum VariableTab
{
    /// <summary>Everything the macros in the list can use right now.</summary>
    Current,

    /// <summary>Only the variables a macro created for itself.</summary>
    Local,

    /// <summary>Only the shared variables, which are edited here.</summary>
    Global,

    /// <summary>Only the variables WhaleGenie provides.</summary>
    System,
}

/// <summary>The local variables one macro created, grouped under the macro's name.</summary>
public sealed class MacroVariableGroup
{
    public required string MacroName { get; init; }

    public required IReadOnlyList<VariableDefinition> Variables { get; init; }
}

/// <summary>How one step of the macro went, for the list beside the variables.</summary>
public sealed record StepOutcome(string Name, string Label, string Value)
{
    public string Color => Value switch
    {
        "failed" => "#E06C75",
        "ok" => "#8CC98C",
        _ => "#9A9A9A",
    };
}

/// <summary>
/// Backs the run window: it drives a <see cref="MacroRunner"/>, shows what the macro did,
/// and lets the user watch it one step at a time. It is the runner's host, so the engine
/// asks it to pause between steps and to hand a failure to the user.
/// </summary>
public partial class RunViewModel : ViewModelBase, IRunHost, IRunLooks
{
    /// <summary>
    /// How many looks the window keeps, and how much of them it is willing to hold. A macro that
    /// loops over a search would otherwise collect one picture per step until the window closed;
    /// the ones kept are the newest, which are the ones a person is looking at.
    /// </summary>
    private const int KeptLooks = 30;
    private const long KeptBytes = 128L * 1024 * 1024;

    /// <summary>The looks still held, newest last, with the step each belongs to.</summary>
    private readonly List<(string StepId, StepLook Look, long Bytes)> _looks = [];

    /// <summary>
    /// The steps being debugged. The window is opened from the macro list and stays where it is, so
    /// asking it to debug another macro replaces these rather than opening a second window over the
    /// same list.
    /// </summary>
    private IReadOnlyList<ExecutableStep> _steps;

    private readonly VariableStore _variables;
    private readonly IDeviceLayer _devices;
    private readonly double _delayScale;
    private readonly bool _ownsDevices;
    private readonly HashSet<string> _breakpoints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The macros this run may call, when the window was handed them outright: the editor's own run
    /// knows exactly which steps are being run, and it is those the macro being debugged is.
    /// </summary>
    private readonly IMacroLibrary? _macros;

    /// <summary>The names worth watching, read off the macro that is loaded.</summary>
    private HashSet<string> _watched;

    /// <summary>The macros in the list, which is what the variables in use are read off: the window
    /// asks what the whole project can use, not only what the macro being debugged touches.</summary>
    private readonly IReadOnlyList<MacroItem> _listed;

    /// <summary>How big the screen is, for the two rows that report it.</summary>
    private readonly int _screenWidth;
    private readonly int _screenHeight;

    private string _clipboard = string.Empty;
    private readonly List<VariableDefinition> _clipboardRows = [];

    private CancellationTokenSource? _cancellation;
    private TaskCompletionSource<bool>? _gate;
    private TaskCompletionSource<StepErrorChoice>? _answer;
    private TaskCompletionSource? _ended;
    private string? _runNote;
    private bool _stopAtNextStep;
    private string? _currentId;

    /// <summary>Raised when the window should close itself.</summary>
    public event Action? CloseRequested;

    private string _debugged = string.Empty;

    /// <summary>The name of the macro being debugged, or empty when this window has none.</summary>
    public string Debugged
    {
        get => _debugged;
        private set
        {
            if (_debugged == value)
            {
                return;
            }

            _debugged = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(WindowTitle));
        }
    }

    /// <summary>
    /// What the window calls what it is showing: the macro being debugged goes beside the title, so
    /// that a window left open on one macro is not mistaken for another's.
    /// </summary>
    public string WindowTitle => Debugged.Length == 0
        ? Strings.Get("Run.Title")
        : $"{Strings.Get("Run.Title")} · {Debugged}";

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
        IMacroLibrary? macros = null, IReadOnlyList<MacroItem>? listed = null)
    {
        _ownsDevices = devices is null;
        _devices = devices ?? new WindowsDeviceLayer();
        _delayScale = delayScale;
        _macros = macros;
        _listed = listed ?? [];
        _variables = MacroVariables.Seed();
        NewType = Types[0];

        (_screenWidth, _screenHeight) = ScreenSize();

        // Nothing is loaded yet, so the compiler is told what every path sets before it reads them.
        _steps = [];
        _watched = [];

        Globals.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasGlobals));

        Load(steps);
        Rebuild();
    }

    /// <summary>
    /// Points the window at a macro: what it debugs, in the order it runs. What the last macro left
    /// behind goes with it — breakpoints, the pictures it looked at and the lines it wrote are about
    /// the steps that are no longer there.
    /// </summary>
    public void Load(IEnumerable<MacroStep> steps)
    {
        var source = steps as IReadOnlyList<MacroStep> ?? [.. steps];
        _steps = source.ToExecutable();
        _watched = VariablesUsed(source);

        _breakpoints.Clear();
        Steps.Clear();
        Lines.Clear();
        ShowStepOutcomes = false;
        ReleaseLooks();
        Build(_steps, 0);
        RefreshVariables();

        // Opening the window on a macro is about to debug it; opening it with nothing to debug is
        // about the variables, because there is nothing else to show.
        Page = _steps.Count == 0 ? RunPage.Variables : RunPage.Debug;
        Status = _steps.Count == 0 ? Strings.Get("Run.Empty") : Strings.Get("Run.Ready");
    }

    /// <summary>
    /// Debugs a macro, which is what the card's own menu asks for. One of these windows is open at a
    /// time, so asking for a second macro puts its steps in this window and starts again; the run
    /// that was going is stopped first, and the reason the log begins with is that a run which ends
    /// early by itself looks like a failure.
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

        Load(macro.Steps);
        Debugged = macro.Name;
        if (switched)
        {
            _runNote = Strings.Format("Run.DebugSwitched", was, macro.Name);
            Status = _runNote;
        }

        if (RunCommand.CanExecute(null))
        {
            RunCommand.Execute(null);
        }
    }

    /// <summary>
    /// How big the screen is, for the rows that report it. A machine that hands out no screen — a
    /// run driven by a check on one, or a desktop opened without one — answers zero rather than
    /// failing the window that asked.
    /// </summary>
    private (int Width, int Height) ScreenSize()
    {
        try
        {
            var size = _devices.Screen.PrimarySize;
            return (size.Width, size.Height);
        }
        catch (Exception)
        {
            return (0, 0);
        }
    }

    /// <summary>
    /// The macros a run may call: the ones in the list as they stand, so a macro that was just
    /// edited is the one called. A window handed its own library uses that instead, which is what
    /// the editor's own run does — it knows exactly which steps are being run, saved or not.
    /// </summary>
    private IMacroLibrary Library => _macros ?? (_listed.Count == 0
        ? EmptyMacroLibrary.Instance
        : new ProjectMacroLibrary(_listed
            .Where(macro => macro.Name.Trim().Length > 0)
            .Select(macro => (macro.Name.Trim(), macro.Steps.ToExecutable(), macro.Callable.Signature))));

    /// <summary>Every step of the macro, in the order it runs, nested ones indented.</summary>
    public ObservableCollection<RunStepViewModel> Steps { get; } = [];

    /// <summary>What the macro has done so far.</summary>
    public ObservableCollection<RunLineViewModel> Lines { get; } = [];

    /// <summary>What the variables hold right now.</summary>
    public ObservableCollection<WatchedVariable> Variables { get; } = [];

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

    [RelayCommand]
    private void ToggleStepOutcomes() => ShowStepOutcomes = !ShowStepOutcomes;

    public bool HasVariables => Variables.Count > 0;

    public bool HasSteps => Steps.Count > 0;

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

        // A run asked for after another one was stopped says why the first one ended: a run that
        // stopped by itself and one that was stopped on the way to this read the same otherwise.
        if (_runNote is { } note)
        {
            _runNote = null;
            Lines.Add(new RunLineViewModel(
                DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                LogLevel.Warn, default, note));
        }

        IsBusy = true;
        Status = Strings.Get("Run.Running");
        IsAsking = false;
        CurrentId = null;

        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var ended = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ended = ended;

        try
        {
            var runner = new MacroRunner(
                _variables, new UiThreadRunHost(this), _devices, _delayScale, Library, this)
            {
                FailureScreenshot = LocalSettings.LoadFailureScreenshot(),
                FailureFolder = AppPaths.Logs,
            };

            // Off the interface thread: a step that talks to a device waits for it right there in
            // the same call, and a page that has not drawn the element yet is waited on for the
            // browser's own half-minute. Doing that on the interface thread is half a minute of a
            // window that will not repaint — and of a stop button that cannot be pressed.
            await Task.Run(() => runner.RunAsync(_steps, cancellation.Token), cancellation.Token);
        }
        finally
        {
            _cancellation = null;
            _ended = null;
            ended.TrySetResult();
            IsBusy = false;
            _stopAtNextStep = false;
            CurrentId = null;

            // The run is over, so nothing is being read any more.
            WindowRead?.Invoke(null);

            RefreshVariables();
            Status = Lines.Count > 0 ? Lines[^1].Text : Strings.Get("Run.Ready");
        }
    }

    /// <summary>Highlights the step that is about to run, and stops when asked to.</summary>
    public async Task BeforeStep(ExecutableStep step, int depth, CancellationToken token)
    {
        // Whatever the last step was reading, this one is not reading it yet: the frame comes down
        // and goes up again when this step's own reading is reported.
        WindowRead?.Invoke(null);

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
    public async Task<StepErrorChoice> Ask(string step, string reason, string detail, string comment,
        CancellationToken token)
    {
        Question = FailedStepPrompt.Question(step, reason, detail, comment);
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

    /// <summary>
    /// Takes what a step saw and puts it on that step's row. Called from the thread the run is on,
    /// so the picture is handed to the window's own thread before anything is done with it.
    /// </summary>
    public void Look(StepLook look)
        => Dispatcher.UIThread.Post(() => Keep(look));

    /// <summary>Opens one step's picture, which is the whole of what it saw rather than a thumbnail.</summary>
    public event Action<StepLook>? LookRequested;

    /// <summary>
    /// Where the window the run is reading right now sits, or null when it is reading nothing: a
    /// step that does not look at the screen, the desktop, or a window that has gone. A step's own
    /// picture says what was found, and this says where the engine was looking for it — which is the
    /// answer when a step looked in the wrong window and found nothing.
    /// </summary>
    public event Action<FramePlace?>? WindowRead;

    /// <summary>
    /// Told by the engine which window it is about to read, and where that window is, before every
    /// picture it takes: the engine looked the window up for this look, so a frame put up from what
    /// is handed over here follows the window rather than staying where it was when the step started.
    /// The call comes from the thread the run is on, so the frame is drawn on the thread that owns
    /// windows.
    /// </summary>
    public void Reading(WindowInfo? window) => Dispatcher.UIThread.Post(() => ReadingAt(window));

    /// <summary>
    /// Moves the frame to the window being read. A reading that lands after the run has stopped
    /// takes the frame down rather than putting one up: a frame left over a window nothing is
    /// reading any more says something untrue.
    /// </summary>
    private void ReadingAt(WindowInfo? window)
    {
        if (!IsBusy || window is null)
        {
            WindowRead?.Invoke(null);
            return;
        }

        WindowRead?.Invoke(FramePlace.Of(window));
    }

    /// <summary>The whole of what a step last saw, or null when it has looked at nothing.</summary>
    public StepLook? LookOf(string stepId)
        => _looks.LastOrDefault(kept => kept.StepId == stepId).Look;

    private void Keep(StepLook look)
    {
        var bytes = (long)look.Frame.Width * look.Frame.Height * 4
            + (look.Needle is { } needle ? (long)needle.Width * needle.Height * 4 : 0);

        _looks.Add((look.StepId, look, bytes));

        // Newest first out of the door: the picture a person wants is the one the run has just
        // made, and a macro that loops over a search makes one every time round.
        while (_looks.Count > KeptLooks || _looks.Sum(kept => kept.Bytes) > KeptBytes)
        {
            var gone = _looks[0].StepId;
            _looks.RemoveAt(0);
            Forget(gone);
        }

        if (Steps.FirstOrDefault(step => step.Id == look.StepId) is { } row)
        {
            var small = look.Frame.IsEmpty ? null : ImageAssets.ToBitmap(look.Frame);
            var previous = row.Thumbnail;
            row.Thumbnail = small;
            row.HasLook = small is not null;
            previous?.Dispose();
        }
    }

    /// <summary>
    /// Takes a step's picture off its row, once the run has let that picture go. Keeping the
    /// thumbnail of a look that is no longer held would put the memory back without the picture.
    /// </summary>
    private void Forget(string stepId)
    {
        if (_looks.Any(kept => kept.StepId == stepId))
        {
            return;
        }

        if (Steps.FirstOrDefault(step => step.Id == stepId) is { } row)
        {
            var previous = row.Thumbnail;
            row.Thumbnail = null;
            row.HasLook = false;
            previous?.Dispose();
        }
    }

    /// <summary>Lets go of the pictures the run kept, which the window does when it goes away.</summary>
    public void ReleaseLooks()
    {
        foreach (var step in Steps)
        {
            step.Thumbnail?.Dispose();
            step.Thumbnail = null;
        }

        _looks.Clear();
    }

    /// <summary>Asks for the picture of one step to be opened.</summary>
    [RelayCommand]
    private void OpenLook(RunStepViewModel? step)
    {
        if (step is not null && LookOf(step.Id) is { } look)
        {
            LookRequested?.Invoke(look);
        }
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

        RefreshStepOutcomes();
        OnPropertyChanged(nameof(HasVariables));
    }

    /// <summary>
    /// Reads back how each step went. Only the steps of this macro are asked about, and a step
    /// counts as answered only once the run has written something for it: before the run starts
    /// the list is empty rather than a column of "nothing yet".
    /// </summary>
    private void RefreshStepOutcomes()
    {
        StepOutcomes.Clear();
        foreach (var step in Steps)
        {
            if (step.Id.Length == 0)
            {
                continue;
            }

            var name = MacroRunner.OutcomeName(step.Id);
            if (_variables.Local.TryGet(name, out var value))
            {
                StepOutcomes.Add(new StepOutcome(name, step.Label, value.AsText()));
            }
        }

        OnPropertyChanged(nameof(HasStepOutcomes));
        OnPropertyChanged(nameof(StepOutcomeHeader));
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
                    // A list parameter keeps its values in rows, and a row may name the variable
                    // that says where to look — a picture an earlier Capture took, most often.
                    foreach (var value in parameter.IsRowList ? parameter.RowValues : [parameter.Value])
                    {
                        Note(value, defined, names);
                    }

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

        foreach (var name in WhaleGenie.Core.Expressions.Expression.ReferencedNames(trimmed))
        {
            names.Add(name);
        }
    }

    // ------------------------------------------------------------------- variables

    /// <summary>Built-in variables, with the value they hold right now.</summary>
    public ObservableCollection<VariableDefinition> SystemVariables { get; } = [];

    /// <summary>Every variable the macros in the list use: global, then local, then built-in.</summary>
    public ObservableCollection<VariableDefinition> CurrentVariables { get; } = [];

    /// <summary>
    /// The shared variables, as this window lists them. A copy rather than the catalogue itself: what
    /// the window binds is read and written on the thread that owns windows, and the catalogue is
    /// also written to from wherever a package is opened. The fields on the lines are the catalogue's
    /// own variables, so editing one edits the shared variable; 「刷新」 puts the list back in step
    /// when something outside has changed it.
    /// </summary>
    public ObservableCollection<VariableDefinition> Globals { get; } = [];

    public bool HasGlobals => Globals.Count > 0;

    /// <summary>Variables created inside each macro in the macro list.</summary>
    public ObservableCollection<MacroVariableGroup> LocalVariables { get; } = [];

    public bool HasLocalVariables => LocalVariables.Count > 0;

    /// <summary>Which set of variables is listed: the ones in use, or one scope on its own.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCurrentTab))]
    [NotifyPropertyChangedFor(nameof(IsLocalTab))]
    [NotifyPropertyChangedFor(nameof(IsGlobalTab))]
    [NotifyPropertyChangedFor(nameof(IsSystemTab))]
    public partial VariableTab SelectedTab { get; set; } = VariableTab.Current;

    public bool IsCurrentTab => SelectedTab is VariableTab.Current;

    public bool IsLocalTab => SelectedTab is VariableTab.Local;

    public bool IsGlobalTab => SelectedTab is VariableTab.Global;

    public bool IsSystemTab => SelectedTab is VariableTab.System;

    /// <summary>Whether the "in use" list also shows the built-in variables a macro touched.</summary>
    [ObservableProperty]
    public partial bool ShowSystemVariables { get; set; }

    public bool HasCurrentVariables => CurrentVariables.Count > 0;

    partial void OnShowSystemVariablesChanged(bool value) => RebuildCurrent();

    [RelayCommand]
    private void SelectTab(string? tab) => SelectedTab = tab switch
    {
        nameof(VariableTab.Local) => VariableTab.Local,
        nameof(VariableTab.Global) => VariableTab.Global,
        nameof(VariableTab.System) => VariableTab.System,
        _ => VariableTab.Current,
    };

    /// <summary>The types a shared variable can be made of, named the way the fields name them.</summary>
    public IReadOnlyList<ActionParameterOption> Types { get; } =
    [
        .. VariableDefinition.TypeNames.Select(name =>
            new ActionParameterOption(name, Strings.Get($"Variable.Type.{name}", name))),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddGlobalCommand))]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewTypeIsList))]
    [NotifyPropertyChangedFor(nameof(NewDefaultPreview))]
    [NotifyPropertyChangedFor(nameof(NewDefaultHasError))]
    [NotifyPropertyChangedFor(nameof(ShowNewDefaultSummary))]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddGlobalCommand))]
    public partial ActionParameterOption NewType { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NewDefaultPreview))]
    [NotifyPropertyChangedFor(nameof(NewDefaultHasError))]
    [NotifyPropertyChangedFor(nameof(ShowNewDefaultSummary))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyCanExecuteChangedFor(nameof(AddGlobalCommand))]
    public partial string NewDefault { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewDescription { get; set; } = string.Empty;

    /// <summary>Explains why a new shared variable cannot be added yet.</summary>
    public string ValidationMessage
    {
        get
        {
            var name = NewName.Trim();
            if (name.Length == 0)
            {
                return string.Empty;
            }

            if (VariableCatalog.IsSystem(name))
            {
                return Strings.Format("Variable.NameIsSystem", name);
            }

            if (VariableCatalog.IsGlobal(name))
            {
                return Strings.Format("Variable.NameTaken", name);
            }

            return NewDefaultHasError ? Strings.Get("Variable.InvalidDefault") : string.Empty;
        }
    }

    public bool CanAdd => NewName.Trim().Length > 0 && ValidationMessage.Length == 0;

    /// <summary>True while the type chosen for a new shared variable holds a list.</summary>
    public bool NewTypeIsList => NewType.Value is "list";

    /// <summary>How the starting value of the new shared variable reads, showing what a list holds.</summary>
    public string NewDefaultPreview => VariableDefinition.Preview(NewType.Value, NewDefault);

    /// <summary>True while the starting value of the new shared variable cannot be read.</summary>
    public bool NewDefaultHasError => VariableDefinition.HasError(NewType.Value, NewDefault);

    /// <summary>True while the new variable's preview shows what a list holds.</summary>
    public bool ShowNewDefaultSummary => NewTypeIsList && !NewDefaultHasError;

    /// <summary>Reads the built-in variables again and re-lists the macro variables.</summary>
    public void Rebuild()
    {
        SystemVariables.Clear();
        _clipboardRows.Clear();

        Globals.Clear();
        foreach (var shared in VariableCatalog.Globals)
        {
            Globals.Add(shared);
        }

        foreach (var variable in VariableCatalog.SystemVariables)
        {
            var row = new VariableDefinition
            {
                Name = variable.Name,
                Scope = variable.Scope,
                Type = variable.Type,
                Description = variable.Description,
                DefaultValue = variable.DefaultValue,
            };

            row.CurrentValue = variable.Name == "sys.clipboard"
                ? _clipboard
                : SystemVariableReader.Read(variable.Name, _screenWidth, _screenHeight);

            if (variable.Name == "sys.clipboard")
            {
                _clipboardRows.Add(row);
            }

            SystemVariables.Add(row);
        }

        LocalVariables.Clear();
        foreach (var macro in _listed)
        {
            var names = macro.LocalVariableNames();
            if (names.Count == 0)
            {
                continue;
            }

            LocalVariables.Add(new MacroVariableGroup
            {
                MacroName = macro.Name,
                Variables =
                [
                    .. names.Select(name => new VariableDefinition
                    {
                        Name = name,
                        Scope = VariableScope.Local,
                        Owner = macro.Name,
                        Description = Strings.Get("Variable.LocalDescription"),
                        CurrentValue = Strings.Get("Variable.NotSet"),
                    }),
                ],
            });
        }

        OnPropertyChanged(nameof(HasLocalVariables));
        RebuildCurrent();
    }

    /// <summary>
    /// Lists the variables the macros in the list actually use, with the scope and the macros that
    /// use them, so their state can be checked before a macro runs. A name a macro uses but no step
    /// defines is called out, which is usually a typo.
    /// </summary>
    private void RebuildCurrent()
    {
        CurrentVariables.Clear();

        var system = VariableCatalog.SystemVariables
            .ToDictionary(variable => variable.Name, StringComparer.OrdinalIgnoreCase);
        var globals = VariableCatalog.Globals
            .ToDictionary(variable => variable.Name, StringComparer.OrdinalIgnoreCase);
        var live = SystemVariables
            .ToDictionary(variable => variable.Name, variable => variable.CurrentValue,
                StringComparer.OrdinalIgnoreCase);

        var used = new Dictionary<string, Usage>(StringComparer.OrdinalIgnoreCase);

        foreach (var macro in _listed)
        {
            var locals = macro.LocalVariableNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
            var known = new HashSet<string>(system.Keys, StringComparer.OrdinalIgnoreCase);
            known.UnionWith(globals.Keys);
            known.UnionWith(locals);

            foreach (var name in UsedNames(macro, known))
            {
                var scope = locals.Contains(name) ? VariableScope.Local
                    : globals.ContainsKey(name) ? VariableScope.Global
                    : system.ContainsKey(name) ? VariableScope.System
                    : VariableScope.Unknown;

                if (scope is VariableScope.System && !ShowSystemVariables)
                {
                    continue;
                }

                // Two macros can each own a local variable with the same name.
                var key = scope is VariableScope.Local ? $"{name}\u0000{macro.Name}" : name;
                if (!used.TryGetValue(key, out var usage))
                {
                    usage = new Usage(name, scope);
                    used[key] = usage;
                }

                usage.Owners.Add(macro.Name);
            }
        }

        foreach (var usage in used.Values
                     .OrderBy(entry => RankOf(entry.Scope))
                     .ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
        {
            var row = new VariableDefinition
            {
                Name = usage.Name,
                Scope = usage.Scope,
                Owner = string.Join(", ", usage.Owners),
                Description = DescriptionOf(usage, system, globals),
                Type = TypeOf(usage, system, globals),
                CurrentValue = ValueOf(usage, system, globals, live),
            };

            if (usage.Scope is VariableScope.System && usage.Name == "sys.clipboard")
            {
                _clipboardRows.Add(row);
            }

            CurrentVariables.Add(row);
        }

        OnPropertyChanged(nameof(HasCurrentVariables));
    }

    /// <summary>Which variables a macro reads: the ones it defines, plus the ones it names.</summary>
    private static SortedSet<string> UsedNames(MacroItem macro, IReadOnlySet<string> known)
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var step in macro.Steps)
        {
            Collect(step);
        }

        return names;

        void Collect(MacroStep step)
        {
            foreach (var parameter in step.Parameters)
            {
                switch (parameter.Kind)
                {
                    case ActionParameterKind.Steps:
                        foreach (var child in parameter.Steps)
                        {
                            Collect(child);
                        }

                        break;
                    case ActionParameterKind.Condition:
                        if (parameter.Condition is not null)
                        {
                            Collect(parameter.Condition);
                        }

                        break;
                    default:
                        // A list parameter keeps its values in rows: whichever of them names a
                        // variable is one the macro reads, the same as any plain field that starts
                        // with a "$".
                        foreach (var value in
                            parameter.IsRowList ? parameter.RowValues : [parameter.Value])
                        {
                            Plain(step, parameter, value);
                        }

                        break;
                }
            }
        }

        /// <summary>
        /// One value the macro reads: a whole-value expression may name several variables anywhere
        /// inside it, a plain field names one when it starts with a "$", and a field that always
        /// names a variable counts even when nothing defines it — which is worth showing while
        /// debugging.
        /// </summary>
        void Plain(MacroStep step, StepParameter parameter, string value)
        {
            var text = value.Trim();
            if (text.Length == 0)
            {
                return;
            }

            if (parameter.Kind is ActionParameterKind.Expression)
            {
                foreach (var reference in Expression.ReferencedNames(text))
                {
                    names.Add(reference);
                }

                return;
            }

            if (text.StartsWith('$'))
            {
                var reference = text[1..].Trim();
                if (reference.Length > 0)
                {
                    names.Add(reference);
                }

                return;
            }

            if (known.Contains(text))
            {
                names.Add(text);
                return;
            }

            // A parameter that always names a variable can point at a name nothing defines, which
            // is worth showing while debugging.
            var declares = step.Definition?.Parameters
                .FirstOrDefault(candidate => candidate.Name == parameter.Name)?.NamesVariable;
            if (declares == true)
            {
                names.Add(text);
            }
        }
    }

    private static int RankOf(VariableScope scope) => scope switch
    {
        VariableScope.Global => 0,
        VariableScope.Local => 1,
        VariableScope.System => 2,
        _ => 3,
    };

    private static string DescriptionOf(Usage usage,
        IReadOnlyDictionary<string, VariableDefinition> system,
        IReadOnlyDictionary<string, VariableDefinition> globals) => usage.Scope switch
        {
            VariableScope.System when system.TryGetValue(usage.Name, out var builtIn) => builtIn.LocalDescription,
            VariableScope.Global when globals.TryGetValue(usage.Name, out var shared) => shared.Description,
            VariableScope.Local => Strings.Get("Variable.LocalDescription"),
            _ => Strings.Get("Variable.UnknownHint"),
        };

    private static string TypeOf(Usage usage,
        IReadOnlyDictionary<string, VariableDefinition> system,
        IReadOnlyDictionary<string, VariableDefinition> globals)
        => usage.Scope switch
        {
            VariableScope.System when system.TryGetValue(usage.Name, out var builtIn) => builtIn.Type,
            VariableScope.Global when globals.TryGetValue(usage.Name, out var shared) => shared.Type,
            _ => "text",
        };

    private static string ValueOf(Usage usage,
        IReadOnlyDictionary<string, VariableDefinition> system,
        IReadOnlyDictionary<string, VariableDefinition> globals,
        IReadOnlyDictionary<string, string> live)
        => usage.Scope switch
        {
            VariableScope.System when live.TryGetValue(usage.Name, out var current) => current,
            VariableScope.Global when globals.TryGetValue(usage.Name, out var shared)
                && shared.DefaultValue.Length > 0 => VariableDefinition.Preview(shared.Type, shared.DefaultValue),
            _ => Strings.Get("Variable.NotSet"),
        };

    /// <summary>One variable in use, together with the macros that use it.</summary>
    private sealed class Usage(string name, VariableScope scope)
    {
        public string Name { get; } = name;

        public VariableScope Scope { get; } = scope;

        public SortedSet<string> Owners { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Fills in the clipboard text, which cannot be read while the rows are built.</summary>
    public void SetClipboard(string text)
    {
        _clipboard = text;
        foreach (var row in _clipboardRows)
        {
            row.CurrentValue = text;
        }
    }

    [RelayCommand]
    private void Refresh() => Rebuild();

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void AddGlobal()
    {
        var created = new VariableDefinition
        {
            Name = NewName.Trim(),
            Scope = VariableScope.Global,
            Type = NewType.Value,
            DefaultValue = NewDefault.Trim(),
            Description = NewDescription.Trim(),
        };

        VariableCatalog.Globals.Add(created);
        Globals.Add(created);

        NewName = string.Empty;
        NewDefault = string.Empty;
        NewDescription = string.Empty;
    }

    [RelayCommand]
    private void RemoveGlobal(VariableDefinition? variable)
    {
        if (variable is not null)
        {
            VariableCatalog.Globals.Remove(variable);
            Globals.Remove(variable);
        }
    }

}
