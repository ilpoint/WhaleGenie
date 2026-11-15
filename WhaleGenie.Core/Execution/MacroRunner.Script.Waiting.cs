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
    // The waits themselves live in Pacing so a family can wait without the runner; these keep the
    // many call sites reading as they did while the families still live here.
    private static Task Pause(int milliseconds, CancellationToken token)
        => Pacing.Pause(milliseconds, token);

    private static Task<bool> WaitForFlagAsync(Func<bool> ready, int timeoutMs, int interval,
        CancellationToken token)
        => Pacing.WaitForFlagAsync(ready, timeoutMs, interval, token);

    private static Task<T?> WaitForValueAsync<T>(Func<T?> probe, int timeoutMs, int interval,
        CancellationToken token)
        where T : class
        => Pacing.WaitForValueAsync(probe, timeoutMs, interval, token);

    private static Task WaitForFlagAsync(Func<bool> ready, int interval, CancellationToken token)
        => Pacing.WaitForFlagAsync(ready, interval, token);

    /// <summary>
    /// The beat a step that looks at the screen keeps: the run's frame gap, which is the display's
    /// own rate unless the settings asked for a slower one. A picture that has not been drawn again
    /// cannot differ from the one just read, so a look that comes sooner reads the same frame twice.
    /// The beat is the run's rather than the step's, because a loop of ten vision steps was taking
    /// ten pictures a pass of a screen that cannot change between them.
    /// </summary>
    private int LookBeat() => FrameGap();

    /// <summary>
    /// The script actions, each answered by the method written for it. A step of
    /// another family is not in this table.
    /// </summary>
    private Dictionary<string, ActionHandler> ScriptActions
        => _scriptActions ??= new(StringComparer.Ordinal)
        {
            ["script.run"] = (step, depth, token) =>
            {
                RunScript(step, depth);
                return Task.FromResult(Signal.Normal);
            },

            ["script.python"] = (step, depth, token) =>
            {
                return Task.FromResult(RunPython(step, depth, token));
            },

            ["script.pip"] = (step, depth, token) =>
            {
                RunPip(step, depth, token);
                return Task.FromResult(Signal.Normal);
            },

            ["script.csharp"] = (step, depth, token) =>
            {
                return Task.FromResult(RunCSharp(step, depth, token));
            },

            ["script.ahk"] = (step, depth, token) =>
            {
                return Task.FromResult(RunAutoHotkey(step, depth, token));
            },

            ["script.nuget"] = (step, depth, token) =>
            {
                RunNuGet(step, depth, token);
                return Task.FromResult(Signal.Normal);
            },
        };

    private Dictionary<string, ActionHandler>? _scriptActions;

}
