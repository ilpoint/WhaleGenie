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
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.Execution;
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

/// <summary>
/// One value a step worked out, as the panel under its row shows it: what it is called and what it
/// holds. <paramref name="Kept"/> says whether the macro keeps it under a name of its own; a value
/// nobody ticked is shown all the same, because looking at what a step could hand over is the
/// reason to open a debugger at all.
/// </summary>
public sealed record RunResultViewModel(string Name, string Value, bool Kept)
{
    /// <summary>
    /// The fields of a record result, one line each, or empty when the result is one piece of data
    /// whose text is the whole of it. A hit is several answers about one thing — where it is, how
    /// sure the reading was — and a line that ran them together is exactly what cannot be read.
    /// </summary>
    public IReadOnlyList<RunResultLine> Lines { get; init; } = [];

    /// <summary>
    /// The colour the name reads in: a value the macro keeps under a name of its own stands out
    /// from one that is only here to be looked at.
    /// </summary>
    public string Color => Kept ? "#BFE3BF" : "#8FB3D9";
}

/// <summary>
/// One line under a step's result: a field of a record, or one entry of a list of them, indented by
/// how deep it sits.
/// </summary>
public sealed record RunResultLine(string Name, string Value, Thickness Indent);

/// <summary>
/// Turns the fields of a record into those lines. It is arithmetic of its own rather than a
/// walk over the panel's controls, because the shape is what is worth checking: a hit is three
/// answers about one place, and a list of hits is three places with each one's answers under it.
/// </summary>
public static class RunResultLines
{
    /// <summary>How far in one level of nesting puts a line.</summary>
    private const double IndentStep = 16;

    /// <summary>The lines for the fields of one result, the fields themselves one level in.</summary>
    public static IReadOnlyList<RunResultLine> Of(IReadOnlyList<JsonTreeNode> fields)
    {
        var lines = new List<RunResultLine>();
        Add(fields, depth: 1, lines);
        return lines;
    }

