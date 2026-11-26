using System;
using System.Collections.Generic;
using WhaleGenie.Core.Execution;
using WhaleGenie.Core.Expressions;
using WhaleGenie.Core.Variables;

namespace WhaleGenie.Core.Tests;

/// <summary>
/// The boundary a call to another macro is made of: what the call names, what the called macro
/// declares, what it is handed, and what it may hand back. Every one of those is refused here, before
/// the called macro runs, so a misspelled name cannot leave a value nobody reads.
/// </summary>
public class CallBinderTests
{
    private sealed class Library : IMacroLibrary
    {
        public Dictionary<string, IReadOnlyList<ExecutableStep>> StepsByName { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, MacroSignature> Signatures { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Names => [.. StepsByName.Keys];

        public IReadOnlyList<ExecutableStep>? Steps(string name)
            => StepsByName.TryGetValue(name, out var steps) ? steps : null;

        public MacroSignature? Signature(string name)
            => Signatures.TryGetValue(name, out var signature) ? signature : null;
    }

    private static (CallBinder Binder, VariableStore Variables, RunState Run, Library Library) Subject(
        MacroSignature? signature = null)
    {
        var library = new Library();
        library.StepsByName["child"] = [];
        library.Signatures["child"] = signature ?? MacroSignature.Empty;

        var variables = new VariableStore();
        var run = new RunState();
        var binder = new CallBinder(new FakeDeviceLayer(), variables, library, run,
            EvaluationContext.System);
        return (binder, variables, run, library);
    }

    private static ExecutableStep Call(string macro = "child",
        IReadOnlyDictionary<string, string>[]? arguments = null,
        IReadOnlyDictionary<string, string>[]? returns = null)
    {
        var parameters = new List<ExecutableParameter> { Fields.Text("macro", macro) };
        if (arguments is not null)
        {
            parameters.Add(Fields.Rows(FieldNames.Arguments, arguments));
        }

        if (returns is not null)
        {
            parameters.Add(Fields.Rows("returns", returns));
        }

        return new ExecutableStep { Type = "control.runMacro", Parameters = [.. parameters] };
    }

    private static Dictionary<string, string> Argument(string name, string? value = null,
        bool given = false)
    {
        var row = new Dictionary<string, string> { [FieldNames.Name] = name };
        if (value is not null)
        {
            row[FieldNames.Value] = value;
        }

        if (given)
        {
            row[FieldNames.Given] = "true";
        }

        return row;
    }

    [Fact]
    public void A_call_to_a_macro_that_is_not_there_is_refused()
    {
        var (binder, _, _, _) = Subject();

        var failure = Assert.Throws<StepFailure>(() => binder.Resolve(Call("ghost")));

        Assert.Equal(RunKeys.MacroNotFound, failure.Key);
        Assert.Equal("ghost", failure.Detail);
    }

    [Fact]
    public void A_call_that_goes_too_deep_is_refused()
    {
        var (binder, _, run, _) = Subject();

        run.CallDepth = 16;
        Assert.Equal(RunKeys.MacroTooDeep,
            Assert.Throws<StepFailure>(() => binder.Resolve(Call())).Key);

        run.CallDepth = 15;
        Assert.Equal("child", binder.Resolve(Call()).Name);
    }

    [Fact]
    public void An_argument_the_called_macro_does_not_declare_is_refused()
    {
        var (binder, _, _, _) = Subject(new MacroSignature(
            [new MacroArgument("count", Formula.Nothing, false)], [], []));

        var failure = Assert.Throws<StepFailure>(
            () => binder.Resolve(Call(arguments: [Argument("nope", "1")])));

        Assert.Equal(RunKeys.UndeclaredArgument, failure.Key);
        Assert.Equal("nope", failure.Detail);
    }

    [Fact]
    public void A_second_row_for_one_argument_is_refused()
    {
        var (binder, _, _, _) = Subject(new MacroSignature(
            [new MacroArgument("count", Formula.Nothing, false)], [], []));

        var failure = Assert.Throws<StepFailure>(() => binder.Resolve(
            Call(arguments: [Argument("count", "1"), Argument("COUNT", "2")])));

        Assert.Equal(RunKeys.DuplicateArgument, failure.Key);
    }

    [Fact]
    public void A_row_with_no_name_passes_nothing()
    {
        var (binder, _, _, _) = Subject(new MacroSignature(
            [new MacroArgument("count", Formula.Nothing, false)], [], []));

        var resolved = binder.Resolve(Call(arguments: [Argument(string.Empty)]));

        Assert.True(resolved.Called.TryGet("count", out var value));
        Assert.True(value.IsNull);
    }

    [Fact]
    public void A_call_hands_the_called_macro_its_values_and_takes_the_names_it_asks_for()
    {
        var (binder, _, _, _) = Subject(new MacroSignature(
            [new MacroArgument("count", Formula.Nothing, false, ValueKind.Number)],
            [new MacroReturn("total", ValueKind.Number)],
            [new MacroLocal("step", Formula.Number(7), ValueKind.Number)]));

        var resolved = binder.Resolve(Call(
            arguments: [Argument("count", "3")],
            returns: [new Dictionary<string, string>
            {
                [FieldNames.Key] = "total",
                [FieldNames.Name] = "sum",
            }]));

        Assert.Equal("child", resolved.Name);
        Assert.True(resolved.Called.TryGet("count", out var count));
        Assert.Equal(3, count.AsNumber());
        Assert.True(resolved.Called.TryGet("total", out var total));
        Assert.True(total.IsNull);
        Assert.True(resolved.Called.TryGet("sys.macroName", out var name));
        Assert.Equal("child", name.AsText());
        Assert.Equal(("total", "sum"), Assert.Single(resolved.Wanted));
    }

    [Fact]
    public void A_macros_own_declarations_are_written_before_its_first_step()
    {
        var (binder, variables, _, _) = Subject();
        var signature = new MacroSignature([], [],
        [
            new MacroLocal("step", Formula.Number(7), ValueKind.Number),
            new MacroLocal("empty", Formula.Nothing),
        ]);

        binder.SeedLocals(signature);

        Assert.True(variables.Local.TryGet("step", out var step));
        Assert.Equal(7, step.AsNumber());
        Assert.True(variables.Local.TryGet("empty", out var empty));
        Assert.Equal(string.Empty, empty.AsText());
    }

    [Fact]
    public void A_result_the_called_macro_does_not_offer_is_refused()
    {
        var (binder, _, _, _) = Subject();

        var failure = Assert.Throws<StepFailure>(() => binder.Resolve(Call(
            returns: [new Dictionary<string, string>
            {
                [FieldNames.Key] = "nope",
                [FieldNames.Name] = "v",
            }])));

        Assert.Equal(RunKeys.UndeclaredResult, failure.Key);
    }

    [Fact]
    public void A_required_argument_with_nothing_passed_is_refused()
    {
        var (binder, _, _, _) = Subject(new MacroSignature(
            [new MacroArgument("count", Formula.Nothing, true)], [], []));

        var failure = Assert.Throws<StepFailure>(() => binder.Resolve(Call()));

        Assert.Equal(RunKeys.MissingArgument, failure.Key);
        Assert.Equal("count", failure.Detail);
    }

    [Fact]
    public void A_value_and_the_marker_saying_there_is_none_together_are_refused()
    {
        var (binder, _, _, _) = Subject(new MacroSignature(
            [new MacroArgument("count", Formula.Nothing, false)], [], []));

        var failure = Assert.Throws<StepFailure>(
            () => binder.Resolve(Call(arguments: [Argument("count", "3", given: true)])));

        Assert.Equal(RunKeys.GivenWithValue, failure.Key);
    }

    [Fact]
    public void A_default_is_used_when_the_call_passes_nothing()
    {
        var (binder, _, _, _) = Subject(new MacroSignature(
            [new MacroArgument("count", Formula.Number(5), false, ValueKind.Number)], [], []));

        var resolved = binder.Resolve(Call());

        Assert.True(resolved.Called.TryGet("count", out var count));
        Assert.Equal(5, count.AsNumber());
    }

    [Fact]
    public void A_value_of_the_wrong_kind_is_refused()
    {
        var (binder, _, _, _) = Subject(new MacroSignature(
            [new MacroArgument("count", Formula.Nothing, false, ValueKind.Number)], [], []));

        var failure = Assert.Throws<StepFailure>(
            () => binder.Resolve(Call(arguments: [Argument("count", "not a number")])));

        Assert.Equal(RunKeys.BadArgumentType, failure.Key);
    }

    [Fact]
    public void A_run_started_from_a_dialog_is_seeded_the_same_way()
    {
        var (binder, variables, _, _) = Subject();
        var signature = new MacroSignature(
            [new MacroArgument("count", Formula.Nothing, true, ValueKind.Number)], [], []);

        Assert.Equal(RunKeys.MissingArgument,
            binder.SeedStart(signature, null)!.Key);

        Assert.Null(binder.SeedStart(signature, [new MacroStartArgument("count", "4", Given: false)]));
        Assert.True(variables.Local.TryGet("count", out var count));
        Assert.Equal(4, count.AsNumber());
    }

    [Fact]
    public void A_declaration_that_cannot_be_read_stops_the_run_before_its_first_step()
    {
        var (binder, _, _, _) = Subject();
        var broken = new MacroSignature([new MacroArgument("bad name", Formula.Nothing, false)], [], []);

        Assert.Equal(RunKeys.BadDeclaration,
            binder.SeedStart(broken, null)!.Key);
    }

    [Fact]
    public void The_values_a_call_asked_for_come_back_under_the_names_it_asked_them_by()
    {
        var (binder, variables, _, _) = Subject();
        var produced = new VariableBag();
        produced.Set("total", Value.FromNumber(3));

        binder.Take([("total", "sum")], produced);
        binder.Take([("missing", "nothing")], produced);

        Assert.True(variables.Local.TryGet("sum", out var sum));
        Assert.Equal(3, sum.AsNumber());
        Assert.True(variables.Local.TryGet("nothing", out var nothing));
        Assert.True(nothing.IsNull);
    }

    [Fact]
    public void A_result_of_the_wrong_kind_is_refused_when_the_call_finishes()
    {
        var signature = new MacroSignature([], [new MacroReturn("total", ValueKind.Number)], []);
        var produced = new VariableBag();
        produced.Set("total", Value.FromText("three"));

        var failure = Assert.Throws<StepFailure>(
            () => CallBinder.CheckResults(signature, [("total", "sum")], produced));

        Assert.Equal(RunKeys.VariableTypeMismatch, failure.Key);
    }
}
