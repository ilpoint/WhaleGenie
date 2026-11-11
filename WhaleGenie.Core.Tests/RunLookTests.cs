using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WhaleGenie.Core.Devices;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Variables;
using WhaleGenie.GraphicsCapture;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// What each step that looks at the screen hands over for a debugger to draw: the picture it
/// looked at, where that picture sits, and every mark on it.
/// </summary>
public partial class RunLookTests
{
    /// <summary>Somewhere to put the looks handed over during a run.</summary>
    private sealed class Watched : IRunLooks
    {
        public List<StepLook> Seen { get; } = [];

        /// <summary>What each step came to, in the order the steps finished.</summary>
        public List<StepRun> Runs { get; } = [];

        /// <summary>Which window each reading said it was going to read, in the order it said so.</summary>
        public List<WindowInfo?> Read { get; } = [];

        public void Look(StepLook look) => Seen.Add(look);

        public void Ran(StepRun run) => Runs.Add(run);

        public void Reading(WindowInfo? window) => Read.Add(window);
    }

    private static ExecutableStep Step(string type, params ExecutableParameter[] parameters)
        => new() { Type = type, Id = "k3f9", Parameters = parameters };

    /// <summary>See <see cref="SampleFiles.Within"/>: the same step, allowed this long.</summary>
    private static ExecutableStep Within(int milliseconds, ExecutableStep step)
        => SampleFiles.Within(milliseconds, step);

    private static ExecutableParameter Param(string name, string text = "")
        => Fields.Text(name, text);

    private static ExecutableParameter Body(string name, params ExecutableStep[] steps)
        => Fields.Steps(name, steps);

    private static ExecutableParameter When(string name, ExecutableStep step)
        => Fields.Text(name, TestConditions.Expression(step));

    /// <summary>See <see cref="SampleFiles.Returns"/>: the results a step hands over.</summary>
    private static ExecutableParameter Returns(params string[] results)
        => SampleFiles.Returns(results);

    /// <summary>See <see cref="SampleFiles.Hit"/>: a place, a picture or a program and all its parts.</summary>
    private static ExecutableParameter Hit(string key, string name, bool every = false)
        => SampleFiles.Hit(key, name, every);

    /// <summary>
    /// The pictures a step looks for, written the way the dialog writes them: one row each, tried
    /// in the order they are listed.
    /// </summary>
    private static ExecutableParameter Pictures(params string[] written)
        => new()
        {
            Name = "images",
            Rows = [.. written.Select(text => (IReadOnlyDictionary<string, string>)
                Cells.Of(("image", text)))],
        };

    /// <summary>See <see cref="SampleFiles.Capture"/>: a step that copies one part of the screen.</summary>
    private static ExecutableStep Capture(int x, int y, int width, int height,
        params ExecutableParameter[] rest) => SampleFiles.Capture(x, y, width, height, rest);

    /// <summary>See <see cref="SampleFiles.Recognize"/>: a step that reads one part of the screen.</summary>
    private static ExecutableStep Recognize(int x, int y, int width, int height,
        params ExecutableParameter[] rest) => SampleFiles.Recognize(x, y, width, height, rest);
}