    private static void Add(IReadOnlyList<JsonTreeNode> nodes, int depth, List<RunResultLine> lines)
    {
        foreach (var node in nodes)
        {
            lines.Add(new RunResultLine(node.Name, node.Preview,
                new Thickness(depth * IndentStep, 0, 0, 0)));

            // A field that is a record or a list of them keeps its own name and puts what is inside
            // it on the lines below, so a list of hits reads as hits rather than as one long line.
            if (node.Children.Count > 0)
            {
                Add(node.Children, depth + 1, lines);
            }
        }
    }
}

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

    /// <summary>
    /// Whether the engine can stop in front of this row. A condition is asked by the step it
    /// belongs to rather than run on its own, so nothing pauses before one: a dot on that row would
    /// be a promise the run cannot keep, and stopping before the question is asked is what the dot
    /// on the step it belongs to is for.
    /// </summary>
    public required bool CanBreak { get; init; }

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

    /// <summary>True once this step has run, so the panel under it has something to say.</summary>
    [ObservableProperty]
    public partial bool HasRun { get; set; }

    /// <summary>How the step ended, in the interface's own words.</summary>
    [ObservableProperty]
    public partial string OutcomeLabel { get; set; } = string.Empty;

    /// <summary>The colour that ending reads in, the way a step's outcome is coloured elsewhere.</summary>
    [ObservableProperty]
    public partial string OutcomeColor { get; set; } = "#9A9A9A";

    /// <summary>How long the step took, written the way the interface writes a length of time.</summary>
    [ObservableProperty]
    public partial string TimeNote { get; set; } = string.Empty;

    /// <summary>Why the step failed, or empty when it did not.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailure))]
    public partial string FailureNote { get; set; } = string.Empty;

    /// <summary>True when this step has something to say about why it failed.</summary>
    public bool HasFailure => FailureNote.Length > 0;

    /// <summary>The condition this breakpoint waits for, or empty while it always stops.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCondition))]
    public partial string ConditionNote { get; set; } = string.Empty;

    public bool HasCondition => ConditionNote.Length > 0;

    /// <summary>
    /// Everything the step worked out, read-only: what the macro keeps and what it does not. Ticking
    /// a result decides whether it becomes a variable, never whether it can be read here.
    /// </summary>
    public ObservableCollection<RunResultViewModel> Results { get; } = [];

    public bool HasResults => Results.Count > 0;

    /// <summary>Says how many values the step worked out.</summary>
    public string ResultsHeader => Strings.Format("Run.StepResults", Results.Count);

    /// <summary>
    /// Takes what this step came to: how it ended, how long it took, why if it failed, and every
    /// value it worked out. A step that ran more than once — a loop, a retry — is answered by the
    /// last time round, which is what its row is about.
    /// </summary>
    public void Took(StepRun run)
    {
        HasRun = true;
        OutcomeLabel = Strings.Get(run.Outcome switch
        {
            "failed" => "Run.OutcomeFailed",
            "skipped" => "Run.OutcomeSkipped",
            _ => "Run.OutcomeOk",
        });
        OutcomeColor = run.Outcome switch
        {
            "failed" => "#E06C75",
            "ok" => "#8CC98C",
            _ => "#9A9A9A",
        };
        TimeNote = DurationUnit.Written(Math.Round((decimal)run.ElapsedMs));
        FailureNote = run.FailureKey.Length == 0
            ? string.Empty
            : run.FailureDetail.Length == 0
                ? Strings.Get(run.FailureKey)
                : Strings.Format(run.FailureKey, run.FailureDetail);

        Results.Clear();
        foreach (var result in run.Results)
        {
            // A result the macro keeps reads as the name it goes by; one nobody ticked reads as the
            // engine's own name for it, which is what there is to call it.
            var kept = result.Name.Length > 0;
            var fields = result.Data is { } data ? RunResultLines.Of(JsonTreeNode.Fields(data)) : [];
            Results.Add(new RunResultViewModel(kept ? result.Name : result.Key,
                fields.Count == 0 ? result.Text : string.Empty, kept)
            {
                // A record's own text is the whole of it written as JSON, which is longer and less
                // readable than the fields it is made of: the fields are what the row shows.
                Lines = fields,
            });
        }

        OnPropertyChanged(nameof(HasResults));
    }

    /// <summary>
    /// Keeps what the step's breakpoint waits for, so the row says why a run may go past it: a
    /// breakpoint with a condition is not the same promise as one without.
    /// </summary>
    public void Waits(MacroStep? condition)
    {
        ConditionNote = condition is null
            ? string.Empty
            : Strings.Format("Run.BreakpointWhen",
                RunViewModel.Detail(new[] { condition }.ToExecutable()[0]));
    }
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
/// One line of what a trigger is waiting for, already written out. The words come from the text
/// table, so the two languages read alike and the line is not put together twice.
/// </summary>
public sealed record TriggerWatchLine(string Text)
{
    public static TriggerWatchLine Of(TriggerWatch watch)
    {
        var parts = new List<string> { watch.Macro, watch.Looking };

        if (watch.Matched is { } matched)
        {
            parts.Add(Strings.Get(matched ? "Run.TriggerThere" : "Run.TriggerAway"));
        }

        parts.Add(Strings.Format("Run.TriggerLooked", Moment(watch.Checked), Moment(watch.Next)));

        if (watch.Told)
        {
            parts.Add(Strings.Get("Run.TriggerTold"));
        }

        return new TriggerWatchLine(string.Join(" · ", parts.Where(part => part.Length > 0)));
    }

    /// <summary>A time of day, or the words for a trigger nothing is on a clock for.</summary>
    private static string Moment(DateTime moment)
        => moment == default ? Strings.Get("Run.TriggerNoClock") : moment.ToString("HH:mm:ss");
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
    /// <summary>The steps a run stops in front of, named the way a step's results name it.</summary>
    private readonly HashSet<string> _breakpoints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The condition each breakpoint waits for, for the ones that have one: a breakpoint with a
    /// condition is a question about the values in play, and it stops the run only when the answer
    /// is yes. A breakpoint without an entry here always stops.
    /// </summary>
    private readonly Dictionary<string, MacroStep> _breakpointConditions =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True while the step in front of the run is to be left out: what "skip this step once" sets,
    /// and what the engine reads back as the answer to its call before the step.
    /// </summary>
    private bool _skipThisStep;

