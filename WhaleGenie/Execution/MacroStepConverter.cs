using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Execution;

/// <summary>Turns the steps the editor holds into the shape the engine runs.</summary>
public static class MacroStepConverter
{
    /// <summary>
    /// Builds the runnable tree. Every step gets an identity on the way, which is what the
    /// debugger uses for breakpoints and for highlighting the step that is running.
    /// </summary>
    public static IReadOnlyList<ExecutableStep> ToExecutable(this IEnumerable<MacroStep> steps)
    {
        var counter = new Counter();
        return [.. steps.Select(step => Convert(step, counter))];
    }

    private static ExecutableStep Convert(MacroStep step, Counter counter) => new()
    {
        Type = step.Type,
        Id = "s" + counter.Next(),
        Meta = step.Meta,
        Parameters = [.. step.Parameters.Select(parameter => Convert(parameter, counter))],
    };

    private static ExecutableParameter Convert(StepParameter parameter, Counter counter) => new()
    {
        Name = parameter.Name,
        Text = parameter.Value,
        Jitter = parameter.Jitter,
        Steps = [.. parameter.Steps.Select(step => Convert(step, counter))],
        Condition = parameter.Condition is null ? null : Convert(parameter.Condition, counter),
    };

    /// <summary>Hands out the running number a step's identity is built from.</summary>
    private sealed class Counter
    {
        private int _value;

        public int Next() => _value++;
    }
}
