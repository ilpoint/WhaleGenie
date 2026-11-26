using System;
using System.Collections.Generic;
using System.Threading;
using WhaleGenie.Core.Devices;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// Everything one run writes down while it goes: the pictures it took, the keys it is still
/// holding, how deep it has called, and why it stopped. It is a plain object rather than fields on
/// the runner so that the state of a run has one owner and one place to look, and so that the
/// interpreter that walks the steps is not also the place every piece of bookkeeping lives.
/// </summary>
/// <remarks>
/// One instance belongs to one run and is emptied when a run starts, never shared between runs or
/// between a run and the step a dialog is trying out. What belongs to the program rather than to a
/// run — the device layer, the macro library, the variables of the caller — stays on the runner.
/// </remarks>
internal sealed class RunState
{
    /// <summary>Pictures captured during the run, kept under the name they were saved to.</summary>
    public Dictionary<string, ImageFrame> Images { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The steps this run has already been told yes to, by the identity written into the file. A
    /// loop that repeats a step a hundred times asks once: the question is about the step, not about
    /// this time round. A run that is started again asks again, which is why the set is emptied at
    /// the start of every run rather than kept beside the runner.
    /// </summary>
    public HashSet<object> Agreed { get; } = [];

    /// <summary>
    /// Reference pictures read from disk, kept beside the time the file was last written. A macro
    /// that looks for the same picture in a loop — or every two hundred milliseconds while it
    /// waits — reads and decodes it once instead of every time round. A picture that has been
    /// changed on disk no longer matches the stamp and is read again.
    /// </summary>
    /// <remarks>
    /// Unlike the rest of this object it outlives a run: the stamp decides whether the copy is
    /// still the file's, so a runner asked to run the same macro twice reads the picture once. It
    /// is emptied with the runner, not at the start of a run.
    /// </remarks>
    public Dictionary<string, (DateTimeOffset Written, ImageFrame Frame)> References { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 这一轮里最近一次由步骤自己指定的窗口规则。后面的步骤说"沿用上一次"时取的就是它：宏做十
    /// 件事都在同一个窗口里，没有道理十次都把窗口重新说一遍。记的是**规则**而不是找到的句柄，
    /// 所以窗口关掉再开、标题变了，后面照样按同一句话重新找。
    /// </summary>
    public WindowRule LastWindowRule { get; set; } = WindowRule.Empty;

    /// <summary>
    /// The steps this run was handed, kept so that a script can name one of them. Nothing else reads
    /// it: what a step is found by is the identity the editor writes into the file, and the steps of
    /// a macro called from this one belong to that call rather than to this list.
    /// </summary>
    public IReadOnlyList<ExecutableStep> Steps { get; set; } = [];

    /// <summary>
    /// The lines this run has written so far, kept so that one which stops on a failure can leave
    /// the whole of them behind as a text file. A run the debugger is watching shows the same lines
    /// in its window, and one a trigger started has no window at all — there this file is the only
    /// account of what happened.
    /// </summary>
    public List<LogEntry> Log { get; } = [];

    /// <summary>
    /// The failures the catch blocks now running have taken hold of, innermost last. A rethrow
    /// takes the last one, so a nested catch never reaches past the block it is written in. The
    /// list is swapped for a new one around a call to another macro: a macro may not rethrow
    /// something its caller caught.
    /// </summary>
    public List<StepFailure> Caught { get; set; } = [];

    /// <summary>Whether this run has already said that its own table is growing without a ceiling.</summary>
    public bool LocalGrowthWarned { get; set; }

    /// <summary>
    /// Whether a step a script ran asked for the whole macro to stop. A script runs to its own end —
    /// it is a program in a process of its own and there is no stopping it politely — so the answer
    /// is carried out of the step that ran it and ends the macro once that step is over.
    /// </summary>
    public bool StoppedInsideScript { get; set; }

    /// <summary>
    /// Whether a step a script ran asked to end its macro. Like a stop, the answer comes back
    /// through the script step and ends the macro once that step is over.
    /// </summary>
    public bool ReturnedInsideScript { get; set; }

    /// <summary>
    /// The keys and mouse buttons the macro has pressed and not yet let go, with the device each
    /// was pressed on so it is let go the same way — a key posted at one window has to be released
    /// at that window, not at whatever has the focus when the run ends.
    /// </summary>
    public List<(IInputDevice Device, string Name)> HeldKeys { get; } = [];

    public List<(IInputDevice Device, string Button, ScreenPoint Point)> HeldButtons { get; } = [];

    /// <summary>Whether a controller was driven, so the run lets go of it the way it does a key.</summary>
    public bool GamepadUsed { get; set; }

    /// <summary>
    /// How many browser steps of this run are inside the browser door right now. A block such as
    /// <c>browser.dialog</c> holds the door while its body runs; the body's own browser steps are
    /// the same turn at the page and must not wait for the door they already hold.
    /// </summary>
    public int BrowserHolds { get; set; }

    /// <summary>
    /// How many input-holding blocks are around the step being run. The block asked for the door so
    /// its steps would send their keys as one turn, so the steps inside must not ask for it again.
    /// </summary>
    public int InputHolds { get; set; }

    /// <summary>
    /// How many loops the run is inside right now. A break or a continue only means something
    /// while this is above zero; one that reaches the top of a macro has nothing to act on.
    /// </summary>
    public int Loops { get; set; }

    /// <summary>
    /// How many switches the run is inside right now. A break inside a switch leaves that switch
    /// rather than the loop around it, so a switch on its own is a place a break can stand.
    /// </summary>
    public int Switches { get; set; }

    /// <summary>How many steps this run has worked to the end, which is what its result reports.</summary>
    public int Executed { get; set; }

    /// <summary>How many calls deep this run is, counted against the same limit for every caller.</summary>
    public int CallDepth { get; set; }

    /// <summary>
    /// The cancellation token of the run as a whole, kept so a background run started by a step
    /// stops with the macro rather than with that one step's timeout.
    /// </summary>
    public CancellationToken RunToken { get; set; }

    /// <summary>The one budget a stop's clean-up blocks spend, made when the first one runs.</summary>
    public CancellationTokenSource? Cleanup { get; set; }

    /// <summary>
    /// The state a run begins in: nothing seen, nothing held, nothing counted, and why it ended not
    /// yet decided. It is called before the first step rather than after the last so that a run
    /// that is started twice cannot carry anything of the first one into the second.
    /// </summary>
    public void Begin(CancellationToken token, IReadOnlyList<ExecutableStep> steps, int callDepth)
    {
        RunToken = token;
        Steps = steps;
        CallDepth = callDepth;
        Executed = 0;
        Loops = 0;
        Switches = 0;
        BrowserHolds = 0;
        InputHolds = 0;
        LocalGrowthWarned = false;
        StoppedInsideScript = false;
        ReturnedInsideScript = false;
        GamepadUsed = false;
        LastWindowRule = WindowRule.Empty;

        Cleanup?.Dispose();
        Cleanup = null;

        Images.Clear();
        Agreed.Clear();
        Log.Clear();
        HeldKeys.Clear();
        HeldButtons.Clear();
        Caught = [];
    }
}
