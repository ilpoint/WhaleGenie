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
    // piece of bookkeeping the run writes down — see RunState for those. One step: _failure,
    // _worked, _saw, _read, _takenWindow and _takenMethod. The family methods live beside this
    // file in MacroRunner.<Family>.cs. A method that more than one family uses stayed in the file
    // that first needed it, so before assuming a family file holds only its own steps: Give (the
    // argument reader every family reads through) is in .Window.cs, Pause and WaitForFlagAsync in
    // .Script.cs, Gate (the capture throttle) in .Detection.cs, and the trial look
    // (CanLook / LookOnce / SeeOnce) in .Input.cs.

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
    /// What the step being run has worked out so far, newest last, or null when nobody is watching
    /// this run. A step that holds steps hands its own list back to the one around it once the steps
    /// inside are done, so every result is filed under the step that worked it out.
    /// </summary>
    private List<StepResult>? _worked;

    /// <summary>
    /// What the step going on right now has just seen, kept until the step is done so that one
    /// step hands over one look however many times it had to look to get there.
    /// </summary>
    private StepLook? _saw;

    /// <summary>The areas the last reading of the screen was taken from, so a look can draw them.</summary>
    private IReadOnlyList<(ImageFrame Frame, ScreenPoint Origin)> _read = [];

    /// <summary>
    /// What the last picture taken for the step going on right now was taken of: which window, and
    /// by which means. It travels with the result the step leaves behind, because a step that found
    /// nothing is answered by asking whether it was even looking at the right window.
    /// </summary>
    private string _takenWindow = string.Empty;

    private CaptureWay? _takenMethod;

    /// <summary>
    /// How long the clean-up blocks of one stopped run may take together. A stop that waited for
    /// every clean-up block to finish on its own could be held up by the very step the user is
    /// trying to get out of, so the budget is a stop's own, shared by all of its clean-up blocks.
    /// </summary>
    private static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(5);
}
