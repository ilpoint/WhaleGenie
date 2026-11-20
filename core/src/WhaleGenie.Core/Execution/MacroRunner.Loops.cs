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

namespace WhaleGenie.Core.Execution;

// Loops and branches: repeat, while, for each, counting, switch and if.
public sealed partial class MacroRunner
{
    /// <summary>
    /// Runs one round of a loop's body. Every loop goes through here so a break or a continue
    /// written inside a body finds a loop above it; the depth is what tells the two apart from
    /// one written where no loop is in reach.
    /// </summary>
    private async Task<Signal> RunBody(ExecutableStep step, int depth, CancellationToken token)
    {
        // The body is this round's: a step left out of it this time reads as skipped rather
        // than as whatever it came to in the round before.
        PrepareOutcomes(step.Children("body"));
        _run.Loops++;
        try
        {
            return await RunSteps(step.Children("body"), depth + 1, token);
        }
        finally
        {
            _run.Loops--;
        }
    }

    /// <summary>
    /// What a break or a continue asks the loop it sits in to do. With no loop above it there is
    /// nothing to ask, so the step fails instead of quietly ending the macro where it stands: a
    /// run that reports success after skipping the rest of a macro is a lie the user cannot see.
    /// </summary>
    private Signal LeaveLoop(Signal signal, ExecutableStep step)
    {
        // A break also has somewhere to go inside a switch; a continue only ever means a loop.
        var reachable = signal is Signal.Break ? _run.Loops > 0 || _run.Switches > 0 : _run.Loops > 0;
        return reachable ? signal : throw new StepFailure(LoopControlKey(signal), step.Type);
    }

    private async Task<Signal> RunForEach(ExecutableStep step, int depth, CancellationToken token)
    {
        var writtenItems = step.Written("items");
        if (!ListText.TryRead(writtenItems, Variables, out var items, out var error))
        {
            throw new StepFailure(RunKeys.BadList, error?.Detail ?? Formula.Speak(writtenItems));
        }

        var itemName = Args(step).Written("itemVariable").Trim();
        if (itemName.Length == 0)
        {
            itemName = "item";
        }

        var order = string.Equals(Args(step).Text("reverse"), "true", StringComparison.OrdinalIgnoreCase)
            ? items.Reverse()
            : items;

        // The round number is only worth having when the step asked for it by name: a variable
        // nobody asked for is one more thing to puzzle over in the variable list.
        var indexName = Args(step).Written("indexVariable").Trim();
        var interval = Pace(Args(step).Number("intervalMs"));
        var index = 0;
        using var indexScope = LoopScope.Round(Variables.Local);
        using var nameScope = LoopScope.Named(Variables.Local, true, itemName, indexName);
        foreach (var item in order)
        {
            token.ThrowIfCancellationRequested();
            indexScope.Set(VariableStore.LoopIndexName, Value.FromNumber(index));
            nameScope.Set(itemName, item);
            if (indexName.Length > 0)
            {
                nameScope.Set(indexName, Value.FromNumber(index));
            }

            var signal = await RunBody(step, depth, token);
            if (signal is Signal.Return or Signal.Stop or Signal.Failed)
            {
                return signal;
            }

            if (signal is Signal.Break)
            {
                _results.Give(step, "ended", Value.FromText("break"));
                return Signal.Normal;
            }

            index++;
            await Pacing.Pause(interval, token);
        }

        _results.Give(step, "ended", Value.FromText("items"));
        return Signal.Normal;
    }

    /// <summary>
    /// Counts from one number to another, running the body once per value. The counter is a variable
    /// the body can read, which is the difference between this and "repeat": repeating counts the
    /// rounds in the runner's head, while counting hands the number to the macro.
    /// </summary>
    private async Task<Signal> RunFor(ExecutableStep step, int depth, CancellationToken token)
    {
        var from = Args(step).Number("rangeFrom");
        var to = Args(step).Number("rangeTo");
        var stride = Args(step).Number("stepSize");
        if (stride == 0)
        {
            // A step is what the counter moves by, so a zero would never move it at all. Taking the
            // direction from where it starts and where it ends is what that can only have meant.
            stride = to >= from ? 1 : -1;
        }

        // The counter is a variable the step itself names, and a loop with no counter has nothing
        // to run its body against, so an empty field falls back to the name every for-loop uses.
        var counter = Args(step).Written("name").Trim();
        if (counter.Length == 0)
        {
            counter = "i";
        }

        var interval = Pace(Args(step).Number("intervalMs"));
        var round = 0;
        using var indexScope = LoopScope.Round(Variables.Local);
        using var nameScope = LoopScope.Named(Variables.Local, true, counter);

        for (var value = from; stride > 0 ? value <= to : value >= to; value += stride)
        {
            token.ThrowIfCancellationRequested();
            indexScope.Set(VariableStore.LoopIndexName, Value.FromNumber(round));
            nameScope.Set(counter, Value.FromNumber(value));

            var signal = await RunBody(step, depth, token);
            if (signal is Signal.Return or Signal.Stop or Signal.Failed)
            {
                return signal;
            }

            if (signal is Signal.Break)
            {
                _results.Give(step, "ended", Value.FromText("break"));
                return Signal.Normal;
            }

            round++;
            await Pacing.Pause(interval, token);
        }

        _results.Give(step, "ended", Value.FromText("range"));
        return Signal.Normal;
    }

