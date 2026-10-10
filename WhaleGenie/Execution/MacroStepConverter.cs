using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Models;

namespace WhaleGenie.Execution;

/// <summary>Turns the steps the editor holds into the shape the engine runs.</summary>
public static class MacroStepConverter
{
    /// <summary>
    /// Builds the runnable tree. A step keeps the name the macro file gave it, so a breakpoint, the
    /// row a run lights up, and the result variable a condition watches are all the same step; a
    /// step that has no name yet — one built by a run started from an unsaved edit — is given one
    /// here, because the engine has nothing else to tell two steps apart by.
    /// </summary>
    public static IReadOnlyList<ExecutableStep> ToExecutable(this IEnumerable<MacroStep> steps)
    {
        var counter = new Counter();
        return [.. steps.Select(step => Convert(step, counter))];
    }

    private static ExecutableStep Convert(MacroStep step, Counter counter) => new()
    {
        Type = step.Type,
        Id = step.Id.Length > 0 ? step.Id : "s" + counter.Next(),
        Meta = step.Meta,
        Parameters = [.. step.Parameters.Select(parameter => Convert(parameter, counter))],
    };

    private static ExecutableParameter Convert(StepParameter parameter, Counter counter) => new()
    {
        Name = parameter.Name,
        Text = parameter.Value,
        Jitter = parameter.Jitter,
        Steps = [.. parameter.Steps.Select(step => Convert(step, counter))],
        Rows = [.. parameter.Rows.Select(row => (IReadOnlyDictionary<string, string>)row.Columns)],
        Condition = parameter.Condition is null ? null : Convert(parameter.Condition, counter),
    };

    /// <summary>Hands out the running number a step's identity is built from.</summary>
    private sealed class Counter
    {
        private int _value;

        public int Next() => _value++;
    }
}
