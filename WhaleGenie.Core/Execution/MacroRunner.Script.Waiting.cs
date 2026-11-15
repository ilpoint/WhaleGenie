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
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;


namespace WhaleGenie.Core.Execution;

// Waiting helpers the families share, and the script dispatch at the end.
public sealed partial class MacroRunner
{
    private static async Task Pause(int milliseconds, CancellationToken token)
    {
        if (milliseconds > 0)
        {
            await Task.Delay(milliseconds, token);
        }
    }

    /// <summary>
    /// Waits for something to become true, asking again every <paramref name="interval"/> and giving
    /// up after <paramref name="timeoutMs"/>. Waiting is the same thing whether it is an element, a
    /// window, a program or a condition the macro wrote itself, so the pacing and the way a timeout
    /// is counted are written once. The question is asked before the clock is read, so a wait that
    /// already holds costs nothing. Zero means no limit of its own: the step's cancellation token is
    /// then what ends the wait, including a step that was never given a timeout and runs until the
    /// macro is stopped.
    /// </summary>
    /// <summary>
    /// The beat a step that looks at the screen keeps: the run's frame gap, which is the display's
    /// own rate unless the settings asked for a slower one. A picture that has not been drawn again
    /// cannot differ from the one just read, so a look that comes sooner reads the same frame twice.
    /// The beat is the run's rather than the step's, because a loop of ten vision steps was taking
    /// ten pictures a pass of a screen that cannot change between them.
    /// </summary>
    private int LookBeat() => FrameGap();

    private async Task<bool> WaitForFlagAsync(Func<bool> ready, int timeoutMs, int interval,
        CancellationToken token)
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (ready())
            {
                return true;
            }

            if (timeoutMs > 0
                && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeoutMs)
            {
                return false;
            }

            await Pause(interval, token);
        }
    }

    /// <summary>The same wait, for something that turns up rather than becomes true.</summary>
    private async Task<T?> WaitForValueAsync<T>(Func<T?> probe, int timeoutMs, int interval,
        CancellationToken token)
        where T : class
    {
        var started = Stopwatch.GetTimestamp();
        while (true)
        {
            if (probe() is { } found)
            {
                return found;
            }

            if (timeoutMs > 0
                && Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeoutMs)
            {
                return null;
            }

            await Pause(interval, token);
        }
    }

    /// <summary>
    /// Waits for something to become true, asking again every <paramref name="interval"/> until it
    /// does. How long that may take is not the wait's to say: the step's own timeout is what ends
    /// it, which is what makes the length of a wait a setting of the step rather than a field that
    /// says the same thing a second time.
    /// </summary>
    private static async Task WaitForFlagAsync(Func<bool> ready, int interval, CancellationToken token)
    {
        while (!ready())
        {
            await Pause(interval, token);
        }
    }

    /// <summary>
    /// The script actions, each answered by the method written for it. Step types of
    /// other families are not this dispatch's to answer.
    /// </summary>
    private async Task<Signal> DispatchScript(ExecutableStep step, int depth,
        CancellationToken token)
    {
        switch (step.Type)
        {
            case "script.run":
                RunScript(step, depth);
                return Signal.Normal;
            case "script.python":
                return RunPython(step, depth, token);
            case "script.pip":
                RunPip(step, depth, token);
                return Signal.Normal;
            case "script.csharp":
                return RunCSharp(step, depth, token);
            case "script.ahk":
                return RunAutoHotkey(step, depth, token);
            case "script.nuget":
                RunNuGet(step, depth, token);
                return Signal.Normal;
            default:
                throw new StepFailure(RunKeys.Unsupported, step.Type);
        }
    }
}
