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

// The host a C# script runs in, and the step it may call back into.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Runs a step of the macro being run, found by the identity written in the step list, and says
    /// whether it ended well. A step that fails is the step's own failure — its rule decides what
    /// happens to the rest of the macro — while a step that asks for the whole macro to stop stops
    /// it, one that returns ends it, and one that leaves a loop it was never inside is the mistake
    /// it always was.
    /// </summary>
    private bool RunStepById(string id, string type, int depth, CancellationToken token)
    {
        var wanted = id.Trim();
        var step = FindStep(_steps, wanted) ?? throw new StepFailure(RunKeys.NoSuchStep, wanted);
        Log(LogLevel.Info, depth, type, LogKeys.ScriptStep, wanted);

        var signal = RunSteps([step], depth + 1, token).GetAwaiter().GetResult();
        if (signal is Signal.Break or Signal.Continue)
        {
            throw new StepFailure(LoopControlKey(signal), wanted);
        }

        if (signal is Signal.Stop)
        {
            _stoppedInsideScript = true;
        }

        if (signal is Signal.Return)
        {
            _returnedInsideScript = true;
        }

        return signal is not Signal.Failed;
    }

    /// <summary>The step with this identity, looking inside the blocks that hold steps as well.</summary>
    private static ExecutableStep? FindStep(IReadOnlyList<ExecutableStep> steps, string id)
    {
        foreach (var step in steps)
        {
            if (string.Equals(step.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return step;
            }

            foreach (var parameter in step.Parameters)
            {
                if (FindStep(parameter.Steps, id) is { } inside)
                {
                    return inside;
                }

            }
        }

        return null;
    }

    /// <summary>
    /// The macro as a script sees it: the values it may read and write, the steps it may run, and
    /// somewhere to say something. It is the run the script is happening inside, so what a script
    /// reads is what the macro holds at that moment and what it writes is what the next step sees.
    /// </summary>
    private sealed class MacroHost(MacroRunner runner, string type, int depth, CancellationToken token)
        : IScriptHost
    {
        public Value? Read(string name) => runner.Variables.TryGet(name, out var value) ? value : null;

        /// <summary>
        /// A script writes the macro's own values, never the program's. A write to a built-in name
        /// is dropped rather than failing the script: the script asked for a value it cannot have,
        /// and what it reads next is still the clock or the clipboard it wrote over.
        /// </summary>
        public void Write(string name, Value value)
        {
            if (VariableName.IsSystem(name))
            {
                return;
            }

            runner.Variables.Set(name, value);
        }

        public bool RunStep(string id) => runner.RunStepById(id, type, depth, token);

        public void Print(string text) => runner.Log(LogLevel.Info, depth, type, LogKeys.ScriptSaid, text);
    }
}
