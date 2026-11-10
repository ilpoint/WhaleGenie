using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Devices.Platform;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
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
    // and the device layer. One run: _host, _devices, _macros, _looks, Variables, _log, _steps,
    // _frames, _references, _lastFrame and _runToken. One step: _failure, _worked, _saw,
    // _read, _takenWindow, _takenMethod, _heldKeys, _heldButtons, _gamepadUsed and _browserHolds.
    // The family methods live beside this file in MacroRunner.<Family>.cs. A method that more than
    // one family uses stayed in the file that first needed it, so before assuming a family file
    // holds only its own steps: Give (the argument reader every family reads through) is in
    // .Window.cs, Pause and WaitForFlagAsync in .Script.cs, Gate (the capture throttle) in
    // .Detection.cs, and the trial look (CanLook / LookOnce / SeeOnce) in .Input.cs.

    private readonly IRunHost _host;
    private readonly IDeviceLayer _devices;
    private readonly IMacroLibrary _macros;
    private readonly IRunLooks? _looks;

    /// <summary>
    /// What the step being run has worked out so far, newest last, or null when nobody is watching
    /// this run. A step that holds steps hands its own list back to the one around it once the steps
    /// inside are done, so every result is filed under the step that worked it out.
    /// </summary>
    private List<StepResult>? _worked;

    /// <summary>Pictures captured during the run, kept under the name they were saved to.</summary>
    private readonly Dictionary<string, ImageFrame> _images = new(StringComparer.OrdinalIgnoreCase);

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
    /// 这一轮里最近一次由步骤自己指定的窗口规则。后面的步骤说"沿用上一次"时取的就是它：宏做十
    /// 件事都在同一个窗口里，没有道理十次都把窗口重新说一遍。记的是**规则**而不是找到的句柄，
    /// 所以窗口关掉再开、标题变了，后面照样按同一句话重新找。
    /// </summary>
    private WindowRule _lastWindowRule = WindowRule.Empty;

    /// <summary>
    /// The last picture taken of each source, kept for as long as the frame policy says a picture
    /// stays worth reading. A macro that looks at the same window several times in a row then reads
    /// it once, which is what a budget of looks is really about. It lives for one run: what a macro
    /// sees is what it saw while it was running, and the next run starts by looking again.
    /// </summary>
    private readonly Dictionary<(long Source, CaptureWay Method),
        (ImageFrame Frame, ScreenPoint Origin, CaptureWay Method, long TakenAt)> _frames = [];

    /// <summary>
    /// When this run last took a picture, which is the one clock every look at the screen is held
    /// to. It is the run's clock rather than a step's: a display cannot draw anything new faster
    /// than it refreshes, so two steps reading it inside one refresh spend the second read on the
    /// same pixels. A run that is started again begins by looking, which is why it is emptied at
    /// the start rather than kept beside the runner.
    /// </summary>
    private long _lastFrame;

    /// <summary>
    /// The steps this run has already been told yes to, by the identity written into the file. A
    /// loop that repeats a step a hundred times asks once: the question is about the step, not about
    /// this time round. A run that is started again asks again, which is why the set is emptied at
    /// the start of every run rather than kept beside the runner.
    /// </summary>
    private readonly HashSet<object> _agreed = [];

    /// <summary>
    /// Reference pictures read from disk, kept beside the time the file was last written. A macro
    /// that looks for the same picture in a loop — or every two hundred milliseconds while it
    /// waits — reads and decodes it once instead of every time round. A picture that has been
    /// changed on disk no longer matches the stamp and is read again.
    /// </summary>
    private readonly Dictionary<string, (DateTime Written, ImageFrame Frame)> _references =
        new(StringComparer.OrdinalIgnoreCase);

    private int _executed;
    private int _callDepth;

    /// <summary>
    /// The cancellation token of the run as a whole, kept so a background run started by a step
    /// stops with the macro rather than with that one step's timeout.
    /// </summary>
    private CancellationToken _runToken;

    /// <summary>
    /// How long the clean-up blocks of one stopped run may take together. A stop that waited for
    /// every clean-up block to finish on its own could be held up by the very step the user is
    /// trying to get out of, so the budget is a stop's own, shared by all of its clean-up blocks.
    /// </summary>
    private static readonly TimeSpan CleanupBudget = TimeSpan.FromSeconds(5);

    /// <summary>The one budget a stop's clean-up blocks spend, made when the first one runs.</summary>
    private CancellationTokenSource? _cleanup;
}
