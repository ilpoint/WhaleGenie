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

// Writing values: variables, shared counts, list changes and the questions put to a person.
public sealed partial class MacroRunner
{
    private void SetVariable(ExecutableStep step, int depth)
    {
        var key = ReadNameKey(step, "name");
        var name = key.Name.Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        key = key with { Name = name };
        var target = Target(step, key);
        var value = ReadValue(step, "value");
        Variables.Set(key, value, target);
        Log(LogLevel.Info, depth, step.Type, LogKeys.Set, name, value.AsText());
    }

    /// <summary>The key a Fail step reports, either the engine's default or the macro's own.</summary>
    private string FailureKeyOf(ExecutableStep step)
    {
        var key = ReadText(step, "failureKey").Trim();
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
        var message = ReadValue(step, "message").AsText();
        var detail = ReadValue(step, "detail").AsText();
        if (message.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingQuestion);
        }

        var question = detail.Length == 0 ? message : message + "\n\n" + detail;
        var answer = await _host.AskUser(new AskQuestion(AskKind.Confirm, question, string.Empty), token);

        Store(step, "confirmed", Value.FromBool(answer.Confirmed), depth);
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
        var message = ReadValue(step, "message").AsText();
        if (message.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingQuestion);
        }

        var suggestion = ReadValue(step, "default").AsText();
        var answer = await _host.AskUser(new AskQuestion(AskKind.Text, message, suggestion), token);

        Store(step, "text", Value.FromText(answer.Text), depth);
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
        var key = ReadNameKey(step, "name");
        var name = key.Name.Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        key = key with { Name = name };
        var amount = ReadValue(step, "amount").AsNumber();
        double sum;
        try
        {
            if (!Variables.Global.TryAddNumber(key, amount, out sum))
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
        Give(step, "value", Value.FromNumber(sum));
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
        var key = ReadNameKey(step, "name");
        var name = key.Name.Trim();
        if (name.Length == 0)
        {
            throw new StepFailure(RunKeys.MissingVariable);
        }

        key = key with { Name = name };
        var bag = Variables.Local.Contains(key) ? Variables.Local : Variables.Global;
        if (!bag.TryUpdate(key, current => Changed(step, current, name), out var list))
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
                items.Add(ReadValue(step, "value"));
                break;
            case "control.listInsert":
                items.Insert(Math.Clamp(Number(step, "listIndex"), 0, items.Count),
                    ReadValue(step, "value"));
                break;
            case "control.listSet":
                items[Index(step, items.Count)] = ReadValue(step, "value");
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
        var index = Number(step, "listIndex");
        if (index < 0)
        {
            index += count;
        }

        if (index < 0 || index >= count)
        {
            throw new StepFailure(RunKeys.IndexOutOfRange, ReadText(step, "listIndex"));
        }

        return index;
    }

    private VariableTarget Target(ExecutableStep step, VariableKey key)
    {
        if (!string.Equals(ReadText(step, "scope"), "global", StringComparison.OrdinalIgnoreCase))
        {
            return VariableTarget.Local;
        }

        // Only the Variable Center creates shared variables, so a macro may write to one
        // that already exists but never invent a new one.
        if (!Variables.Global.Contains(key))
        {
            throw new StepFailure(RunKeys.GlobalMustExist, key.Name);
        }

        return VariableTarget.Global;
    }
}