    /// <summary>
    /// Whether the macro being debugged asked to share the input door with the other macros that
    /// asked for it. A window opened on its own, with no macro behind it, has nothing to share.
    /// </summary>
    private bool _exclusiveInput;

    /// <summary>
    /// Whose turn it is at the input door, as a line to read. It is shown only for the macros that
    /// asked to share the door, because for any other macro the answer is always the same and says
    /// nothing.
    /// </summary>
    public string InputOwnerLine => InputGate.Owner.Length == 0
        ? Strings.Get("Run.InputOwnerNone")
        : Strings.Format("Run.InputOwner", InputGate.Owner);

    /// <summary>True while this macro takes turns at the input door, so the line means something.</summary>
    public bool ShowsInputOwner => _exclusiveInput;

    /// <summary>The door changed hands, so what the line says has changed with it.</summary>
    public void RefreshInputOwner() => OnPropertyChanged(nameof(InputOwnerLine));

    /// <summary>The names a breakpoint's condition may use, read off the macro being debugged.</summary>
    private IReadOnlyList<VariableChoice> _choices = [];

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

    /// <summary>The trigger service of the main window, when there is one to ask.</summary>
    private readonly MacroTriggerService? _triggers;

    /// <summary>
    /// Where a built-in variable is read for the rows of the variables page. The rows ask the run's own
    /// values first and fall back to this, so what the page shows is what a macro would read at that
    /// moment — the round of a loop and the name of the macro included.
    /// </summary>
    private readonly SystemVariableReader _system;

    private CancellationTokenSource? _cancellation;
    private TaskCompletionSource<bool>? _gate;
    private TaskCompletionSource<StepErrorChoice>? _answer;
    private TaskCompletionSource<AskAnswer>? _asked;
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

