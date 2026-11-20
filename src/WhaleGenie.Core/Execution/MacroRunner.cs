using System;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Walks a macro's steps and carries them out. Everything that is not a device action — the
/// control flow, the expressions, the failure rules, and the bookkeeping the step settings ask
/// for — happens here, which is what makes the engine testable on its own.
/// </summary>
/// <remarks>
/// A device action is written in its own family, one file per family, and reached through the
/// table this class collects. What the families need of a run is <see cref="IRunContext"/>, so an
/// action never holds the interpreter, and the interpreter never has to know what an action does.
/// </remarks>
public sealed partial class MacroRunner
{
    // What this class holds, by lifetime. Program-wide: the shared global table, the two doors and
    // the device layer. One run: _host, _devices, _macros, _looks and _run, which is every piece of
    // bookkeeping the run writes down — see RunState for those — together with the six layers a step
    // reads the world through: _pictures, _windows, _points, _results, _sight and _reads, and the
    // four pieces of work the run hands a step to: _calls, _timeout, _values and the action table.
    // One attempt: _here, which is what the step being tried has worked out so far — see
    // StepProgress for that. What is left in the partial files beside this one is the interpreter
    // itself: the step loop, attempts and failure rules, dispatch, loops, conditions, the held
    // input, and the one door a script runs a step through.

    private readonly IRunHost _host;
    private readonly IDeviceLayer _devices;
    private readonly IMacroLibrary _macros;
    private readonly IRunLooks? _looks;

    /// <summary>The pictures a step looks for, read once and kept while the file behind them holds still.</summary>
    private readonly ReferencePictures _pictures;

    /// <summary>The window a step names, and the rule it wrote to name it.</summary>
    private readonly WindowFacts _windows;

    /// <summary>What looking at the screen means: areas, pictures, the beat, and the marks a look leaves.</summary>
    private readonly ScreenSight _sight;

    /// <summary>Where a step's point is, and which way its input travels.</summary>
    private readonly ScreenPoints _points;

    /// <summary>The one place a step's results are written.</summary>
    private readonly StepResults _results;

    /// <summary>Reading a call: the macro it names, the values it is handed and the ones it hands back.</summary>
    private readonly CallBinder _calls;

    /// <summary>The failure a step reports when its own clock runs out.</summary>
    private readonly StepTimeout _timeout;

    /// <summary>Writing the run's own values: one name, a shared count, a list.</summary>
    private readonly ValueActions _values;

    /// <summary>Reading writing off the screen: areas, corrections, and which readings count.</summary>
    private readonly ScreenReads _reads;

    /// <summary>
    /// What this run has written down so far. Everything here belongs to one run and is emptied
    /// before the first step of the next one.
    /// </summary>
    private readonly RunState _run = new();

    /// <summary>
    /// What the step being attempted has worked out so far. It is emptied where an attempt begins,
    /// so a retried step starts from nothing.
    /// </summary>
    private readonly StepProgress _here = new();

    /// <summary>
    /// The failure this step reports when its own clock runs out, in the words its kind of step
    /// says it in. A step run on its own (<see cref="StepTrial"/>) is answered with the same
    /// failure a run would have written, so the dialog and the run agree about what went wrong.
    /// </summary>
    internal StepFailure TimeoutOf(ExecutableStep step) => _timeout.For(step);

    /// <summary>
    /// How long the clean-up blocks of one stopped run may take together. A stop that waited for
    /// every clean-up block to finish on its own could be held up by the very step the user is
    /// trying to get out of, so the budget is a stop's own, shared by all of its clean-up blocks.
    /// </summary>
    private static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(5);
}
