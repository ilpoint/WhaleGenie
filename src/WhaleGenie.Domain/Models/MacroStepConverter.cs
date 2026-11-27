using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;

namespace WhaleGenie.Models;

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

    /// <summary>
    /// One parameter as the engine sees it. The value is the shape the editor already keeps: a
    /// field is read where it is written, by the editor that knows what the field is for, and
    /// nothing reads it again here — a second reading would be a second place to get it wrong.
    /// </summary>
    private static ExecutableParameter Convert(StepParameter parameter, Counter counter) => new()
    {
        Name = parameter.Name,
        Written = parameter.Written,
        Jitter = parameter.Jitter,
        Steps = [.. parameter.Steps.Select(step => Convert(step, counter))],
        Rows = [.. parameter.Rows.Select(Row)],
    };

    /// <summary>
    /// One row of a list parameter. Every column says a piece of what the row is, so a name written
    /// in one is read where it stands.
    /// </summary>
    /// <summary>
    /// A row copied out of the editor's own: the cells are a shape the editor writes in place as
    /// the person types, so handing the run the editor's dictionary would let an edit reach a macro
    /// that is already running. The copy is shallow on purpose — an expression node is immutable,
    /// so the shapes inside can be shared safely.
    /// </summary>
    private static IReadOnlyDictionary<string, ExpressionNode> Row(StepParameterRow row)
        => row.Copy().Cells;

    /// <summary>Hands out the running number a step's identity is built from.</summary>
    private sealed class Counter
    {
        private int _value;

        public int Next() => _value++;
    }
}
