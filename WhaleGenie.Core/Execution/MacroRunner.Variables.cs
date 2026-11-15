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

// Writing values: variables, shared counts, list changes and the questions put to a person.
public sealed partial class MacroRunner
{
    private void SetVariable(ExecutableStep step, int depth)
    {
        var name = Args(step).Written("name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        var target = Target(step, name);
        var value = Args(step).ValueOf("value");
        target.Set(name, value);
        NoteLocalGrowth(depth);
        Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, value.AsText());
    }

    /// <summary>
    /// Empties a name again. A local one goes back to the empty value of the kind it was declared
    /// as, or out of the table when nothing declared it, so reading it afterwards is the same
    /// failure as reading a name nothing ever wrote. A global belongs to the project and cannot
    /// be removed from inside a macro, so clearing one writes its declared starting value back.
    /// </summary>
    private void ClearVariable(ExecutableStep step, int depth)
    {
        var name = Args(step).Written("name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        if (string.Equals(Args(step).Text("scope"), "global", StringComparison.OrdinalIgnoreCase))
        {
            if (!Variables.Global.Contains(name))
            {
                throw new StepFailure(RunKeys.GlobalMustExist, name);
            }

            Variables.Global.Set(name, Variables.GlobalDefault?.Invoke(name) ?? Value.Null);
            Log(LogLevel.Info, depth, step.Type, LogKeys.ClearedVariable, name);
            return;
        }

        Variables.Local.Clear(name);
        Log(LogLevel.Info, depth, step.Type, LogKeys.ClearedVariable, name);
    }

    /// <summary>
    /// Says once, in the log, that a run is keeping more names than a macro usually needs. A name
    /// built while the macro runs — a row number in its spelling — is kept until the run ends, and
    /// a loop that writes a new one every round grows the table without a ceiling. Nothing is
    /// trimmed: cutting names out would lose values a macro may still read.
    /// </summary>
    private void NoteLocalGrowth(int depth)
    {
        if (_run.LocalGrowthWarned || Variables.Local.Count <= LocalNameWarning)
        {
            return;
        }

        _run.LocalGrowthWarned = true;
        Log(LogLevel.Warn, depth, string.Empty, LogKeys.VariableTableLarge, Variables.Local.Count);
    }

    /// <summary>The key a Fail step reports, either the engine's default or the macro's own.</summary>
    private string FailureKeyOf(ExecutableStep step)
    {
        var key = Args(step).Text("failureKey").Trim();
        if (key.Length == 0)
        {
            return RunKeys.StepFailedByMacro;
        }

        if (!UserFailureKeys.IsValid(key))
        {
            throw new StepFailure(RunKeys.BadUserFailureKey, key);
        }

        return key;
    }

    /// <summary>
    /// Puts the macro's own question to whoever is at the machine and keeps the answer. The
    /// question is written by the person who wrote the macro, so it travels as plain text rather
    /// than as a message key: the engine holds no user-facing words of its own.
    /// </summary>
    private async Task<Signal> AskPerson(ExecutableStep step, int depth, CancellationToken token)
    {
        var message = Args(step).ValueOf("message").AsText();
        var detail = Args(step).ValueOf("detail").AsText();
        if (message.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingQuestion);
        }

        var question = detail.Length == 0 ? message : message + "\n\n" + detail;
        var answer = await _host.AskUser(new AskQuestion(AskKind.Confirm, question, string.Empty), token);

        _results.Store(step, "confirmed", Value.FromBool(answer.Confirmed), depth);
        Log(LogLevel.Info, depth, step.Type,
            answer.Confirmed ? LogKeys.AskedYes : LogKeys.AskedNo, question);
        return Signal.Normal;
    }

    /// <summary>
    /// Asks for a line of writing and keeps it. A question nobody answers hands over an empty line
    /// rather than failing: a macro that needs an answer says so by looking at what it got, and one
    /// that runs under a trigger never had anybody to ask.
    /// </summary>
    private async Task<Signal> AskForText(ExecutableStep step, int depth, CancellationToken token)
    {
        var message = Args(step).ValueOf("message").AsText();
        if (message.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingQuestion);
        }

        var suggestion = Args(step).ValueOf("default").AsText();
        var answer = await _host.AskUser(new AskQuestion(AskKind.Text, message, suggestion), token);

        _results.Store(step, "text", Value.FromText(answer.Text), depth);
        Log(LogLevel.Info, depth, step.Type, LogKeys.AskedText, answer.Text);
        return Signal.Normal;
    }

    /// <summary>
    /// Adds to a shared number as one step. Reading the old value and writing the sum from two
    /// ordinary steps can lose one of two runs' additions; here both happen under the table's lock,
    /// so every addition that was asked for is in the answer.
    /// </summary>
    private void IncrementGlobal(ExecutableStep step, int depth)
    {
        var name = Args(step).Written("name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        var amount = Args(step).ValueOf("amount").AsNumber();
        double sum;
        try
        {
            if (!Variables.Global.TryAddNumber(name, amount, out sum))
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
        _results.Give(step, "value", Value.FromNumber(sum));
        Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, Value.FormatNumber(sum));
    }

    /// <summary>
    /// Changes one item of a list — or empties it — as a single step under the bag's lock: two runs
    /// appending to the same global list both see the list as it is when their turn comes, so
    /// neither writes a list that is missing the other's item. A local list is changed the same way
    /// under its own bag, which no other run touches.
    /// </summary>
    private void ChangeList(ExecutableStep step, int depth)
    {
        var name = Args(step).Written("name").Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        var bag = Variables.Local.Contains(name) ? Variables.Local : Variables.Global;
        if (!bag.TryUpdate(name, current => Changed(step, current, name), out var list))
        {
            throw new StepFailure(RunKeys.NotAList, name);
        }

        Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, list.AsText());
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
                items.Add(Args(step).ValueOf("value"));
                break;
            case "control.listInsert":
                items.Insert(Math.Clamp(Args(step).Number("listIndex"), 0, items.Count),
                    Args(step).ValueOf("value"));
                break;
            case "control.listSet":
                items[Index(step, items.Count)] = Args(step).ValueOf("value");
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
        var index = Args(step).Number("listIndex");
        if (index < 0)
        {
            index += count;
        }

        if (index < 0 || index >= count)
        {
            throw new StepFailure(RunKeys.IndexOutOfRange, Args(step).Text("listIndex"));
        }

        return index;
    }

    private VariableBag Target(ExecutableStep step, string name)
    {
        if (!string.Equals(Args(step).Text("scope"), "global", StringComparison.OrdinalIgnoreCase))
        {
            return Variables.Local;
        }

        // Only the Variable Center creates shared variables, so a macro may write to one
        // that already exists but never invent a new one.
        if (!Variables.Global.Contains(name))
        {
            throw new StepFailure(RunKeys.GlobalMustExist, name);
        }

        return Variables.Global;
    }
}
