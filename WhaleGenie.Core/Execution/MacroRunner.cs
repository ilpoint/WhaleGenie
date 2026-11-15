using System;
using WhaleGenie.Core.Devices;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Walks a macro's steps and carries them out. Everything that is not a device action — the
/// control flow, the variables, the expressions, the lists, and the bookkeeping the step
/// settings ask for — happens here, which is what makes the engine testable on its own.
/// </summary>
/// <remarks>
/// Actions from <c>input</c>, <c>vision</c>, <c>ocr</c> and <c>uia</c> need the device layer
/// and are reported as unsupported until that is wired up.
/// </remarks>
public sealed partial class MacroRunner
{
    // What this class holds, by lifetime. Program-wide: the shared global table, the two gates
    // and the device layer. One run: _host, _devices, _macros, _looks and _run, which is every
    // piece of bookkeeping the run writes down — see RunState for those. One attempt: _here, which
    // is what the step being tried has worked out so far — see StepProgress for that. The family
    // methods live beside this file in MacroRunner.<Family>.cs. A method that more than one family
    // uses stayed in the file that first needed it, so before assuming a family file holds only
    // its own steps: Give (the result writer every family hands to) is in .Window.cs, Pause and
    // WaitForFlagAsync in .Script.cs, Gate (the capture throttle) in .Detection.cs, and the trial
    // look (CanLook / LookOnce / SeeOnce) in .Input.cs.

    private readonly IRunHost _host;
    private readonly IDeviceLayer _devices;
    private readonly IMacroLibrary _macros;
    private readonly IRunLooks? _looks;

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
    /// How long the clean-up blocks of one stopped run may take together. A stop that waited for
    /// every clean-up block to finish on its own could be held up by the very step the user is
    /// trying to get out of, so the budget is a stop's own, shared by all of its clean-up blocks.
    /// </summary>
    private static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(5);
}