    /// <param name="triggers">
    /// The service that watches for the macros of the list to be wanted. Handed in so the window can
    /// say what each armed trigger is waiting for: when a macro that was expected did not run, the
    /// answer is nearly always in that list.
    /// </param>
    /// <param name="macroName">
    /// The name of the macro these steps belong to, where there is one. The editor hands in the macro
    /// it is editing, so a run started there says which macro it is; a card's 「运行调试」 says so
    /// through its own call.
    /// </param>
    public RunViewModel(IEnumerable<MacroStep> steps, IDeviceLayer devices, double delayScale = 1,
        IMacroLibrary? macros = null, IReadOnlyList<MacroItem>? listed = null,
        MacroTriggerService? triggers = null, string macroName = "")
    {
        _devices = devices;
        _delayScale = delayScale;
        _macros = macros;
        _listed = listed ?? [];
        _triggers = triggers;
        Debugged = macroName;
        _system = new SystemVariableReader(_devices);
        _variables = MacroVariables.Seed(_system);
        NewType = Types[0];

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
        _choices = VariableChoices.For(source);
        _steps = source.ToExecutable();
        _watched = VariablesUsed(source);

        _breakpoints.Clear();
        _breakpointConditions.Clear();
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

        // Whether there is anything to run is only known here, and the window was drawn before that
        // when a macro is loaded into one that had none: the toolbar and the run buttons are drawn
        // for "nothing to run" and would stay that way without being told. The card's own menu takes
        // exactly that road — it opens the window empty and then loads the macro into it.
        OnPropertyChanged(nameof(HasSteps));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStep));
        RunCommand.NotifyCanExecuteChanged();
        StepCommand.NotifyCanExecuteChanged();
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
        _exclusiveInput = macro.ExclusiveInput;
        OnPropertyChanged(nameof(ShowsInputOwner));
        OnPropertyChanged(nameof(InputOwnerLine));
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
    /// Starts debugging the macro the picker is on, which is what the window opened from the main
    /// window offers instead of the run controls. It takes the card menu's own road, because it is
    /// the same thing asked for from a different place.
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

    /// <summary>
    /// What each armed trigger is waiting for, one line each. A macro that was expected to run and
    /// did not is nearly always a trigger whose waiting is not true, and this is where that is read.
    /// Only the triggers something looks at on a clock are here: a key binding answers a key, so
    /// there is no moment it was last looked at.
    /// </summary>
    public ObservableCollection<TriggerWatchLine> Triggers { get; } = [];

    public bool HasTriggers => Triggers.Count > 0;

    /// <summary>Reads the trigger service again, which the window does while it is open.</summary>
    public void RefreshTriggers()
    {
        Triggers.Clear();

        if (_triggers is { } service)
        {
            foreach (var watch in service.Watches())
            {
                Triggers.Add(TriggerWatchLine.Of(watch));
            }
        }

        OnPropertyChanged(nameof(HasTriggers));
    }

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

    /// <summary>
    /// Carries on without carrying out the step the run is standing in front of. It is what a pause
    /// is for other than looking: a step that would do something the user does not want done — on
    /// this machine, with these values — is left out, and the run goes on to whatever comes next.
    /// </summary>
    [RelayCommand(CanExecute = nameof(IsPaused))]
    private void SkipStep()
    {
        if (_gate is null)
        {
            // Nothing is waiting in front of a step, so there is no step to leave out: setting the
            // flag now would leave out whichever step happened to run next.
            return;
        }

        _skipThisStep = true;
        Release();
    }

    /// <summary>True while a run is waiting in front of a step for the user to say what happens next.</summary>
    public bool IsPaused => _gate is not null;

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

            // A breakpoint that is gone has nothing left to wait for: a condition kept behind it
            // would turn up again on the next breakpoint put on the same step.
            _breakpointConditions.Remove(row.Id);
            row.Waits(null);
        }
    }

    /// <summary>
    /// Gives a step's breakpoint a condition to wait for, turning the breakpoint on with it: a
    /// condition is what a breakpoint is about, and setting one on a step that has no breakpoint
    /// means the user wants that step to stop when the condition holds.
    /// </summary>
    public void ConditionBreakpoint(RunStepViewModel? row, MacroStep condition)
    {
        if (row is null)
        {
            return;
        }

        _breakpoints.Add(row.Id);
        _breakpointConditions[row.Id] = condition;
        row.HasBreakpoint = true;
        row.Waits(condition);
    }

    /// <summary>Puts a breakpoint back to stopping every time it is reached.</summary>
    public void ClearBreakpointCondition(RunStepViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        _breakpointConditions.Remove(row.Id);
        row.Waits(null);
    }

    /// <summary>The condition a step's breakpoint waits for, or null while it always stops.</summary>
    public MacroStep? BreakpointCondition(string stepId)
        => _breakpointConditions.TryGetValue(stepId, out var condition) ? condition : null;

    /// <summary>
    /// The names a breakpoint's condition may use: the same ones the macro's own fields are offered,
    /// which is what makes a condition written here read the way the rest of the macro reads.
    /// </summary>
    public IReadOnlyList<VariableChoice> Choices => _choices;

    /// <summary>
    /// The devices this run is on, so a dialog opened from the window tries its work out on the same
    /// machine — and, in a check, on the same stand-in.
    /// </summary>
    internal IDeviceLayer Devices => _devices;

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

    /// <summary>Answers the question the macro itself asked.</summary>
    [RelayCommand]
    private void AnswerUser(string? choice)
        => _asked?.TrySetResult(choice == "ok"
            ? new AskAnswer(true, AnswerText)
            : new AskAnswer(false, AnswerText));

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
        IsAskingUser = false;
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
                RunName = Debugged,
                ExclusiveInput = _exclusiveInput,
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

    /// <summary>
    /// Highlights the step that is about to run and stops when asked to: single stepping, a
    /// breakpoint, or a breakpoint whose condition holds. The answer is whether the step is to be
    /// carried out at all, which is what the pause's "skip this step once" says no to.
    /// </summary>
    public async Task<bool> BeforeStep(ExecutableStep step, int depth, CancellationToken token)
    {
        // Whatever the last step was reading, this one is not reading it yet: the frame comes down
        // and goes up again when this step's own reading is reported.
        WindowRead?.Invoke(null);

        CurrentId = step.Id;
        RefreshVariables();

        if (!_stopAtNextStep && !StopsHere(step.Id))
        {
            return true;
        }

        _stopAtNextStep = false;
        Status = Strings.Get("Run.Paused");

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _gate = gate;
        OnPropertyChanged(nameof(IsPaused));
        SkipStepCommand.NotifyCanExecuteChanged();
        using var registration = token.Register(() => gate.TrySetResult(false));

        var go = await gate.Task;
        _gate = null;
        OnPropertyChanged(nameof(IsPaused));
        SkipStepCommand.NotifyCanExecuteChanged();
        Status = Strings.Get("Run.Running");

        if (!go)
        {
            throw new OperationCanceledException(token);
        }

        var skip = _skipThisStep;
        _skipThisStep = false;
        return !skip;
    }

    /// <summary>
    /// Whether the run stops in front of this step: a breakpoint is a promise to stop, and one that
    /// carries a condition keeps that promise only while the condition holds. A condition that
    /// cannot be worked out at all stops the run too and says so in the log, because a breakpoint
    /// nobody can read is not the same thing as a breakpoint that has not been reached.
    /// </summary>
    private bool StopsHere(string stepId)
    {
        if (!_breakpoints.Contains(stepId))
        {
            return false;
        }

        if (BreakpointCondition(stepId) is not { } condition)
        {
            return true;
        }

        var answer = MacroRunner.AskCondition(new[] { condition }.ToExecutable()[0], _variables,
            _devices, _macros);
        if (answer.Key.Length == 0)
        {
            return answer.Holds;
        }

        Write(Strings.Format("Run.BreakpointUnreadable",
            answer.Detail.Length == 0 ? Strings.Get(answer.Key) : Strings.Format(answer.Key, answer.Detail)));
        return true;
    }

    /// <summary>Writes one line into the log that did not come from the run itself.</summary>
    private void Write(string text)
        => Lines.Add(new RunLineViewModel(
            DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            LogLevel.Warn, default, text));

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

    /// <summary>
    /// Puts the macro's own question on the run window and waits for the answer. The window is the
    /// one place a run under the debugger can be answered, and a run that is stopped while the
    /// question is up reads as unanswered, the same way a failure question reads as "stop".
    /// </summary>
    public async Task<AskAnswer> AskUser(AskQuestion question, CancellationToken token)
    {
        Question = question.Message;
        WantsTypedAnswer = question.Kind is AskKind.Text;
        AnswerText = question.Default;
        IsAskingUser = true;

        var answer = new TaskCompletionSource<AskAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
        _asked = answer;
        using var registration = token.Register(() => answer.TrySetResult(AskAnswer.Cancel));

        var given = await answer.Task;
        _asked = null;
        IsAskingUser = false;
        return given;
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

    /// <summary>
    /// Takes what a step came to — how long it took, how it ended, why if it failed, and everything
    /// it worked out whether or not the macro keeps any of it — and puts it on that step's row.
    /// Called from the thread the run is on, so it is handed to the window's own thread first.
    /// </summary>
    public void Ran(StepRun run) => Dispatcher.UIThread.Post(() => KeepRun(run));

    /// <summary>Fills in the panel under a step's row with what the step just came to.</summary>
    private void KeepRun(StepRun run)
    {
        if (Steps.FirstOrDefault(step => step.Id == run.StepId) is { } row)
        {
            row.Took(run);
        }
    }

    /// <summary>Opens one step's picture, which is the whole of what it saw rather than a thumbnail.</summary>
    public event Action<StepLook>? LookRequested;

    /// <summary>
    /// Raised when the run moves to another step, with that step's row, or null when it is not
    /// standing in front of one any more. The list follows the run rather than making the person
    /// hunt for the row: a long macro says nothing about where it is if the row is below the fold.
    /// </summary>
    public event Action<RunStepViewModel?>? CurrentChanged;

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
            RunStepViewModel? current = null;
            foreach (var row in Steps)
            {
                row.IsCurrent = string.Equals(row.Id, value, StringComparison.Ordinal);
                if (row.IsCurrent)
                {
                    current = row;
                }
            }

            CurrentChanged?.Invoke(current);
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
                CanBreak = !step.Type.StartsWith("condition.", StringComparison.Ordinal),
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

    internal static string Detail(ExecutableStep step)
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

            // What a macro would read right now, which is what this page is about: the value a step
            // has left in place comes first — the round of a loop, the macro being run — and behind it
            // the ones read off the machine as they are asked for.
            row.CurrentValue = _variables.TryGet(variable.Name, out var value)
                ? value.AsText()
                : _system.Read(variable.Name);

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