    /// <summary>
    /// Compares one value against the values of each case in turn and runs the first case that
    /// matches. The rest are left alone, and the otherwise steps take over when nothing matches —
    /// that is what makes a switch different from a row of separate ifs.
    /// </summary>
    private async Task<Signal> RunSwitch(ExecutableStep step, int depth, CancellationToken token)
    {
        var value = Args(step).ValueOf("switchValue");
        var mode = Args(step).Text("switchMode");

        foreach (var branch in step.Children("cases"))
        {
            if (branch.Type != "control.case")
            {
                continue;
            }

            foreach (var wanted in Labels(branch))
            {
                if (CaseMatches(step, value, wanted, mode))
                {
                    var signal = await RunBranch(branch, depth, token);

                    // A branch is a block of the macro like any other, so it says how it went
                    // too. It is run from here rather than through the walk, which is why it has
                    // to be written down by hand.
                    WriteOutcome(branch, signal is Signal.Failed ? OutcomeFailed : OutcomeOk);

                    // A break written in a case leaves this switch, not the loop around it. A
                    // continue is still the loop's: it has nothing to say about a switch.
                    return signal is Signal.Break ? Signal.Normal : signal;
                }
            }
        }

        var otherwise = await RunBranch(step, depth, token, "otherwise");
        return otherwise is Signal.Break ? Signal.Normal : otherwise;
    }

    /// <summary>
    /// Runs one branch of a switch as a place a break can stand: the switch is counted while the
    /// branch runs, so a break that reaches the runner knows there is something to leave.
    /// </summary>
    private async Task<Signal> RunBranch(ExecutableStep step, int depth, CancellationToken token,
        string children = "body")
    {
        _run.Switches++;
        try
        {
            return await RunSteps(step.Children(children), depth + 1, token);
        }
        finally
        {
            _run.Switches--;
        }
    }

    /// <summary>
    /// The values a case answers to. A branch holds a list of them, and each is worked out where it
    /// stands, so a case value names a variable the same way any other value does.
    /// </summary>
    private IReadOnlyList<string> Labels(ExecutableStep branch)
    {
        var written = branch.Written("values");
        IReadOnlyList<ExpressionNode> items = written switch
        {
            ListNode list => list.Items,
            _ when Formula.IsNothing(written) => [],
            _ => [written],
        };

        return [.. items
            .Select(item => Read(item).AsText().Trim())
            .Where(label => label.Length > 0)];
    }

    /// <summary>Compares the switch's value against one case value the way the step chose.</summary>
    private bool CaseMatches(ExecutableStep step, Value value, string wanted, string mode)
    {
        switch (mode.Trim().ToLowerInvariant())
        {
            case "notequals":
                return !CaseEquals(step, value, wanted);
            case "contains":
            case "startswith":
            case "endswith":
                // The ways of comparing text are the ones the whole program shares.
                return TextCompare.Matches(value.AsText(), wanted, mode, OptionsOf(step, mode));
            case "notcontains":
                // Said as the denial of "contains" rather than as a comparison of its own: the
                // spacing and case switches then mean here exactly what they mean there, and there
                // is one place where "what counts as holding this text" is decided.
                return !TextCompare.Matches(value.AsText(), wanted, TextCompare.Contains,
                    OptionsOf(step, TextCompare.Contains));
            case "greaterthan":
                return Order(step, value.AsText(), wanted) > 0;
            case "greaterorequal":
                return Order(step, value.AsText(), wanted) >= 0;
            case "lessthan":
                return Order(step, value.AsText(), wanted) < 0;
            case "lessorequal":
                return Order(step, value.AsText(), wanted) <= 0;
            case "regex":
                try
                {
                    return TextCompare.Matches(value.AsText(), wanted, TextCompare.Regex,
                        OptionsOf(step, TextCompare.Regex));
                }
                catch (ArgumentException)
                {
                    // A pattern the machine cannot read is the macro's problem to see, not a
                    // stack trace to puzzle over, so it comes back as a failed step.
                    throw new StepFailure(RunKeys.BadPattern, wanted);
                }
            default:
                return CaseEquals(step, value, wanted);
        }
    }

    /// <summary>
    /// Equality keeps the number reading it has always had, and hands the text reading to the one
    /// shared comparison so the spacing and case switches mean the same thing here as elsewhere.
    /// </summary>
    private bool CaseEquals(ExecutableStep step, Value value, string wanted)
        => ValueRules.Same(value, Value.FromText(wanted))
           || TextCompare.Matches(value.AsText(), wanted, TextCompare.Exactly,
               OptionsOf(step, TextCompare.Exactly));

    /// <summary>
    /// Puts two texts in order for a greater-than or less-than branch: as numbers when both read
    /// as numbers, and the way the expression language compares text otherwise, so "b" after "a"
    /// works and "10" still beats "9" rather than falling behind it.
    /// </summary>
    private int Order(ExecutableStep step, string value, string wanted)
        => double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var left)
           && double.TryParse(wanted, NumberStyles.Any, CultureInfo.InvariantCulture, out var right)
            ? left.CompareTo(right)
            : string.Compare(value, wanted, OptionsOf(step, TextCompare.Contains).Comparison);

    private async Task<Signal> RunIf(ExecutableStep step, int depth, CancellationToken token)
    {
        StepChecks.RequireCondition(step, "condition");
        var matched = Check(step, "condition", token);
        return await RunSteps(step.Children(matched ? "then" : "else"), depth + 1, token);
    }
}
