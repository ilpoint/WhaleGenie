using System;
using System.Collections.Generic;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Execution;

/// <summary>
/// The control actions that work on the run's own values: setting one, emptying one, adding to a
/// shared number, changing a list. They are the only actions that write a value without a device
/// being involved, so what "a variable" means — which table it lives in, what clearing it comes to,
/// and what a list change is applied to — is decided here once.
/// </summary>
internal sealed class ValueActions(IRunContext run, RunState state)
{
    /// <summary>How many names a run may keep before it says so, once.</summary>
    private const int LocalNameWarning = 50_000;

    /// <summary>How many names a run may keep at all.</summary>
    public const int LocalNameLimit = 200_000;

    /// <summary>Writes a value the macro named, in the scope it named.</summary>
    public void Set(ExecutableStep step, int depth)
    {
        var name = run.Args(step).Written("name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        var target = Target(step, name);
        var value = run.Args(step).ValueOf("value");
        target.Set(name, value);
        NoteLocalGrowth(depth);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, value.AsText());
    }

    /// <summary>
    /// Empties a name again. A local one goes back to the empty value of the kind it was declared
    /// as, or out of the table when nothing declared it, so reading it afterwards is the same
    /// failure as reading a name nothing ever wrote. A global belongs to the project and cannot
    /// be removed from inside a macro, so clearing one writes its declared starting value back.
    /// </summary>
    public void Clear(ExecutableStep step, int depth)
    {
        var name = run.Args(step).Written("name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        if (string.Equals(run.Args(step).Text("scope"), "global", StringComparison.OrdinalIgnoreCase))
        {
            if (!run.Variables.Global.Contains(name))
            {
                throw new StepFailure(RunKeys.GlobalMustExist, name);
            }

            run.Variables.Global.Set(name, run.Variables.GlobalDefault?.Invoke(name) ?? Value.Null);
            run.Log(LogLevel.Info, depth, step.Type, LogKeys.ClearedVariable, name);
            return;
        }

        run.Variables.Local.Clear(name);
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.ClearedVariable, name);
    }

    /// <summary>
    /// Says once, in the log, that a run is keeping more names than a macro usually needs. A name
    /// built while the macro runs — a row number in its spelling — is kept until the run ends, and
    /// a loop that writes a new one every round grows the table without a ceiling. Nothing is
    /// trimmed: cutting names out would lose values a macro may still read.
    /// </summary>
    private void NoteLocalGrowth(int depth)
    {
        if (state.LocalGrowthWarned || run.Variables.Local.Count <= LocalNameWarning)
        {
            return;
        }

        state.LocalGrowthWarned = true;
        run.Log(LogLevel.Warn, depth, string.Empty, LogKeys.VariableTableLarge,
            run.Variables.Local.Count);
    }

    /// <summary>
    /// Adds to a shared number as one step. Reading the old value and writing the sum from two
    /// ordinary steps can lose one of two runs' additions; here both happen under the table's lock,
    /// so every addition that was asked for is in the answer.
    /// </summary>
    public void Increment(ExecutableStep step, int depth)
    {
        var name = run.Args(step).Written("name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        var amount = run.Args(step).ValueOf("amount").AsNumber();
        double sum;
        try
        {
            if (!run.Variables.Global.TryAddNumber(name, amount, out sum))
            {
                throw new StepFailure(RunKeys.GlobalMustExist, name);
            }
        }
        catch (ExpressionException failure)
        {
            throw new StepFailure(RunKeys.NotANumber, $"{name}: {failure.Detail}");
        }

        // The count after this step, so a macro can compare it or write it down without reading
        // the same global again — a second read would also be a second look at a value another run
        // may have changed in between.
        run.Give(step, "value", Value.FromNumber(sum));
        run.Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, Value.FormatNumber(sum));
    }

    /// <summary>
    /// Changes one item of a list — or empties it — as a single step under the bag's lock: two runs
    /// appending to the same global list both see the list as it is when their turn comes, so
    /// neither writes a list that is missing the other's item. A local list is changed the same way
    /// under its own bag, which no other run touches.
    /// </summary>
    public void ChangeList(ExecutableStep step, int depth)
    {
        var name = run.Args(step).Written("name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        var bag = run.Variables.Local.Contains(name) ? run.Variables.Local : run.Variables.Global;
        if (!bag.TryUpdate(name, current => Changed(step, current, name), out var list))
        {
            throw new StepFailure(RunKeys.NotAList, name);
        }

        run.Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, list.AsText());
    }

    /// <summary>
    /// The list one change step makes out of the list that is there now. The step's own value is
    /// read here, inside the lock, so a step that names the list it changes reads it as it stands
    /// and a value that cannot be read leaves the list as it was.
    /// </summary>
    private Value Changed(ExecutableStep step, Value current, string name)
    {
        if (!current.IsList)
        {
            throw new StepFailure(RunKeys.NotAList, name);
        }

        var items = new List<Value>(current.Items);
        switch (step.Type)
        {
            case "control.listAdd":
                items.Add(run.Args(step).ValueOf("value"));
                break;
            case "control.listInsert":
                items.Insert(Math.Clamp(run.Args(step).Number("listIndex"), 0, items.Count),
                    run.Args(step).ValueOf("value"));
                break;
            case "control.listSet":
                items[Index(step, items.Count)] = run.Args(step).ValueOf("value");
                break;
            case "control.listRemoveAt":
                items.RemoveAt(Index(step, items.Count));
                break;
            default:
                items.Clear();
                break;
        }

        return Value.FromList(items);
    }

    private int Index(ExecutableStep step, int count)
    {
        var index = run.Args(step).Number("listIndex");
        if (index < 0)
        {
            index += count;
        }

        if (index < 0 || index >= count)
        {
            throw new StepFailure(RunKeys.IndexOutOfRange, run.Args(step).Text("listIndex"));
        }

        return index;
    }

    private VariableBag Target(ExecutableStep step, string name)
    {
        if (!string.Equals(run.Args(step).Text("scope"), "global", StringComparison.OrdinalIgnoreCase))
        {
            return run.Variables.Local;
        }

        // Only the Variable Center creates shared variables, so a macro may write to one
        // that already exists but never invent a new one.
        if (!run.Variables.Global.Contains(name))
        {
            throw new StepFailure(RunKeys.GlobalMustExist, name);
        }

        return run.Variables.Global;
    }
}
