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
using WhaleGenie.Localization;
using WhaleGenie.Models;
using WhaleGenie.Runtime;
using WhaleGenie.Storage;

namespace WhaleGenie.ViewModels;

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

    /// <summary>
    /// The macro's own input ceiling, or null to follow the program. It is replaced whenever the
    /// window is pointed at another macro, because it belongs to the macro being run rather than to
    /// the window.
    /// </summary>
    private int? _inputRate;
    /// <summary>The steps a run stops in front of, named the way a step's results name it.</summary>
    private readonly HashSet<string> _breakpoints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The ids of this macro's own steps. A called macro's steps go past the same host, and one of
    /// its ids has no business making one of these look as though it had run.
    /// </summary>
    private readonly HashSet<string> _ownIds = new(StringComparer.Ordinal);

    /// <summary>
    /// The steps the run has actually arrived at. Before it starts, every step is written as
    /// "skipped" so a condition about one further down reads a word rather than nothing — which
    /// means a step merely having a value says nothing about whether it ran. Single stepping made
    /// that plain: the whole list said "skipped" at the first pause, including steps the run had
    /// not reached and might never reach.
    /// </summary>
    private readonly HashSet<string> _reached = new(StringComparer.Ordinal);

    /// <summary>
    /// What each breakpoint waits for: how many arrivals count, and an expression that has to hold.
    /// A breakpoint with no entry here stops the moment the run reaches it.
    /// </summary>
    private readonly Dictionary<string, BreakpointRule> _breakpointRules =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>How many times the run has arrived at each breakpoint since it started.</summary>
    private readonly Dictionary<string, int> _breakpointHits = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True while the step in front of the run is to be left out: what "skip this step once" sets,
    /// and what the engine reads back as the answer to its call before the step.
    /// </summary>
    private bool _skipThisStep;

    /// <summary>
    /// Whose turn it is at the input door, as a line to read. The door is only ever held by a
    /// control.holdInput block, so the line has something to say exactly while one of those is
    /// running.
    /// </summary>
    public string InputOwnerLine => Strings.Format("Run.InputOwner", SharedDoors.Input.Owner);

    /// <summary>True while some run holds the input door, so the line means something.</summary>
    public bool ShowsInputOwner => SharedDoors.Input.Owner.Length > 0;

    /// <summary>The door changed hands, so what the line says has changed with it.</summary>
    public void RefreshInputOwner()
    {
        OnPropertyChanged(nameof(InputOwnerLine));
        OnPropertyChanged(nameof(ShowsInputOwner));
    }

    /// <summary>
    /// Which run is using the shared browser right now. There is one browser and one current page
    /// for the whole program, so a browser step can wait for another macro just as an input step
    /// waits at the input door; this is the line that says who has it.
    /// </summary>
    public string BrowserOwnerLine => SharedDoors.Browser.Owner.Length == 0
        ? string.Empty
        : Strings.Format("Run.BrowserOwner", SharedDoors.Browser.Owner);

    /// <summary>True while some run is using the browser, so the line has something to say.</summary>
    public bool ShowsBrowserOwner => SharedDoors.Browser.Owner.Length > 0;

    /// <summary>The browser door changed hands.</summary>
    public void RefreshBrowserOwner()
    {
        OnPropertyChanged(nameof(BrowserOwnerLine));
        OnPropertyChanged(nameof(ShowsBrowserOwner));
    }

    /// <summary>The names a breakpoint's condition may use, read off the macro being debugged.</summary>
    private IReadOnlyList<VariableChoice> _choices = [];

    /// <summary>
    /// The macros this run may call, when the window was handed them outright: the editor's own run
    /// knows exactly which steps are being run, and it is those the macro being debugged is.
    /// </summary>
    private readonly IMacroLibrary? _macros;

    /// <summary>
    /// What the loaded macro declares, when the window was handed it: the debug window needs the
    /// interface to offer parameters and results while the macro is being worked on, and the direct
    /// run needs the declarations to seed them.
    /// </summary>
    private MacroCallable? _callable;

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

    /// <summary>The table this run shares with everything else in the project.</summary>
    private readonly VariableSession _session;

    private CancellationTokenSource? _cancellation;
    private RunLogCapture? _capture;
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
        MacroTriggerService? triggers = null, string macroName = "",
        MacroCallable? callable = null, int? inputRate = null, VariableSession? variables = null)
    {
        _devices = devices;
        _session = variables ?? new VariableSession();
        _delayScale = delayScale;
        _inputRate = inputRate;
        _macros = macros;
        _listed = listed ?? [];
        _triggers = triggers;
        _callable = callable;
        Debugged = macroName;
        _system = new SystemVariableReader(_devices);
        _variables = MacroVariables.Seed(_system, _session);

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
        BuildArguments();
        var source = steps as IReadOnlyList<MacroStep> ?? [.. steps];
        _choices = VariableChoices.For(source, _session.Globals, _callable?.Parameters,
            _callable?.Results);
        _steps = source.ToExecutable();
        _watched = VariablesUsed(source);

        _breakpoints.Clear();
        _breakpointRules.Clear();
        _breakpointHits.Clear();
        _ownIds.Clear();
        _reached.Clear();
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
        StatusKindValue = RunStatusKind.Ready;

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
}
