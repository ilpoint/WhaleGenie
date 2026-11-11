using System.Collections.Generic;
using System.Linq;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Models;

namespace WhaleGenie.Runtime;

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
        Parameters = [.. step.Parameters.Select(parameter => Convert(step.Type, parameter, counter))],
    };

    /// <summary>
    /// One parameter as the engine sees it. The line the file holds is read into its shape here —
    /// the one place a line becomes a value — because past this point nothing reads a line.
    /// </summary>
    private static ExecutableParameter Convert(string type, StepParameter parameter,
        Counter counter) => new()
        {
            Name = parameter.Name,
            Written = FormulaText.Read(parameter.Value, ModeOf(type, parameter.Name)),
            Jitter = parameter.Jitter,
            Steps = [.. parameter.Steps.Select(step => Convert(step, counter))],
            Rows = [.. parameter.Rows.Select(Row)],
        };

    /// <summary>
    /// How the catalogue says a field is read. A field the catalogue does not know is read as one
    /// value, which is what a hand-written step's cell has always been.
    /// </summary>
    private static ActionReadMode ModeOf(string type, string name)
        => ActionSemanticsCatalog.Find(type)?.Parameter(name)?.ReadMode ?? ActionReadMode.WholeValue;

    /// <summary>
    /// One row of a list parameter. Every column says a piece of what the row is, so a name written
    /// in one is read where it stands.
    /// </summary>
    private static IReadOnlyDictionary<string, ExpressionNode> Row(StepParameterRow row)
        => row.Columns.ToDictionary(column => column.Key,
            column => FormulaText.Read(column.Value, ActionReadMode.Text));

    /// <summary>Hands out the running number a step's identity is built from.</summary>
    private sealed class Counter
    {
        private int _value;

        public int Next() => _value++;
    }
}
